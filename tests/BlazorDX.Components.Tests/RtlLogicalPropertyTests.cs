using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Guards the RTL half of ADR 0016: a stylesheet that declares itself converted must not use
/// physical directional properties (<c>margin-left</c>, <c>text-align: right</c>, a bare
/// <c>left:</c>, ...) without a justification.
/// </summary>
/// <remarks>
/// <para>
/// This exists because RTL was otherwise verified by exactly one axe-only E2E check on
/// <c>/dialog?dir=rtl</c>, which asserts no accessibility violations and nothing about layout — a
/// stylesheet could be entirely unconverted and still pass it. A static check is the cheap half
/// that actually catches an unconverted (or re-physicalized) declaration, and it catches it at
/// the source rather than in a browser.
/// </para>
/// <para>
/// <b>Opt-in by marker, deliberately.</b> Only files containing <c>rtl-clean</c> are enforced.
/// The conversion backlog is 24 stylesheets and ~104 declarations; enforcing everything now would
/// just fail. Each conversion batch adds the marker to the files it converts, and the end state
/// is every stylesheet carrying it — the same ratchet DX1003 uses for hardcoded strings.
/// </para>
/// <para>
/// <b>Escape hatch.</b> A line carrying <c>rtl-exempt: &lt;reason&gt;</c> is allowed. Some
/// physical usages are correct: <c>DxSheet</c>'s <c>Side="left"/"right"</c> names a physical
/// screen edge (converting it would make <c>Side="right"</c> dock left under RTL, contradicting
/// the parameter), and a box pinned to both edges is symmetric. This formalizes the prose
/// carve-out the pilot already wrote into dx-overlay.css.
/// </para>
/// </remarks>
public sealed class RtlLogicalPropertyTests
{
    private const string ConvertedMarker = "rtl-clean";
    private const string ExemptMarker = "rtl-exempt";

    private static readonly (string Name, Regex Pattern)[] PhysicalProperties =
    [
        ("margin-left/right", new Regex(@"margin-(left|right)\s*:", RegexOptions.Compiled)),
        ("padding-left/right", new Regex(@"padding-(left|right)\s*:", RegexOptions.Compiled)),
        ("border-left/right", new Regex(@"border-(left|right)(-\w+)?\s*:", RegexOptions.Compiled)),
        ("border-*-left/right-radius", new Regex(@"border-(top|bottom)-(left|right)-radius\s*:", RegexOptions.Compiled)),
        ("text-align: left/right", new Regex(@"text-align\s*:\s*(left|right)\b", RegexOptions.Compiled)),
        ("float: left/right", new Regex(@"float\s*:\s*(left|right)\b", RegexOptions.Compiled)),

        // A bare `left:`/`right:` positioning property. The lookbehind keeps this from matching
        // the hyphenated properties above (border-left:, margin-right:, ...), which have their
        // own rules and their own messages.
        ("bare left:/right:", new Regex(@"(?<![-\w])(left|right)\s*:", RegexOptions.Compiled)),
    ];

    [Fact]
    public void Stylesheets_marked_rtl_clean_use_logical_properties()
    {
        string[] converted = ShippedStylesheets()
            .Where(path => File.ReadAllText(path).Contains(ConvertedMarker, StringComparison.Ordinal))
            .ToArray();

        // If this trips, the marker was dropped from dx-overlay.css (ADR 0016's pilot) and the
        // guard would silently be checking nothing.
        Assert.NotEmpty(converted);

        List<string> violations = [];
        foreach (string path in converted)
        {
            string[] lines = File.ReadAllLines(path);
            string[] code = StripComments(lines);

            for (int i = 0; i < lines.Length; i++)
            {
                // Detect on the code, exempt on the original line: a declaration mentioned inside
                // a comment (including this guard's own documentation) is not a declaration, and
                // the `rtl-exempt` justification necessarily lives in a comment.
                if (lines[i].Contains(ExemptMarker, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach ((string name, Regex pattern) in PhysicalProperties)
                {
                    if (pattern.IsMatch(code[i]))
                    {
                        violations.Add($"{Path.GetFileName(path)}:{i + 1}: {name} — {lines[i].Trim()}");
                        break;
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Physical directional properties in rtl-clean stylesheets. Convert them to logical "
            + $"properties (margin-inline-start, text-align: start, inset-inline-start, ...), or add "
            + $"`/* {ExemptMarker}: <reason> */` on the line if the usage is deliberately physical:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void The_converted_pilot_still_documents_its_deliberate_physical_usages()
    {
        // dx-overlay.css is the worked example the rollout copies: converted, marked, and with
        // every intentionally-physical line carrying a reason. If the exemptions disappear, the
        // convention has been lost even though the guard above would still pass.
        string overlay = File.ReadAllText(
            Path.Combine(ComponentsWwwroot(), "dx-overlay.css"));

        Assert.Contains(ConvertedMarker, overlay, StringComparison.Ordinal);
        Assert.Contains($"{ExemptMarker}:", overlay, StringComparison.Ordinal);
        Assert.Contains("text-align: start", overlay, StringComparison.Ordinal);
        Assert.Contains("margin-inline-start", overlay, StringComparison.Ordinal);
    }

    /// <summary>
    /// Blanks out <c>/* ... */</c> spans (which CSS allows to run across lines) while keeping the
    /// line count and column positions intact, so a match's line number still points at the real
    /// source line.
    /// </summary>
    private static string[] StripComments(string[] lines)
    {
        string[] code = new string[lines.Length];
        bool inComment = false;

        for (int i = 0; i < lines.Length; i++)
        {
            // Read from the original line, write to a separate buffer: blanking in place would
            // erase the '*' that the very next character needs to see to close the comment.
            string line = lines[i];
            char[] stripped = new char[line.Length];

            for (int c = 0; c < line.Length; c++)
            {
                if (!inComment && c + 1 < line.Length && line[c] == '/' && line[c + 1] == '*')
                {
                    inComment = true;
                }

                bool closing = inComment && c > 0 && line[c - 1] == '*' && line[c] == '/';
                stripped[c] = inComment ? ' ' : line[c];

                if (closing)
                {
                    inComment = false;
                }
            }

            code[i] = new string(stripped);
        }

        return code;
    }

    private static IEnumerable<string> ShippedStylesheets() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.css", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));

    private static string ComponentsWwwroot() =>
        Path.Combine(RepositoryRoot(), "src", "BlazorDX.Components", "wwwroot");

    // Walks up from the test binaries to the directory holding the solution. Failing loudly beats
    // skipping: a guard that silently finds no files to check is worse than no guard.
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
