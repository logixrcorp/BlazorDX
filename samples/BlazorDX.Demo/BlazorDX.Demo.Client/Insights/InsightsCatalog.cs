namespace BlazorDX.Demo.Client.Insights;

/// <summary>Which of the three Insights areas an entry belongs to.</summary>
public enum InsightCategory
{
    Article,
    Blog,
    Whitepaper,
}

/// <summary>
/// One published piece: enough metadata to render it as a card on an index page and link to it.
/// Articles/Whitepapers are hand-built Razor pages (their own route); Blog posts are Markdown
/// files rendered through <see cref="BlazorDX.Components.DxMarkdown"/> via a dynamic
/// <c>/insights/blog/{Slug}</c> route.
/// </summary>
/// <param name="Slug">URL segment; for a Blog entry, also the Markdown filename (without extension) under <c>wwwroot/content/blog/</c>.</param>
/// <param name="Title">Display title.</param>
/// <param name="Category">Which index page lists it.</param>
/// <param name="Summary">One-to-two sentence teaser shown on the card.</param>
/// <param name="Route">Full route to the piece.</param>
/// <param name="Published">Publish date, shown on the card and the piece itself.</param>
/// <param name="Author">Byline.</param>
public sealed record InsightEntry(
    string Slug, string Title, InsightCategory Category, string Summary, string Route, DateOnly Published,
    string Author = "BlazorDX Team");

/// <summary>The single source of truth for what's published across Articles, Blog, and Whitepapers.</summary>
public static class InsightsCatalog
{
    public static readonly IReadOnlyList<InsightEntry> Entries =
    [
        new(
            "zero-trust-ephemeral-chat-conduit",
            "The Architecture of Silence",
            InsightCategory.Article,
            "Inside the Zero-Trust Ephemeral Chat Conduit: a blind-router server that never sees " +
            "plaintext, a browser-sandboxed crypto core, and a closed Shadow DOM that tears itself " +
            "down the moment it's tampered with.",
            "/insights/articles/zero-trust-ephemeral-chat-conduit",
            new DateOnly(2026, 7, 17)),
    ];

    public static IEnumerable<InsightEntry> ByCategory(InsightCategory category) =>
        Entries.Where(e => e.Category == category).OrderByDescending(e => e.Published);
}
