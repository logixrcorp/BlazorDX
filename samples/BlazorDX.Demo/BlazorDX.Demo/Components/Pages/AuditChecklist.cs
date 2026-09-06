namespace BlazorDX.Demo.Components.Pages;

/// <summary>One area of the manual screen-reader pass, and the checks that belong to it.</summary>
/// <param name="Id">Anchor id for the section.</param>
/// <param name="Title">Heading shown to the tester.</param>
/// <param name="Route">Demo route this area is tested on, or null for the baseline pass.</param>
/// <param name="Note">One line of guidance shown under the heading.</param>
/// <param name="Checks">The individual checks, in order.</param>
public sealed record AuditArea(string Id, string Title, string? Route, string Note, string[] Checks);

/// <summary>
/// The manual screen-reader checklist, rendered by <c>/audit</c>.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>docs/accessibility-screen-reader-checklist.md</c>. The two are kept in step
/// by <c>AuditChecklistDriftTests</c>, which parses both and fails if the counts diverge — the
/// same shape of guard the localization resources use, and for the same reason: a checklist that
/// has quietly drifted from the document it claims to implement is worse than no checklist,
/// because it still looks authoritative.
/// </para>
/// <para>
/// The data lives in C# rather than being read from the markdown at runtime because the document
/// is not shipped with the app, and because rendering it server-side is what lets the page work
/// with no JavaScript at all.
/// </para>
/// </remarks>
public static class AuditChecklist
{
    /// <summary>Every area, in the order a tester should work through them.</summary>
    public static readonly AuditArea[] Areas =
    [
        new("baseline", "Every page (baseline)", null, "Run these once on any route before starting the component sections.",
        [
            "First Tab reveals a skip link, and landmarks (nav / main) let you jump blocks. (2.4.1)",
            "Every interactive control announces name, role, and value or state. (4.1.2)",
            "A visible focus indicator is present and follows a logical order. (2.4.7 / 2.4.3)",
            "Async results are announced through the live region. (4.1.3)",
            "No keyboard trap; Escape dismisses overlays. (2.1.2)",
            "With reduced motion enabled in your OS, transitions are suppressed.",
        ]),
        new("files", "File manager", "/files", "The move alternative is the important one: drag-and-drop is enhancement-only.",
        [
            "The tree announces treeitem, its level, and expanded or collapsed state; arrow keys navigate it.",
            "The move alternative works with no mouse: arm \"Move\", choose a \"Move here\" target, confirm, and hear the result. (2.5.7)",
            "Upload through the standard file input works without dragging anything.",
            "After a move or upload, focus lands somewhere sensible - the moved row, or the status. (2.4.3)",
            "A name collision is announced clearly rather than silently dropped.",
        ]),
        new("scheduler", "Scheduler", "/scheduler", "Category must not be conveyed by colour alone.",
        [
            "The view switch (Week / Month / Day) is a tablist, and arrow keys move between tabs.",
            "The month grid announces as a grid with rows and cells; arrows, Home/End and PageUp/PageDown move the active cell, and aria-current reads on today.",
            "In the day or week time view, the active slot is announced with its date and hour.",
            "Each event announces title, date, time and category - with category not colour-only.",
            "Changing the view or the date is announced. (4.1.3)",
        ]),
        new("docviewer", "Document and PDF viewer", "/docviewer", "An untagged PDF is a content limitation, not a viewer bug - note which you hit.",
        [
            "The embed or iframe announces a meaningful title. (4.1.2)",
            "The toolbar (Download / Print / Open) is reachable and labelled, with targets at least 24 by 24.",
            "The accessible download link is reachable even when the toolbar is hidden.",
            "An unsafe or empty source gives a spoken \"unavailable\" placeholder rather than a broken control.",
            "Note whether the source PDF is PDF/UA-tagged.",
        ]),
        new("reporting", "Reporting and Power BI", "/reports", "Also visit /powerbi. Results were recorded for this area once; this re-checks them.",
        [
            "The iframe has a title, and the toolbar plus parameter form conform. (3.3.1 / 3.3.2)",
            "An accessible export is offered - tagged PDF, data, or \"show as table\".",
            "The accessibility statement names what BlazorDX guarantees versus the renderer or report author.",
        ]),
        new("excel", "Excel viewer and editor", "/excel", "Also visit /excel-edit. No manual pass has ever been recorded here.",
        [
            "The formula bar, cell reference and active cell value are announced as you navigate.",
            "Arrow-key cell navigation reads as a grid and gridcell pattern, not as a plain table.",
            "Recalculated cells are announced, or the update is otherwise discoverable rather than silent.",
        ]),
        new("word", "Word viewer and editor", "/word", "Also visit /word-edit. No manual pass has ever been recorded here.",
        [
            "Document structure - headings, lists, tables - reads correctly through the OOXML to HTML round trip, with no semantic loss versus the source file.",
            "The formatting toolbar announces pressed or active state for the current selection.",
        ]),
        new("htmx", "HTMX static-SSR viewer", "/htmx/doc", "This route deliberately skips Blazor enhanced navigation.",
        [
            "With JavaScript disabled entirely, the fallback is independently operable.",
            "After a full-page navigation, focus lands somewhere sensible rather than being stranded.",
        ]),
        new("editorial", "Editorial components", "/docs", "Open a real article from the docs, not just the component gallery.",
        [
            "Scrollytelling stages neither trap nor steal focus, and the reveal respects reduced motion for a keyboard or AT user - not only visually.",
            "Without the scrollytelling script, stage content is still reachable and readable in document order.",
            "Heading hierarchy in a real article is correct and skips no levels.",
            "Figure and spread images have meaningful alt text in practice, not only in the flagship article.",
            "Table-of-contents links move focus to the target section on activation, not just scroll to it.",
            "The reading-progress indicator is aria-hidden, and is never the only way information is conveyed.",
            "The drop cap's enlarged first letter is not read twice or oddly - some screen readers mishandle a styled ::first-letter.",
        ]),
    ];

    /// <summary>Total number of individual checks across every area.</summary>
    public static int TotalChecks => Areas.Sum(area => area.Checks.Length);
}
