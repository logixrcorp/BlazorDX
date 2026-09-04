using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Guards the one real cost of ADR 0021's optional-localization design: English lives in two
/// places — the fallback at each <c>DxStrings</c> call site, and the invariant <c>.resx</c> —
/// and nothing in the compiler or the runtime makes them agree.
/// </summary>
/// <remarks>
/// <para>
/// The failure this catches is specifically invisible. If a call site says "Clear filter" and the
/// resource says "Clear filters", both render fine; a consumer who called <c>AddLocalization()</c>
/// simply sees different English than one who did not, and every existing test passes either way
/// because each asserts against only one of the two. The same is true of a key that exists at a
/// call site but not in the resource file: <c>DxStrings</c> falls back to English, so it looks
/// correct in English and silently loses every translation.
/// </para>
/// <para>
/// This is why ADR 0021 could accept the duplication. Unchecked it is a slow drift; checked it is
/// bookkeeping the build does for you.
/// </para>
/// <para>
/// No opt-in marker here, unlike DX1003 and <c>RtlLogicalPropertyTests</c> — those ratchet because
/// they would otherwise report the whole unconverted backlog at once. This one keys off
/// <c>DxStrings</c> usage, so it already covers exactly the localized components and picks up each
/// new one in the rollout for free.
/// </para>
/// </remarks>
public sealed class LocalizedStringConsistencyTests
{
    // The DxStrings<T> a component holds names its resource file: DxStrings<DxAlert> -> DxAlert.resx.
    // Anchored on `private`, which matters: both pilots reference `DxStrings<T>` in a doc comment
    // above the real field, so matching the bare type name resolves the resource type as "T".
    private static readonly Regex ResourceType = new(@"private\s+DxStrings<(\w+)>", RegexOptions.Compiled);

    // S["Key", "English"...] — literal key and literal English only. A computed key would not match
    // and would go unchecked, which is a gap worth knowing about; there are none today.
    private static readonly Regex CallSite = new(@"S\[\s*""([^""]*)""\s*,\s*""([^""]*)""", RegexOptions.Compiled);

    [Fact]
    public void Call_site_English_matches_the_invariant_resource_value()
    {
        List<string> problems = [];
        int checkedSites = 0;

        foreach (string component in LocalizedComponents())
        {
            string source = File.ReadAllText(component);
            string resourceName = ResourceType.Match(source).Groups[1].Value;
            string resx = Path.Combine(ComponentsDirectory(), $"{resourceName}.resx");

            if (!File.Exists(resx))
            {
                problems.Add($"{Path.GetFileName(component)} uses DxStrings<{resourceName}> but "
                    + $"{resourceName}.resx does not exist. It must sit at the project root — see "
                    + "docs/localization.md on why ResourcesPath breaks the lookup.");
                continue;
            }

            Dictionary<string, string> resources = ReadResources(resx);
            HashSet<string> used = [];

            foreach (Match site in CallSite.Matches(source))
            {
                string key = site.Groups[1].Value;
                string english = site.Groups[2].Value;
                used.Add(key);
                checkedSites++;

                if (!resources.TryGetValue(key, out string? value))
                {
                    problems.Add($"{resourceName}.resx has no entry for \"{key}\" "
                        + $"(used in {Path.GetFileName(component)}). The call site renders English "
                        + "and silently loses every translation.");
                }
                else if (value != english)
                {
                    problems.Add($"{resourceName}.resx[\"{key}\"] is \"{value}\" but the call site "
                        + $"in {Path.GetFileName(component)} falls back to \"{english}\".");
                }
            }

            foreach (string orphan in resources.Keys.Where(k => !used.Contains(k)).OrderBy(k => k))
            {
                problems.Add($"{resourceName}.resx[\"{orphan}\"] is not used by any call site — "
                    + "the string moved or was removed, and translators are still maintaining it.");
            }
        }

        // If this trips, the regexes stopped matching the codebase rather than the codebase
        // becoming consistent — a guard that checks nothing must fail loudly.
        Assert.True(checkedSites > 0, "No DxStrings call sites found. Has the pattern changed?");

        Assert.True(problems.Count == 0,
            "Call-site English and .resx values have drifted:" + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(p => "  " + p)));
    }

    private static Dictionary<string, string> ReadResources(string path) =>
        XDocument.Load(path).Root!
            .Elements("data")
            .Where(data => data.Attribute("name") is not null)
            .ToDictionary(
                data => data.Attribute("name")!.Value,
                data => data.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

    // AllDirectories, not TopDirectoryOnly: components may live in subfolders as the rollout
    // proceeds, and a silently-skipped component is exactly the drift this test exists to catch.
    // The .resx files themselves stay at the project root regardless — that placement is the
    // configuration that works (docs/localization.md).
    private static IEnumerable<string> LocalizedComponents() =>
        Directory.EnumerateFiles(ComponentsDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => Path.GetFileName(path) != "DxStrings.cs")
            .Where(path => ResourceType.IsMatch(File.ReadAllText(path)));

    private static string ComponentsDirectory() =>
        Path.Combine(RepositoryRoot(), "src", "BlazorDX.Components");

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BlazorDX.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, $"No BlazorDX.slnx above {AppContext.BaseDirectory}.");
        return directory!.FullName;
    }
}
