using System.Globalization;
using BlazorDX.Interop;
using BlazorDX.Primitives.Scheduling;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled scheduler built on <see cref="SchedulerPrimitive"/>. Renders a
/// toolbar with a keyboard-accessible Week / Month / Day view switch, then the
/// active view: a time grid (Week/Day) with absolutely positioned event blocks,
/// or a month date grid with per-day event buttons. All views share one ARIA
/// "grid" with 2-D arrow-key navigation (mirroring the DataGrid), focus-visible
/// cells, accessible event buttons, text category labels (never colour alone),
/// and a polite aria-live region announcing view/date changes. Styling is
/// token-driven (see dx-scheduler.css).
/// </summary>
/// <remarks>
/// RRULE-style recurring events are expanded in C# (see <see cref="SchedulerPrimitive"/>), and the
/// time grid supports pointer drag-to-move / drag-to-create via a thin TypeScript bridge
/// (<see cref="ISchedulerInterop"/>) with edge auto-scroll — all date math and state stay in C#.
/// The overlap layout is computed in pure C# (the once-planned Rust overlap-lane kernel is not
/// wired up and the date math does not need it).
/// </remarks>
public sealed class DxScheduler : SchedulerPrimitive, IAsyncDisposable
{
    /// <summary>Pixel height of one hour row (Week/Day time grid).</summary>
    [Parameter] public int HourHeight { get; set; } = 44;

    [Parameter] public string? Class { get; set; }

    [Inject] private ISchedulerInterop Drag { get; set; } = default!;

    private static readonly string[] WeekdayNames =
        ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    private readonly string gridId = $"dx-sched-{Guid.NewGuid():N}";

    // Whether the time-grid drag bridge is currently wired to gridId (only in time views).
    private bool dragWired;

    private string Columns => $"grid-template-columns:56px repeat({ViewDayCount},minmax(0,1fr));";

    private string MonthColumns => "grid-template-columns:repeat(7,minmax(0,1fr));";

    private string CellId(int row, int column) => $"{gridId}-r{row}c{column}";

    protected override void OnParametersSet() => ClampActiveCell();

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        // Rebuild the recurrence expansion for the window being rendered (events or date may
        // have changed since the last pass).
        InvalidateOccurrences();

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-sched {Class}".TrimEnd());

        BuildToolbar(builder);
        BuildLiveRegion(builder);

        switch (View)
        {
            case SchedulerView.Month:
                BuildMonth(builder);
                break;
            default:
                BuildTimeGrid(builder);   // Week and Day share the time grid
                break;
        }

