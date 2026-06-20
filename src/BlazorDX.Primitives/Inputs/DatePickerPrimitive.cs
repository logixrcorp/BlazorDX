using System.Globalization;
using BlazorDX.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Primitives.Inputs;

/// <summary>
/// Tier 1 headless date picker: a trigger that opens an anchored calendar with
/// month navigation, day selection, and 2D keyboard navigation. Focus stays on the
/// calendar grid; the focused day is surfaced via aria-activedescendant (the
/// WAI-ARIA grid pattern). Composes the anchored-positioning and dismissal
/// primitives; renders no markup itself.
/// </summary>
public class DatePickerPrimitive : ComponentBase, IAsyncDisposable
{
    /// <summary>A calendar always shows six weeks so the layout never reflows.</summary>
    private const int CalendarCellCount = 42;

    private bool behaviorsActive;
    private ElementReference gridElement;

    [Parameter] public DateOnly? Value { get; set; }

    // Nullable to match Value, so @bind-Value works with a DateOnly? field.
    [Parameter] public EventCallback<DateOnly?> ValueChanged { get; set; }

    [Parameter] public string Placeholder { get; set; } = "Select a date";

    [Parameter] public string Side { get; set; } = "bottom";

    [Parameter] public string Align { get; set; } = "start";

    [Parameter] public int Offset { get; set; } = 4;

    [Parameter] public int ExitDurationMs { get; set; } = 120;

    /// <summary>Culture for the month label and selected-date text (defaults to current UI culture).</summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    [Inject] private IOverlayInterop Overlay { get; set; } = default!;
    [Inject] private IAnchorInterop Anchor { get; set; } = default!;

    protected string AnchorId { get; } = $"dx-date-anchor-{Guid.NewGuid():N}";
    protected string PanelId { get; } = $"dx-date-panel-{Guid.NewGuid():N}";

    protected bool IsOpen { get; private set; }

    /// <summary>First day of the month currently shown in the calendar.</summary>
    private DateOnly viewMonth = FirstOfMonth(Today());

    /// <summary>The day the keyboard cursor is on (drives aria-activedescendant).</summary>
    private DateOnly focusedDate = Today();

    protected bool HasValue => Value is not null;

    private CultureInfo Fmt => Culture ?? CultureInfo.CurrentCulture;

    protected string DisplayText => Value?.ToString("d", Fmt) ?? Placeholder;

    protected string MonthLabel => viewMonth.ToString("MMMM yyyy", Fmt);

    protected string ActiveDescendantId => DayId(focusedDate);

    protected static IReadOnlyList<string> WeekdayHeaders { get; } =
        ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];

    /// <summary>The 42 days of the calendar grid, starting on the Sunday on/before the 1st.</summary>
    protected IEnumerable<DateOnly> CalendarDays()
    {
        DateOnly start = viewMonth.AddDays(-(int)viewMonth.DayOfWeek);
        for (int i = 0; i < CalendarCellCount; i++)
        {
            yield return start.AddDays(i);
        }
    }

    protected string DayId(DateOnly date) => $"{PanelId}-d-{date:yyyyMMdd}";

    protected bool IsToday(DateOnly date) => date == Today();

    protected bool IsSelected(DateOnly date) => Value == date;

    protected bool IsFocused(DateOnly date) => date == focusedDate;

    protected bool IsOutsideMonth(DateOnly date) => date.Month != viewMonth.Month;

    protected void CaptureGrid(ElementReference element) => gridElement = element;

    protected void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    protected void PreviousMonth() => MoveView(-1);

    protected void NextMonth() => MoveView(1);

    protected async Task SelectAsync(DateOnly date)
    {
        if (ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(date);
        }

        Close();
    }

    protected async Task OnTriggerKeyDownAsync(KeyboardEventArgs args)
    {
        if (!IsOpen && args.Key is "ArrowDown" or "Enter" or " ")
        {
            Open();
        }
        else if (IsOpen)
        {
            await OnGridKeyDownAsync(args);
        }
    }

    protected async Task OnGridKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowLeft": MoveFocus(-1); break;
            case "ArrowRight": MoveFocus(1); break;
            case "ArrowUp": MoveFocus(-7); break;
            case "ArrowDown": MoveFocus(7); break;
            case "Home": MoveFocus(-(int)focusedDate.DayOfWeek); break;
            case "End": MoveFocus(6 - (int)focusedDate.DayOfWeek); break;
            case "PageUp": MoveFocusMonths(-1); break;
            case "PageDown": MoveFocusMonths(1); break;
            case "Enter" or " ": await SelectAsync(focusedDate); break;
            case "Escape": Close(); break;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsOpen && !behaviorsActive)
        {
            behaviorsActive = true;
            await Anchor.AttachAsync(PanelId, AnchorId, Side, Align, Offset);
            await Overlay.OpenAsync(
                PanelId, AnchorId, trapFocus: false, lockScroll: false, closeOnEscape: false, closeOnOutsideClick: true, OnDismiss);
            await FocusGridAsync();
        }
        else if (!IsOpen && behaviorsActive)
        {
            behaviorsActive = false;
            await Overlay.CloseAsync(PanelId);
            await Anchor.DetachAsync(PanelId);
        }
    }

    private void Open()
    {
        focusedDate = Value ?? Today();
        viewMonth = FirstOfMonth(focusedDate);
        IsOpen = true;
        StateHasChanged();
    }

    private void Close()
    {
        IsOpen = false;
        StateHasChanged();
    }

    private void MoveFocus(int days)
    {
        focusedDate = focusedDate.AddDays(days);
        viewMonth = FirstOfMonth(focusedDate);
        StateHasChanged();
    }

    private void MoveFocusMonths(int months)
    {
        focusedDate = focusedDate.AddMonths(months);
        viewMonth = FirstOfMonth(focusedDate);
        StateHasChanged();
    }

    private void MoveView(int months)
    {
        viewMonth = viewMonth.AddMonths(months);
        focusedDate = viewMonth;
        StateHasChanged();
    }

    private async Task FocusGridAsync()
    {
        try
        {
            await gridElement.FocusAsync();
        }
        catch (InvalidOperationException)
        {
            // Grid not yet rendered; the next render will focus it.
        }
    }

    private void OnDismiss() => _ = InvokeAsync(() =>
    {
        Close();
        return Task.CompletedTask;
    });

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    public async ValueTask DisposeAsync()
    {
        if (behaviorsActive)
        {
            await Overlay.CloseAsync(PanelId);
            await Anchor.DetachAsync(PanelId);
        }
    }
}
