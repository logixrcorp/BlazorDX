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
    // and would go unchecked, which is a gap worth knowing about; the rollout's convention is that
    // indirect text (switch arms, lookup helpers) still resolves through a literal pair like this,
    // precisely so this check keeps working. See docs/localization.md.
    // The English half allows backslash escapes: several components quote the offending input in
    // an error string (`Cannot encode as Code 128: \"{0}\"`). Stopping at the first escaped quote
    // would compare a truncated fragment against the resource and report drift that isn't there.
    private static readonly Regex CallSite = new(
        @"S\[\s*""([^""]*)""\s*,\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    [Fact]
    public void Call_site_English_matches_the_invariant_resource_value()
    {
        List<string> problems = [];
        int checkedSites = 0;

        // Usage is collected per *resource file*, not per component, because a resource file can
        // be shared: every chart localizes against DxChartResources so that ~18 components with
        // two strings each do not become ~18 .resx files. Checking orphans per component would
        // then report each chart's keys as unused by all the others.
        Dictionary<string, HashSet<string>> usedByResource = [];

        foreach (string component in LocalizedComponents())
        {
            // Comments stripped first. Documentation that shows the pattern — including this
            // test's own guidance in docs and in component doc comments — otherwise reads as a
            // real call site, and the orphan check then demands a resource entry named "Key".
            string source = StripComments(File.ReadAllText(component));
            string resourceName = ResourceNameFor(component, source);

            // The .resx sits at the root of the component's OWN project, which is no longer
            // always BlazorDX.Components: Primitives, Htmx and the two Integrations packages
            // localize too, each with its own resources.
            string project = ProjectOf(component);
            string resx = Path.Combine(project, $"{resourceName}.resx");

            if (!File.Exists(resx))
            {
                problems.Add($"{Path.GetFileName(component)} uses DxStrings<{resourceName}> but "
                    + $"{resourceName}.resx does not exist. It must sit at the project root — see "
                    + "docs/localization.md on why ResourcesPath breaks the lookup.");
                continue;
            }

            Dictionary<string, string> resources = ReadResources(resx);
            // Keyed by the resx path, not the bare name: two packages may each define a
            // resource with the same short name without sharing anything.
            HashSet<string> used = usedByResource.TryGetValue(resx, out HashSet<string>? seen)
                ? seen
                : usedByResource[resx] = [];

            foreach (Match site in CallSite.Matches(source))
            {
                string key = site.Groups[1].Value;

                // The .resx holds the decoded text, so the C# escapes have to come off before
                // comparing — otherwise every string containing a quote reports as drift.
                string english = Unescape(site.Groups[2].Value);
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
        }

        // Orphans, once every component has been read.
        foreach ((string resx, HashSet<string> used) in usedByResource)
        {
            foreach (string orphan in ReadResources(resx).Keys.Where(k => !used.Contains(k)).OrderBy(k => k))
            {
                problems.Add($"{Path.GetFileName(resx)}[\"{orphan}\"] is not used by any call site — "
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

    /// <summary>
    /// Decodes the C# escapes a call-site literal can carry, so it can be compared with the
    /// decoded text in the <c>.resx</c>. Only the escapes these strings actually use.
    /// </summary>
    private static string Unescape(string literal) =>
        literal.Replace("\\\"", "\"", StringComparison.Ordinal)
               .Replace("\\\\", "\\", StringComparison.Ordinal);

    /// <summary>
    /// Blanks out <c>//</c> and <c>/* … */</c> comments, leaving string literals intact — a
    /// <c>//</c> inside a string starts no comment, and a quote inside a comment starts no string.
    /// </summary>
    /// <remarks>
    /// Handles regular and verbatim (<c>@"…"</c>) literals. Raw string literals
    /// (<c>"""…"""</c>) are treated as regular ones, which is safe here only because no component
    /// puts a comment marker inside one; revisit if that changes.
    /// </remarks>
    private static string StripComments(string source)
    {
        char[] result = source.ToCharArray();
        bool inLine = false, inBlock = false, inString = false, inChar = false, verbatim = false;

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (inLine)
            {
                if (c is '\n') { inLine = false; } else { result[i] = ' '; }
            }
            else if (inBlock)
            {
                bool closing = c is '*' && next is '/';
                if (c is not '\n') { result[i] = ' '; }
                if (closing) { result[i + 1] = ' '; i++; inBlock = false; }
            }
            else if (inString)
            {
                // In a verbatim string "" is an escaped quote; otherwise a backslash escapes.
                if (verbatim && c is '"' && next is '"') { i++; }
                else if (!verbatim && c is '\\') { i++; }
                else if (c is '"') { inString = false; verbatim = false; }
            }
            else if (inChar)
            {
                if (c is '\\') { i++; }
                else if (c is '\'') { inChar = false; }
            }
            else if (c is '/' && next is '/') { inLine = true; result[i] = ' '; }
            else if (c is '/' && next is '*') { inBlock = true; result[i] = ' '; }
            else if (c is '"') { inString = true; verbatim = i > 0 && source[i - 1] is '@'; }
            else if (c is '\'') { inChar = true; }
        }

        return new string(result);
    }

    private static Dictionary<string, string> ReadResources(string path) =>
        XDocument.Load(path).Root!
            .Elements("data")
            .Where(data => data.Attribute("name") is not null)
            .ToDictionary(
                data => data.Attribute("name")!.Value,
                data => data.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

    /// <summary>
    /// The resource a file localizes against — its own <c>DxStrings&lt;T&gt;</c> declaration, or,
    /// for a partial split out into <c>Type.Part.cs</c>, the declaration in <c>Type.cs</c>.
    /// </summary>
    /// <remarks>
    /// Partials matter here: <c>DxWordEditor.Find.cs</c> and <c>DxSpreadsheetViewer.Editing.cs</c>
    /// use <c>S[...]</c> but declare no field, because the type declares it once in its main file.
    /// Scanning only declaring files left their call sites unchecked <i>and</i> reported the keys
    /// they use as orphaned — a guard failing in both directions at once.
    /// </remarks>
    private static string ResourceNameFor(string file, string strippedSource)
    {
        Match own = ResourceType.Match(strippedSource);
        if (own.Success)
        {
            return own.Groups[1].Value;
        }

        string name = Path.GetFileNameWithoutExtension(file);
        string declaring = Path.Combine(
            Path.GetDirectoryName(file)!, name[..name.IndexOf('.', StringComparison.Ordinal)] + ".cs");

        return File.Exists(declaring)
            ? ResourceType.Match(StripComments(File.ReadAllText(declaring))).Groups[1].Value
            : string.Empty;
    }

    /// <summary>The <c>src/&lt;project&gt;</c> directory a source file belongs to.</summary>
    private static string ProjectOf(string file)
    {
        string src = Path.Combine(RepositoryRoot(), "src");
        string relative = Path.GetRelativePath(src, file);
        return Path.Combine(src, relative.Split(Path.DirectorySeparatorChar)[0]);
    }

    // Every project under src/, all directories: components live in subfolders (Primitives puts
    // them under Inputs/ and Overlays/), and a silently-skipped one is exactly the drift this test
    // exists to catch. Each project's .resx files stay at that project's root — the placement that
    // makes the default localizer factory work at all (docs/localization.md).
    private static IEnumerable<string> LocalizedComponents() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            // The helper itself (src/Shared/DxStrings.cs, linked into every package) defines the
            // pattern rather than using it; scanning it would look for resources that do not exist.
            .Where(path => Path.GetFileName(path) != "DxStrings.cs")
            // Either declares the localizer, or uses it — the second covers partial files, which
            // declare nothing but still hold call sites (see ResourceNameFor).
            .Where(path => ResourceType.IsMatch(File.ReadAllText(path))
                || CallSite.IsMatch(StripComments(File.ReadAllText(path))));


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