        builder.CloseElement();
    }

    // ---- Toolbar + view switch ----

    private void BuildToolbar(RenderTreeBuilder builder)
    {
        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-sched-toolbar");

        NavButton(builder, 4, "‹", PreviousLabel, PreviousAsync);
        NavButton(builder, 10, "Today", "Go to today", TodayAsync);
        NavButton(builder, 16, "›", NextLabel, NextAsync);

        builder.OpenElement(22, "span");
        builder.AddAttribute(23, "class", "dx-sched-range");
        builder.AddContent(24, RangeLabel);
        builder.CloseElement();

        BuildViewSwitch(builder);

        builder.CloseElement();
    }

    // Segmented Week/Month/Day switch. Roving tabindex + arrow keys, WAI-ARIA
    // tablist pattern (mirrors DxTabs); selection is announced via the live region.
    private void BuildViewSwitch(RenderTreeBuilder builder)
    {
        builder.OpenElement(30, "div");
        builder.AddAttribute(31, "class", "dx-sched-views");
        builder.AddAttribute(32, "role", "tablist");
        builder.AddAttribute(33, "aria-label", "Calendar view");
        builder.AddAttribute(34, "onkeydown",
            EventCallback.Factory.Create<KeyboardEventArgs>(this, OnViewSwitchKeyDownAsync));
        builder.AddEventPreventDefaultAttribute(35, "onkeydown", true);

        ViewButton(builder, 40, SchedulerView.Week, "Week");
        ViewButton(builder, 50, SchedulerView.Month, "Month");
        ViewButton(builder, 60, SchedulerView.Day, "Day");

        builder.CloseElement();
    }

    private void ViewButton(RenderTreeBuilder builder, int seq, SchedulerView view, string label)
    {
        bool selected = View == view;
        builder.OpenElement(seq, "button");
        builder.SetKey(view);
        builder.AddAttribute(seq + 1, "type", "button");
        builder.AddAttribute(seq + 2, "class", selected ? "dx-sched-view dx-sched-view-on" : "dx-sched-view");
        builder.AddAttribute(seq + 3, "role", "tab");
        builder.AddAttribute(seq + 4, "aria-selected", selected ? "true" : "false");
        builder.AddAttribute(seq + 5, "tabindex", selected ? "0" : "-1");
        builder.AddAttribute(seq + 6, "onclick", EventCallback.Factory.Create(this, () => SetViewAsync(view)));
        builder.AddContent(seq + 7, label);
        builder.CloseElement();
    }

    private async Task OnViewSwitchKeyDownAsync(KeyboardEventArgs args)
    {
        SchedulerView[] order = [SchedulerView.Week, SchedulerView.Month, SchedulerView.Day];
        int current = Array.IndexOf(order, View);
        int next = args.Key switch
        {
            "ArrowRight" or "ArrowDown" => (current + 1) % order.Length,
            "ArrowLeft" or "ArrowUp" => (current - 1 + order.Length) % order.Length,
            "Home" => 0,
            "End" => order.Length - 1,
            _ => current,
        };

        if (next != current)
        {
            await SetViewAsync(order[next]);
        }
    }

    private void NavButton(RenderTreeBuilder builder, int seq, string text, string label, Func<Task> onClick)
    {
        builder.OpenElement(seq, "button");
        builder.AddAttribute(seq + 1, "type", "button");
        builder.AddAttribute(seq + 2, "class", "dx-sched-nav");
        builder.AddAttribute(seq + 3, "aria-label", label);
        builder.AddAttribute(seq + 4, "onclick", EventCallback.Factory.Create(this, onClick));
        builder.AddContent(seq + 5, text);
        builder.CloseElement();
    }

    // Polite live region: announces the current view and date range (WCAG 4.1.3).
    private void BuildLiveRegion(RenderTreeBuilder builder)
    {
        builder.OpenElement(70, "div");
        builder.AddAttribute(71, "class", "dx-sched-sr");
        builder.AddAttribute(72, "role", "status");
        builder.AddAttribute(73, "aria-live", "polite");
        builder.AddContent(74, $"{View} view, {RangeLabel}");
        builder.CloseElement();
    }

    // ---- Time grid (Week + Day) ----

    private void BuildTimeGrid(RenderTreeBuilder builder)
    {
        BuildTimeHead(builder);
        BuildTimeBody(builder);
    }

    private void BuildTimeHead(RenderTreeBuilder builder)
    {
        builder.OpenElement(80, "div");
        builder.AddAttribute(81, "class", "dx-sched-head");
        builder.AddAttribute(82, "style", Columns);

        builder.OpenElement(83, "div");
        builder.AddAttribute(84, "class", "dx-sched-corner");
        builder.CloseElement();

        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        foreach (DateOnly day in Days)
        {
            builder.OpenElement(85, "div");
            builder.SetKey(day);
            builder.AddAttribute(86, "class", day == today ? "dx-sched-dayhead dx-sched-today" : "dx-sched-dayhead");
            builder.AddContent(87, $"{day.DayOfWeek.ToString()[..3]} {day.Day}");
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildTimeBody(RenderTreeBuilder builder)
    {
        int bodyHeight = HourSpan * HourHeight;

        builder.OpenElement(90, "div");
        builder.AddAttribute(91, "id", gridId);
        builder.AddAttribute(92, "class", "dx-sched-body");
        builder.AddAttribute(93, "style", Columns);
        // role="application": the time grid is a fully custom 2-D keyboard widget
        // (events are absolutely positioned in day columns; it does not map cleanly
        // to grid > row > gridcell). application has no required-children rule and
        // aria-activedescendant is valid on it, so axe stays clean while the existing
        // arrow/Home/End/PageUp/PageDown keyboard model is preserved.
        builder.AddAttribute(94, "role", "application");
        builder.AddAttribute(95, "aria-label", $"{View} schedule");
        builder.AddAttribute(96, "tabindex", "0");
        if (HasActiveCell)
        {
            builder.AddAttribute(97, "aria-activedescendant", CellId(ActiveRow, ActiveColumn));
        }

        builder.AddAttribute(98, "onkeydown",
            EventCallback.Factory.Create<KeyboardEventArgs>(this, OnGridKeyDownAsync));
        builder.AddEventPreventDefaultAttribute(99, "onkeydown", true);

        // Time axis.
        builder.OpenElement(100, "div");
        builder.AddAttribute(101, "class", "dx-sched-axis");
        foreach (int hour in Hours)
        {
            builder.OpenElement(102, "div");
            builder.SetKey(hour);
            builder.AddAttribute(103, "class", "dx-sched-hour");
            builder.AddAttribute(104, "style", $"height:{HourHeight}px;");
            builder.AddContent(105, $"{hour}:00");
            builder.CloseElement();
        }

        builder.CloseElement();

        ScheduledBlock[] blocks = Blocks().ToArray();
        for (int dayIndex = 0; dayIndex < ViewDayCount; dayIndex++)
        {
            int captured = dayIndex;
            builder.OpenElement(110, "div");
            builder.SetKey(dayIndex);
            builder.AddAttribute(111, "class", "dx-sched-col");
            builder.AddAttribute(112, "style",
                $"height:{bodyHeight}px;background-size:100% {HourHeight}px;");

            // Active-cell marker: an invisible focusable target for the active time slot.
            if (HasActiveCell && ActiveColumn == captured)
            {
                int top = ActiveRow * HourHeight;
                // aria-activedescendant target for the active time slot. No role:
                // inside role="application" it is just the labelled focus marker, so
                // there is no orphaned gridcell (aria-required-parent) for axe to flag.
                builder.OpenElement(113, "div");
                builder.AddAttribute(114, "id", CellId(ActiveRow, captured));
                builder.AddAttribute(115, "class", "dx-sched-cell-active");
                builder.AddAttribute(117, "aria-label", ActiveSlotLabel(captured));
                builder.AddAttribute(118, "style", $"top:{top}px;height:{HourHeight}px;");
                builder.CloseElement();
            }

            foreach (ScheduledBlock block in blocks)
            {
                if (block.DayIndex == captured)
                {
                    BuildEvent(builder, block);
                }
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildEvent(RenderTreeBuilder builder, ScheduledBlock block)
    {
        double top = block.OffsetHours * HourHeight;
        double height = block.LengthHours * HourHeight;
        SchedulerEvent ev = block.Event;

        builder.OpenElement(120, "button");
        builder.SetKey(ev);
        builder.AddAttribute(121, "type", "button");
        builder.AddAttribute(122, "class", "dx-sched-event");

        // Drag-to-move keys off this: only concrete (non-recurring) events carry a row index,
        // so an expanded occurrence (SourceIndex -1) is selectable but not directly draggable.
        if (block.SourceIndex >= 0)
        {
            builder.AddAttribute(1221, "data-dx-key", block.SourceIndex);
        }

        builder.AddAttribute(123, "style",
            string.Create(CultureInfo.InvariantCulture,
                $"top:{top:0.#}px;height:{height:0.#}px;{EventAccent(ev.Color)}"));
        builder.AddAttribute(124, "aria-label", EventLabel(ev));
        builder.AddAttribute(125, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(ev)));

        if (ev.Category is { Length: > 0 } category)
        {
            // Category as text + icon (never colour alone) — WCAG 1.4.1.
            builder.OpenElement(126, "span");
            builder.AddAttribute(127, "class", "dx-sched-event-cat");
            builder.AddContent(128, $"● {category}");
            builder.CloseElement();
        }

        builder.OpenElement(129, "span");
        builder.AddAttribute(130, "class", "dx-sched-event-time");
        builder.AddContent(131, ev.Start.ToString("t", CultureInfo.InvariantCulture));
        builder.CloseElement();

        builder.OpenElement(132, "span");
        builder.AddAttribute(133, "class", "dx-sched-event-title");
        builder.AddContent(134, ev.Title);
        builder.CloseElement();

        builder.CloseElement();
    }

    // ---- Month grid ----

    private void BuildMonth(RenderTreeBuilder builder)
    {
        // Weekday header row.
        builder.OpenElement(140, "div");
        builder.AddAttribute(141, "class", "dx-sched-month-head");
        builder.AddAttribute(142, "style", MonthColumns);
        foreach (string name in WeekdayNames)
        {
            builder.OpenElement(143, "div");
            builder.SetKey(name);
            builder.AddAttribute(144, "class", "dx-sched-dayhead");
            builder.AddContent(145, name);
            builder.CloseElement();
        }

        builder.CloseElement();

        // Date grid.
        builder.OpenElement(150, "div");
        builder.AddAttribute(151, "id", gridId);
        builder.AddAttribute(152, "class", "dx-sched-month");
        builder.AddAttribute(153, "style", MonthColumns);
        builder.AddAttribute(154, "role", "grid");
        builder.AddAttribute(155, "aria-label", "Month schedule");
        builder.AddAttribute(156, "tabindex", "0");
        builder.AddAttribute(1561, "aria-colcount", "7");
        builder.AddAttribute(1562, "aria-rowcount", MonthWeekCount);
        if (HasActiveCell)
        {
            builder.AddAttribute(157, "aria-activedescendant", CellId(ActiveRow, ActiveColumn));
        }

        builder.AddAttribute(158, "onkeydown",
            EventCallback.Factory.Create<KeyboardEventArgs>(this, OnGridKeyDownAsync));
        builder.AddEventPreventDefaultAttribute(159, "onkeydown", true);

        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        int weeks = MonthWeekCount;
        for (int row = 0; row < weeks; row++)
        {
            // Each week is a role="row" so the grid satisfies grid > row > gridcell
            // (WCAG aria-required-children). The wrapper uses display:contents so the
            // 7 cells still participate directly in the parent CSS grid layout.
            builder.OpenElement(1585, "div");
            builder.SetKey(row);
            builder.AddAttribute(1586, "class", "dx-sched-month-row");
            builder.AddAttribute(1587, "role", "row");
            for (int col = 0; col < 7; col++)
            {
                BuildMonthCell(builder, row, col, today);
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildMonthCell(RenderTreeBuilder builder, int row, int col, DateOnly today)
    {
        DateOnly day = MonthGridStart.AddDays((row * 7) + col);
        bool active = IsActiveCell(row, col);
        bool inMonth = IsInDisplayedMonth(day);

        string css = "dx-sched-month-cell";
        if (!inMonth)
        {
            css += " dx-sched-outside";
        }

        if (day == today)
        {
            css += " dx-sched-today";
        }

        if (active)
        {
            css += " dx-sched-cell-active";
        }

        builder.OpenElement(160, "div");
        builder.SetKey(day);
        builder.AddAttribute(161, "id", CellId(row, col));
        builder.AddAttribute(162, "class", css);
        builder.AddAttribute(163, "role", "gridcell");
        builder.AddAttribute(164, "aria-label", day.ToString("D", CultureInfo.InvariantCulture));
        if (day == today)
        {
            builder.AddAttribute(165, "aria-current", "date");
        }

        int capturedRow = row;
        int capturedCol = col;
        builder.AddAttribute(166, "onclick",
            EventCallback.Factory.Create(this, () => SetActiveCell(capturedRow, capturedCol)));

        builder.OpenElement(167, "span");
        builder.AddAttribute(168, "class", "dx-sched-month-num");
        builder.AddContent(169, day.Day);
        builder.CloseElement();

        foreach (SchedulerEvent ev in EventsOn(day))
        {
            BuildMonthEvent(builder, ev);
        }

        builder.CloseElement();
    }

    private void BuildMonthEvent(RenderTreeBuilder builder, SchedulerEvent ev)
    {
        builder.OpenElement(170, "button");
        builder.SetKey(ev);
        builder.AddAttribute(171, "type", "button");
        builder.AddAttribute(172, "class", "dx-sched-month-event");
        builder.AddAttribute(173, "style", ev.Color is null ? null : EventAccent(ev.Color));
        builder.AddAttribute(174, "aria-label", EventLabel(ev));
        builder.AddAttribute(175, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(ev)));

        // Category icon + text so status isn't colour-only (WCAG 1.4.1).
        builder.OpenElement(176, "span");
        builder.AddAttribute(177, "class", "dx-sched-month-dot");
        builder.AddAttribute(178, "aria-hidden", "true");
        builder.AddContent(179, "●");
        builder.CloseElement();

        builder.OpenElement(180, "span");
        builder.AddAttribute(181, "class", "dx-sched-month-event-label");
        builder.AddContent(182, $"{ev.Start.ToString("t", CultureInfo.InvariantCulture)} {ev.Title}");
        builder.CloseElement();

        builder.CloseElement();
    }

    // ---- Keyboard nav ----

    private async Task OnGridKeyDownAsync(KeyboardEventArgs args)
    {
        if (MoveActiveCell(args.Key, args.CtrlKey))
        {
            StateHasChanged();
        }

        await Task.CompletedTask;
    }

    // ---- Drag-to-move / drag-to-create bridge ----

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Drag lives on the time grid only (Week/Day); the Month grid keeps click-to-select.
        // RegisterTimeGrid is idempotent, so re-wiring every render keeps the geometry
        // (day count, hours, row height) in sync as the view changes. Off-browser this is a
        // no-op via NullSchedulerInterop.
        if (View == SchedulerView.Month)
        {
            if (dragWired)
            {
                await Drag.UnregisterAsync(gridId);
                dragWired = false;
            }

            return;
        }

        await Drag.RegisterTimeGridAsync(gridId, ViewDayCount, StartHour, EndHour, HourHeight, OnDragResult);
        dragWired = true;
    }

    // Applies a completed pointer drag reported by the bridge. The result is untrusted pointer
    // geometry, so the primitive re-validates the index and clamps the day/hour before raising
    // OnEventMoved / OnRangeCreated; we marshal back onto the renderer and re-render afterwards.
    private void OnDragResult(SchedulerDragResult result)
    {
        _ = InvokeAsync(async () =>
        {
            if (result.Kind == SchedulerDragKind.Create)
            {
                await ApplyCreateAsync(result.DayIndex, result.StartHour, result.EndHour);
            }
            else
            {
                await ApplyMoveAsync(result.SourceIndex, result.DayIndex, result.StartHour);
            }

            StateHasChanged();
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (dragWired)
        {
            await Drag.UnregisterAsync(gridId);
        }

        await Drag.DisposeAsync();
    }

    // ---- Styling ----

    // The consumer colour drives a left ACCENT border, not the text background: there is
    // no fixed darkening that keeps white text >= 4.5:1 for an arbitrary input colour (a
    // near-white colour stays light). So events use a neutral body with dark text — which
    // clears AA for any colour — and surface the category colour as the accent stripe
    // (and the month dot) via this CSS variable (WCAG 1.4.3, verified by axe). A null
    // colour falls back to --dx-accent in the stylesheet.
    private static string EventAccent(string? color) =>
        color is null
            ? string.Empty
            : $"--dx-event-accent:{color};";

    // ---- Labels ----

    private string EventLabel(SchedulerEvent ev)
    {
        string date = DateOnly.FromDateTime(ev.Start).ToString("D", CultureInfo.InvariantCulture);
        string time = $"{ev.Start.ToString("t", CultureInfo.InvariantCulture)} to {ev.End.ToString("t", CultureInfo.InvariantCulture)}";
        string cat = ev.Category is { Length: > 0 } c ? $", {c}" : string.Empty;
        return $"{ev.Title}, {date}, {time}{cat}";
    }

    private string ActiveSlotLabel(int column)
    {
        DateOnly? date = CellDate(ActiveRow, column);
        int? hour = CellHour(ActiveRow);
        string dateText = date?.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty;
        return hour is int h ? $"{dateText}, {h}:00" : dateText;
    }

    private string PreviousLabel => View switch
    {
        SchedulerView.Month => "Previous month",
        SchedulerView.Day => "Previous day",
        _ => "Previous week",
    };

    private string NextLabel => View switch
    {
        SchedulerView.Month => "Next month",
        SchedulerView.Day => "Next day",
        _ => "Next week",
    };

    private string RangeLabel
    {
        get
        {
            if (View == SchedulerView.Month)
            {
                return MonthFirst.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            }

            if (View == SchedulerView.Day)
            {
                return WeekStart.ToString("dddd, MMM d", CultureInfo.InvariantCulture);
            }

            DateOnly last = WeekStart.AddDays(Math.Max(0, ViewDayCount - 1));

            // Include the year on both ends when the range straddles a year boundary
            // (e.g. "Dec 28, 2026 – Jan 3, 2027").
            string format = WeekStart.Year == last.Year ? "MMM d" : "MMM d, yyyy";
            return $"{WeekStart.ToString(format, CultureInfo.InvariantCulture)} – {last.ToString(format, CultureInfo.InvariantCulture)}";
        }
    }
}
