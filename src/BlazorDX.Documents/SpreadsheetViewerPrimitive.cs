using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Documents;

/// <summary>
/// Tier 1 headless state for a read-only spreadsheet viewer: which sheet is active,
/// how to switch sheets, and the keyboard model for the sheet tab strip. It renders
/// no markup — <see cref="DxSpreadsheetViewer"/> is the styled Tier 2 layer on top.
/// Mirrors the self-contained view-switch pattern used by the scheduler.
/// </summary>
public class SpreadsheetViewerPrimitive : ComponentBase
{
    private int active;

    /// <summary>The workbook to display. Sheets render in workbook order as tabs.</summary>
    [Parameter] public Workbook? Workbook { get; set; }

    /// <summary>The zero-based index of the initially active sheet.</summary>
    [Parameter] public int ActiveSheetIndex { get; set; }

    /// <summary>Raised when the active sheet changes (tab click or arrow keys).</summary>
    [Parameter] public EventCallback<int> ActiveSheetIndexChanged { get; set; }

    /// <summary>The sheets in workbook order (never null; empty when no workbook).</summary>
    protected IReadOnlyList<Worksheet> Sheets => Workbook?.Sheets ?? [];

    /// <summary>The currently active sheet, or null when the workbook has no sheets.</summary>
    protected Worksheet? ActiveSheet =>
        active >= 0 && active < Sheets.Count ? Sheets[active] : null;

    /// <summary>The clamped index of the active sheet.</summary>
    protected int ActiveIndex => active;

    protected override void OnParametersSet() =>
        active = Sheets.Count == 0 ? 0 : Math.Clamp(ActiveSheetIndex, 0, Sheets.Count - 1);

    /// <summary>Whether <paramref name="index"/> is the active sheet.</summary>
    protected bool IsActive(int index) => index == active;

    /// <summary>Activates the sheet at <paramref name="index"/> (no-op if unchanged/out of range).</summary>
    protected async Task SetActiveAsync(int index)
    {
        if (index < 0 || index >= Sheets.Count || index == active)
        {
            return;
        }

        active = index;
        if (ActiveSheetIndexChanged.HasDelegate)
        {
            await ActiveSheetIndexChanged.InvokeAsync(index);
        }

        StateHasChanged();
    }

    /// <summary>Arrow/Home/End navigation across the tab strip (automatic activation).</summary>
    protected async Task OnTabStripKeyDownAsync(KeyboardEventArgs args)
    {
        if (Sheets.Count == 0)
        {
            return;
        }

        int next = args.Key switch
        {
            "ArrowRight" or "ArrowDown" => (active + 1) % Sheets.Count,
            "ArrowLeft" or "ArrowUp" => (active - 1 + Sheets.Count) % Sheets.Count,
            "Home" => 0,
            "End" => Sheets.Count - 1,
            _ => active,
        };

        if (next != active)
        {
            await SetActiveAsync(next);
        }
    }
}
