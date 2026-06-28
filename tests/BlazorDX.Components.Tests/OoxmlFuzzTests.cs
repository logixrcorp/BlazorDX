using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using BlazorDX.Documents;
using BlazorDX.Primitives.Grid;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Property/fuzz harness for the untrusted-document parsers. The invariant under test is a safety
/// property, not a correctness one: for <em>any</em> input — random bytes, a bit-flipped valid file,
/// an adversarial worksheet, or garbage markup — the reader must finish promptly and either return a
/// model or throw a <em>documented</em> rejection (<see cref="InvalidDataException"/> /
/// <see cref="XmlException"/> / <see cref="FormatException"/>). A hang, an OOM, or any other
/// exception type is a parser bug this harness is meant to surface. Seeds are fixed, so any failure
/// reproduces from the reported seed.
/// </summary>
public sealed class OoxmlFuzzTests
{
    // Generous per-parse ceiling: a real parse of these tiny inputs is sub-millisecond, so blowing
    // this means a hang or super-linear blowup (e.g. the column-pad amplification we fixed).
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    private const string Sml = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Pkg = "http://schemas.openxmlformats.org/package/2006/relationships";

    // Runs the parse on a worker with a timeout (to catch hangs without wedging the suite) and
    // asserts the outcome is success or a documented rejection. Returns the exception (if any) so a
    // caller can apply a stricter contract (e.g. "must not throw at all").
    private static async Task<Exception?> RunAsync(Action parse, int seed)
    {
        Exception? thrown = null;
        Task task = Task.Run(() =>
        {
            try
            {
                parse();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        });

        bool completed;
        try
        {
            await task.WaitAsync(Budget);
            completed = true;
        }
        catch (TimeoutException)
        {
            completed = false;
        }

        Assert.True(completed, $"seed {seed}: parse did not finish within {Budget.TotalSeconds:0}s (possible hang/blowup)");
        return thrown;
    }

    private static async Task AssertGracefulAsync(Action parse, int seed)
    {
        Exception? thrown = await RunAsync(parse, seed);
        if (thrown is not null)
        {
            Assert.True(
                thrown is InvalidDataException or XmlException or FormatException,
                $"seed {seed}: unexpected {thrown.GetType().Name}: {thrown.Message}");
        }
    }

    // ---- 1. Random non-zip bytes ----

    [Fact]
    public async Task Readers_reject_random_bytes_gracefully()
    {
        for (int seed = 0; seed < 120; seed++)
        {
            Random rng = new(seed);
            byte[] data = new byte[rng.Next(0, 4096)];
            rng.NextBytes(data);
            await AssertGracefulAsync(() => XlsxReader.Read(data), seed);
            await AssertGracefulAsync(() => DocxReader.Read(data), seed);
        }
    }

    // ---- 2. Bit-flip mutation of valid files ----

    [Fact]
    public async Task Readers_survive_bit_flipped_valid_files()
    {
        byte[] xlsx = XlsxWriter.Write(["A", "B"], [["1", "2"], ["3", "4"]]);
        byte[] docx = DocxWriter.Write(new WordDocument(
        [
            new WordParagraph([new WordRun("hello "), new WordRun("world", Bold: true)]),
        ]));

        for (int seed = 0; seed < 200; seed++)
        {
            await AssertGracefulAsync(() => XlsxReader.Read(Mutate(xlsx, seed)), seed);
            await AssertGracefulAsync(() => DocxReader.Read(Mutate(docx, seed + 100_000)), seed + 100_000);
        }
    }

    private static byte[] Mutate(byte[] source, int seed)
    {
        Random rng = new(seed);
        byte[] copy = (byte[])source.Clone();
        if (copy.Length == 0)
        {
            return copy;
        }

        int flips = rng.Next(1, 16);
        for (int i = 0; i < flips; i++)
        {
            copy[rng.Next(copy.Length)] ^= (byte)(1 << rng.Next(8));
        }

        return copy;
    }

    // ---- 3. Adversarial worksheet XML in an otherwise-valid package ----
    // Directly stresses the column-reference / row-padding path (the H1 amplification fix).

    [Fact]
    public async Task Adversarial_worksheets_parse_or_reject_gracefully()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            Random rng = new(seed);
            byte[] bytes = BuildXlsx(RandomSheetData(rng));
            await AssertGracefulAsync(() => XlsxReader.Read(bytes), seed);
        }
    }

    private static string RandomSheetData(Random rng)
    {
        StringBuilder sb = new();
        int rows = rng.Next(0, 8);
        for (int r = 0; r < rows; r++)
        {
            sb.Append("<row");
            if (rng.Next(2) == 0)
            {
                sb.Append(" r=\"").Append(rng.Next(1, 2_000_000)).Append('"');
            }

            sb.Append('>');
            int cells = rng.Next(0, 6);
            for (int c = 0; c < cells; c++)
            {
                sb.Append("<c");
                if (rng.Next(3) != 0)
                {
                    sb.Append(" r=\"").Append(RandomRef(rng)).Append('"');
                }

                sb.Append(rng.Next(5) switch
                {
                    0 => " t=\"s\"",
                    1 => " t=\"str\"",
                    2 => " t=\"b\"",
                    3 => " t=\"inlineStr\"",
                    _ => string.Empty,
                });
                sb.Append('>');
                sb.Append(rng.Next(2) == 0
                    ? $"<v>{rng.Next(0, 100)}</v>"
                    : "<is><t>x</t></is>");
                sb.Append("</c>");
            }

            sb.Append("</row>");
        }

        return sb.ToString();
    }

    // Up to 12 leading letters: a column index that, unclamped, would be astronomically large and
    // drive a runaway dense-row pre-pad. The clamp must turn it into a fast no-op.
    private static string RandomRef(Random rng)
    {
        int letters = rng.Next(1, 13);
        StringBuilder sb = new();
        for (int i = 0; i < letters; i++)
        {
            sb.Append((char)('A' + rng.Next(26)));
        }

        sb.Append(rng.Next(1, 2_000_000));
        return sb.ToString();
    }

    private static byte[] BuildXlsx(string sheetData)
    {
        using MemoryStream ms = new();
        using (ZipArchive zip = new(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "xl/workbook.xml",
                $"<workbook xmlns=\"{Sml}\" xmlns:r=\"{Rel}\"><sheets>" +
                "<sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels",
                $"<Relationships xmlns=\"{Pkg}\"><Relationship Id=\"rId1\" " +
                $"Type=\"{Rel}/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add(zip, "xl/worksheets/sheet1.xml",
                $"<worksheet xmlns=\"{Sml}\"><sheetData>{sheetData}</sheetData></worksheet>");
        }

        return ms.ToArray();
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    // ---- 4. WordHtml.FromHtml is total: it must NEVER throw on any markup ----

    [Fact]
    public async Task FromHtml_never_throws_on_random_markup()
    {
        string[] palette =
        [
            "<p>", "</p>", "<b>", "</b>", "<strong>", "</strong>", "<i>", "</i>",
            "<ul>", "<li>", "</li>", "</ul>", "<ol>", "<table>", "<tr>", "<td>", "</td>", "</tr>", "</table>",
            "<h1>", "</h1>", "<a href=\"http://x\">", "<a href=\"javascript:alert(1)\">", "</a>",
            "<img src=\"data:image/png;base64,QQ==\">", "<img src=\"data:text/html;base64,##\">",
            "<", ">", "<>", "<<", ">>", "</", "&amp;", "&#65;", "&#x42;", "&nbsp;", "&notreal;",
            "\"", "'", " ", "text", "résumé", "\0",
        ];

        for (int seed = 0; seed < 300; seed++)
        {
            Random rng = new(seed);
            StringBuilder sb = new();
            int n = rng.Next(0, 50);
            for (int i = 0; i < n; i++)
            {
                sb.Append(palette[rng.Next(palette.Length)]);
            }

            string html = sb.ToString();

            // Stronger than the reader contract: FromHtml is total — it must not throw at all.
            Exception? thrown = await RunAsync(() => _ = WordHtml.FromHtml(html), seed);
            Assert.True(thrown is null, $"seed {seed}: FromHtml threw {thrown?.GetType().Name}: {thrown?.Message}");
        }
    }
}
