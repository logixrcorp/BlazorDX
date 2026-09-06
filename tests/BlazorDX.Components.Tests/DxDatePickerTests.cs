using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + interaction for the styled date picker (no-op browser bridges).</summary>
public sealed class DxDatePickerTests : TestContext
{
    public DxDatePickerTests()
    {
        Services.AddScoped<IOverlayInterop, NullOverlayInterop>();
        Services.AddScoped<IAnchorInterop, NullAnchorInterop>();
    }

    [Fact]
    public void A_day_cell_announces_its_date_in_the_ambient_culture()
    {
        // The aria-label is the whole of what a screen reader says for a day cell, and it was
        // built with InvariantCulture — so a user on a French UI heard every date read out in
        // English. Nothing visual changes when this is wrong, which is why nothing caught it.
        using CultureScope _ = CultureScope.For("fr-FR");

        IRenderedComponent<DxDatePicker> picker = RenderComponent<DxDatePicker>(parameters => parameters
            .Add(p => p.Value, new DateOnly(2026, 6, 16)));
        picker.Find(".dx-date-trigger").Click();

        string labels = string.Join(" | ", picker.FindAll("[role='gridcell']")
            .Select(cell => cell.GetAttribute("aria-label") ?? string.Empty));

        Assert.Contains("juin", labels, StringComparison.Ordinal);
        Assert.DoesNotContain("June", labels, StringComparison.Ordinal);
    }

    [Fact]
    public void An_explicit_Culture_reaches_the_day_labels_too_not_just_the_visible_text()
    {
        // The subtler half. Formatting the label from CurrentCulture would look correct in the
        // test above and still be wrong here: an explicit Culture would render a French month
        // header over day cells announced in English — visible text and announced text
        // disagreeing, which no real user is ever in.
        using CultureScope _ = CultureScope.For("en-US");

        IRenderedComponent<DxDatePicker> picker = RenderComponent<DxDatePicker>(parameters => parameters
            .Add(p => p.Value, new DateOnly(2026, 6, 16))
            .Add(p => p.Culture, new System.Globalization.CultureInfo("fr-FR")));
        picker.Find(".dx-date-trigger").Click();

        string labels = string.Join(" | ", picker.FindAll("[role='gridcell']")
            .Select(cell => cell.GetAttribute("aria-label") ?? string.Empty));

        Assert.Equal("juin 2026", picker.Find(".dx-date-month").TextContent);
        Assert.Contains("juin", labels, StringComparison.Ordinal);
    }

    [Fact]
    public void Shows_placeholder_until_a_date_is_set()
    {
        IRenderedComponent<DxDatePicker> picker = RenderComponent<DxDatePicker>(parameters => parameters
            .Add(p => p.Placeholder, "Pick a date"));

        string trigger = picker.Find(".dx-date-trigger").OuterHtml;
        Assert.Contains("Pick a date", trigger);
        Assert.Contains("dx-date-placeholder", trigger);
    }

    [Fact]
    public void Displays_the_selected_date_in_the_bound_culture()
    {
        // The picker now formats the selected date with the short-date pattern of
        // its culture; the invariant culture's pattern is MM/dd/yyyy.
        IRenderedComponent<DxDatePicker> picker = RenderComponent<DxDatePicker>(parameters => parameters
            .Add(p => p.Value, new DateOnly(2026, 6, 16))
            .Add(p => p.Culture, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Contains("06/16/2026", picker.Find(".dx-date-trigger").OuterHtml);
    }

    [Fact]
    public void Opening_shows_a_calendar_grid_of_42_days()
    {
        IRenderedComponent<DxDatePicker> picker = RenderComponent<DxDatePicker>(parameters => parameters
            .Add(p => p.Value, new DateOnly(2026, 6, 16)));

        picker.Find(".dx-date-trigger").Click();

        Assert.Equal("June 2026", picker.Find(".dx-date-month").TextContent);
        Assert.Equal(42, picker.FindAll(".dx-date-day").Count);
        // The selected day is marked.
        Assert.Single(picker.FindAll(".dx-date-day.dx-date-selected"));
    }

    [Fact]
    public void Clicking_a_day_raises_ValueChanged()
    {
        DateOnly? bound = null;
        IRenderedComponent<DxDatePicker> picker = RenderComponent<DxDatePicker>(parameters => parameters
            .Add(p => p.Value, new DateOnly(2026, 6, 16))
            .Add(p => p.ValueChanged, value => bound = value));

        picker.Find(".dx-date-trigger").Click();
        // Day "20" is unambiguous in the June 2026 view.
        var twenty = picker.FindAll(".dx-date-day").First(d => d.TextContent == "20" && !d.ClassList.Contains("dx-date-outside"));
        twenty.Click();

        Assert.Equal(new DateOnly(2026, 6, 20), bound);
    }

    [Fact]
    public void Next_month_button_advances_the_view()
    {
        IRenderedComponent<DxDatePicker> picker = RenderComponent<DxDatePicker>(parameters => parameters
            .Add(p => p.Value, new DateOnly(2026, 6, 16)));

        picker.Find(".dx-date-trigger").Click();
        picker.Find("[aria-label='Next month']").Click();

        Assert.Equal("July 2026", picker.Find(".dx-date-month").TextContent);
    }

    [Fact]
    public void ArrowDown_moves_the_active_descendant_by_a_week()
    {
        IRenderedComponent<DxDatePicker> picker = RenderComponent<DxDatePicker>(parameters => parameters
            .Add(p => p.Value, new DateOnly(2026, 6, 16)));

        picker.Find(".dx-date-trigger").Click();
        string before = picker.Find("[role=grid]").GetAttribute("aria-activedescendant")!;
        picker.Find("[role=grid]").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        string after = picker.Find("[role=grid]").GetAttribute("aria-activedescendant")!;

        Assert.EndsWith("20260616", before);
        Assert.EndsWith("20260623", after); // +7 days
    }
}
