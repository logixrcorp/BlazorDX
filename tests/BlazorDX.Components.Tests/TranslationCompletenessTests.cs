using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Every invariant <c>.resx</c> is complete in every language the library ships, and each
/// translation agrees with the English on the things a translation must not change.
/// </summary>
/// <remarks>
/// <para>
/// A translation cannot be checked for meaning, but three of its failure modes are mechanical and
/// all three are invisible at a glance:
/// </para>
/// <list type="bullet">
///   <item>
///     A <b>missing key</b> silently falls back to English. The UI still renders, so nothing looks
///     broken — the French user simply gets one English string among the rest.
///   </item>
///   <item>
///     A <b>dropped placeholder</b> is worse. "Graphique en secteurs avec {0} parts" without its
///     <c>{0}</c> reads as perfectly good French and tells the reader nothing; a screen-reader user
///     hears a chart described with no numbers in it.
///   </item>
///   <item>
///     <b>Edge whitespace</b> matters because several strings are sentence fragments joined to a
///     link in markup — "Le PDF ne s'affiche pas ? " has a trailing space that holds it off the
///     anchor that follows.
///   </item>
/// </list>
/// <para>
/// What this deliberately does not check is whether a translation differs from the English. They legitimately
/// coincide for <i>Message</i>, <i>Documents</i>, <i>Actions</i>, <i>Options</i>, <i>Pagination</i>,
/// <i>Total</i>, <i>Normal</i>, <i>Style</i>, <i>Saturation</i> and <i>Document</i> in French, and
/// for <i>Name</i>, <i>Alt</i>, <i>Tab</i>, <i>Operator</i> and <i>Sepia</i> in German. A rule
/// demanding a difference would only invite someone to invent one.
/// </para>
/// </remarks>
public sealed class TranslationCompletenessTests
{
    // {0}, {1:N0}, {3:0} — the specifier is part of the identity: dropping :N0 loses the
    // thousands separators that make a large count readable.
    private static readonly Regex Placeholder = new(@"\{\d+(?::[^}]*)?\}", RegexOptions.Compiled);

    [Fact]
    public void Every_resource_is_complete_in_every_shipped_language()
    {
        List<string> problems = [];
        string[] cultures = ShippedCultures();
        int compared = 0;

        // Inferred, not hardcoded: the shipped set is whatever culture files exist, and every
        // invariant resource must then carry all of them. Translating a new string into French
        // and forgetting German fails here rather than shipping one English string to Germany.
        Assert.True(cultures.Length > 0, "No culture-qualified .resx files found at all.");

        foreach (string invariant in InvariantResources())
        {
            string name = Path.GetFileName(invariant);
            Dictionary<string, string> source = Read(invariant);

            foreach (string culture in cultures)
            {
                string translated = $"{invariant[..^".resx".Length]}.{culture}.resx";

                if (!File.Exists(translated))
                {
                    problems.Add($"{name} has no .{culture}.resx. Every string the library ships is "
                        + $"translated into {string.Join(", ", cultures)}; a new resource file needs "
                        + "one per language (docs/localization.md).");
                    continue;
                }

                Dictionary<string, string> target = Read(translated);

                foreach (string key in source.Keys.Where(k => !target.ContainsKey(k)).OrderBy(k => k))
                {
                    problems.Add($"{name}[\"{key}\"] has no {culture} value, so it renders in English "
                        + $"to that user: \"{source[key]}\"");
                }

                foreach (string key in target.Keys.Where(k => !source.ContainsKey(k)).OrderBy(k => k))
                {
                    problems.Add($"{name}[\"{key}\"] exists only in {culture} — the English string "
                        + "moved or was removed, and the translation is now dead weight.");
                }

                foreach (string key in source.Keys.Where(target.ContainsKey).OrderBy(k => k))
                {
                    compared++;
                    CompareOne($"{name} [{culture}]", key, source[key], target[key], problems);
                }
            }
        }

        // A guard that finds nothing to compare is not passing, it is broken.
        Assert.True(compared > 0, "No translated strings found. Have the .resx files moved?");

        Assert.True(problems.Count == 0,
            "Translated resources do not line up with the invariant ones:" + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(p => "  " + p)));
    }

    private static void CompareOne(string file, string key, string english, string translated, List<string> problems)
    {
        string[] sourceHoles = [.. Placeholder.Matches(english).Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal)];
        string[] targetHoles = [.. Placeholder.Matches(translated).Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal)];

        if (!sourceHoles.SequenceEqual(targetHoles, StringComparer.Ordinal))
        {
            problems.Add($"{file}[\"{key}\"] changes its placeholders, so the translated text loses "
                + $"or misplaces its values.{Environment.NewLine}      en \"{english}\""
                + $"{Environment.NewLine}      ->  \"{translated}\"");
        }

        if (StartsWithSpace(english) != StartsWithSpace(translated) || EndsWithSpace(english) != EndsWithSpace(translated))
        {
            problems.Add($"{file}[\"{key}\"] changes its leading or trailing space. These strings are "
                + $"joined to adjacent markup, so the space is load-bearing.{Environment.NewLine}"
                + $"      en \"{english}\"{Environment.NewLine}      ->  \"{translated}\"");
        }
    }

    private static bool StartsWithSpace(string value) => value.Length > 0 && char.IsWhiteSpace(value[0]);

    private static bool EndsWithSpace(string value) => value.Length > 0 && char.IsWhiteSpace(value[^1]);

    private static Dictionary<string, string> Read(string path) =>
        XDocument.Load(path).Root!
            .Elements("data")
            .Where(data => data.Attribute("name") is not null)
            .ToDictionary(
                data => data.Attribute("name")!.Value,
                data => data.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

    /// <summary>
    /// The languages this library actually ships, read off the files rather than listed here.
    /// </summary>
    /// <remarks>
    /// Keeping the list out of the test is what makes it catch the realistic mistake. A hardcoded
    /// set has to be edited when a language is added, and whoever forgets to edit it also gets no
    /// failure; inferring it means adding one <c>.de.resx</c> immediately demands the other 64.
    /// </remarks>
    private static string[] ShippedCultures() =>
        [.. Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.resx", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => name.Contains('.', StringComparison.Ordinal))
            .Select(name => name[(name.LastIndexOf('.') + 1)..])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(culture => culture, StringComparer.Ordinal)];

    private static IEnumerable<string> InvariantResources() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.resx", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            // A culture-qualified file (Name.fr.resx) is a counterpart, not an invariant.
            .Where(path => !Path.GetFileNameWithoutExtension(path).Contains('.', StringComparison.Ordinal));

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
