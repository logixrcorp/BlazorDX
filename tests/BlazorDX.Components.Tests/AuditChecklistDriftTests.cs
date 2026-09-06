using System.Text.RegularExpressions;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Keeps the <c>/audit</c> page in step with the checklist document it claims to implement.
/// </summary>
/// <remarks>
/// <para>
/// The page renders its checks from a C# array rather than from the markdown, because the document
/// is not shipped with the app. That leaves two copies, and two copies drift. A checklist that has
/// quietly fallen behind the document is worse than no checklist at all, because it still looks
/// authoritative to the volunteer working through it — they would report a complete pass over an
/// incomplete list.
/// </para>
/// <para>
/// Both files are read as text rather than through a project reference: the test project does not
/// reference the demo, and the localization consistency tests already establish this shape in this
/// repo. The assertion is deliberately on the <em>count</em> per section rather than on the exact
/// wording, since the page rewrites each check into a spoken instruction ("Open a section's link
/// and…") while the document is written as a terse spec. Pinning the prose would make every
/// editorial improvement a test failure and teach people to edit the test.
/// </para>
/// </remarks>
public sealed class AuditChecklistDriftTests
{
    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorDX.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. parts]));

    private static string ChecklistMarkdown() =>
        Read("docs", "accessibility-screen-reader-checklist.md");

    private static string PageData() =>
        Read("samples", "BlazorDX.Demo", "BlazorDX.Demo", "Components", "Pages", "AuditChecklist.cs");

    [Fact]
    public void The_page_carries_every_check_the_document_lists()
    {
        // "- [ ] " opens each unchecked item in the document.
        int documented = Regex.Matches(ChecklistMarkdown(), @"^- \[[ xX]\] ", RegexOptions.Multiline).Count;

        // Each check in the C# data is a quoted string inside an area's collection expression.
        // Counting the entries between the [ ... ] of each Checks array is fragile; counting the
        // lines that are nothing but a quoted string is not, because the generator writes one per
        // line and nothing else in the file has that shape.
        int rendered = Regex.Matches(PageData(), @"^\s{12}"".*"",\s*$", RegexOptions.Multiline).Count;

        Assert.Equal(documented, rendered);
    }

    [Fact]
    public void Every_area_names_a_route_that_the_demo_actually_serves()
    {
        // A dead link here sends a volunteer to a 404 and quietly loses that whole section of the
        // pass. The routes are asserted against the demo's own @page directives rather than a
        // hand-kept list, so a renamed route fails here rather than in someone's testing session.
        string[] routes = [.. Regex.Matches(PageData(), @"^\s{8}new\(""[^""]+"", ""[^""]+"", ""([^""]+)""",
                RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)];

        Assert.NotEmpty(routes);

        string demo = Path.Combine(RepositoryRoot(), "samples", "BlazorDX.Demo");
        HashSet<string> served = [.. Directory
            .EnumerateFiles(demo, "*.razor", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"@page\s+""([^""]+)""")
                .Select(m => m.Groups[1].Value))];

        foreach (string route in routes)
        {
            Assert.Contains(route, served);
        }
    }

    [Fact]
    public void The_audit_route_itself_is_served()
    {
        string page = Read("samples", "BlazorDX.Demo", "BlazorDX.Demo", "Components", "Pages", "Audit.razor");

        Assert.Contains("@page \"/audit\"", page, StringComparison.Ordinal);

        // The checklist has to be in the server-rendered markup: this is a tool for screen-reader
        // users, so content that only appears once JavaScript runs is the exact failure the
        // library's own no-JS checks exist to catch.
        Assert.Contains("AuditChecklist.Areas", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", page, StringComparison.Ordinal);
    }
}
