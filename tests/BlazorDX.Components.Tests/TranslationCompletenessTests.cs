using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Every invariant <c>.resx</c> has a French counterpart, and the two agree on the things a
/// translation must not change.
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
/// What this deliberately does not check is whether French and English differ. They legitimately
/// coincide for <i>Message</i>, <i>Documents</i>, <i>Actions</i>, <i>Options</i>, <i>Pagination</i>,
/// <i>Total</i>, <i>Normal</i>, <i>Style</i>, <i>Saturation</i> and <i>Document</i>, and a rule
/// demanding a difference would only invite someone to invent one.
/// </para>
/// </remarks>
public sealed class TranslationCompletenessTests
{
    // {0}, {1:N0}, {3:0} — the specifier is part of the identity: dropping :N0 loses the
    // thousands separators that make a large count readable.
    private static readonly Regex Placeholder = new(@"\{\d+(?::[^}]*)?\}", RegexOptions.Compiled);

    [Fact]
    public void Every_resource_has_a_complete_French_counterpart()
    {
        List<string> problems = [];
        int compared = 0;

        foreach (string invariant in InvariantResources())
        {
            string french = invariant[..^".resx".Length] + ".fr.resx";
            string name = Path.GetFileName(invariant);

            if (!File.Exists(french))
            {
                problems.Add($"{name} has no .fr.resx. Every string the library ships is "
                    + "translated; a new resource file needs one too (docs/localization.md).");
                continue;
            }

            Dictionary<string, string> source = Read(invariant);
            Dictionary<string, string> target = Read(french);

            foreach (string key in source.Keys.Where(k => !target.ContainsKey(k)).OrderBy(k => k))
            {
                problems.Add($"{name}[\"{key}\"] has no French value, so it renders in English "
                    + $"to a French user: \"{source[key]}\"");
            }

            foreach (string key in target.Keys.Where(k => !source.ContainsKey(k)).OrderBy(k => k))
            {
                problems.Add($"{name}[\"{key}\"] exists only in French — the English string moved "
                    + "or was removed, and the translation is now dead weight.");
            }

            foreach (string key in source.Keys.Where(target.ContainsKey).OrderBy(k => k))
            {
                compared++;
                CompareOne(name, key, source[key], target[key], problems);
            }
        }

        // A guard that finds nothing to compare is not passing, it is broken.
        Assert.True(compared > 0, "No translated strings found. Have the .resx files moved?");

        Assert.True(problems.Count == 0,
            "French resources do not line up with the invariant ones:" + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(p => "  " + p)));
    }

    private static void CompareOne(string file, string key, string english, string french, List<string> problems)
    {
        string[] sourceHoles = [.. Placeholder.Matches(english).Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal)];
        string[] targetHoles = [.. Placeholder.Matches(french).Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal)];

        if (!sourceHoles.SequenceEqual(targetHoles, StringComparer.Ordinal))
        {
            problems.Add($"{file}[\"{key}\"] changes its placeholders, so the translated text loses "
                + $"or misplaces its values.{Environment.NewLine}      en \"{english}\""
                + $"{Environment.NewLine}      fr \"{french}\"");
        }

        if (StartsWithSpace(english) != StartsWithSpace(french) || EndsWithSpace(english) != EndsWithSpace(french))
        {
            problems.Add($"{file}[\"{key}\"] changes its leading or trailing space. These strings are "
                + $"joined to adjacent markup, so the space is load-bearing.{Environment.NewLine}"
                + $"      en \"{english}\"{Environment.NewLine}      fr \"{french}\"");
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
