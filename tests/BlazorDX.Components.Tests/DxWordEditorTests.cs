using AngleSharp.Dom;
using BlazorDX.Components;
using BlazorDX.Documents;
using BlazorDX.Interop;
using BlazorDX.Security;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The round-trip Word editor: verifies the model↔editor↔.docx bridge survives a full
/// load → edit-HTML → save pass, and that the component renders the seeded content over a
/// real, labeled <see cref="DxRichTextEditor"/> toolbar (no new contentEditable, no
/// MarkupString). The live contentEditable typing path is not exercisable under bUnit
/// (the no-op interop returns ""), so the load/save conversion the component uses is tested
/// directly; the live-edit path is covered by the /word-edit axe E2E.
/// </summary>
public sealed class DxWordEditorTests : TestContext
{
    public DxWordEditorTests()
    {
        // No DOM under bUnit: the no-op bridge returns "" for GetHtml/SetHtml.
        Services.AddScoped<IRichTextInterop, NullRichTextInterop>();
    }

    private static WordDocument SampleDocument() =>
        new(
        [
            new WordHeading(1, [new WordRun("Quarterly Report")]),
            new WordHeading(2, [new WordRun("Overview")]),
            new WordParagraph(
            [
                new WordRun("This quarter was "),
                new WordRun("strong", Bold: true),
                new WordRun(" and "),
                new WordRun("steady", Italic: true),
                new WordRun("."),
            ]),
            new WordList(false, [[new WordRun("First point")], [new WordRun("Second point")]]),
            new WordList(true, [[new WordRun("Step one")], [new WordRun("Step two")]]),
            new WordTable(
            [
                new WordTableRow([new WordTableCell([new WordRun("Name")]), new WordTableCell([new WordRun("Role")])]),
                new WordTableRow([new WordTableCell([new WordRun("Ada")]), new WordTableCell([new WordRun("Analyst")])]),
            ]),
        ]);

    // ---------------------------------------------------------------------
    // Round-trip: the exact bridge the component runs on load/save.
    // ---------------------------------------------------------------------

    [Fact]
    public void Round_trip_through_editor_html_and_docx_preserves_structure()
    {
        WordDocument original = SampleDocument();

        // Load path: model -> editor HTML (what the component seeds the surface with).
        string editorHtml = WordHtml.ToHtml(original);

        // Save path: editor HTML -> model -> .docx -> model (what the component emits).
        WordDocument fromEditor = WordHtml.FromHtml(editorHtml);
        byte[] docx = DocxWriter.Write(fromEditor);
        WordDocument roundTripped = DocxReader.Read(docx);

        // Headings: levels and text survive.
        WordHeading[] headings = roundTripped.Blocks.OfType<WordHeading>().ToArray();
        Assert.Equal(2, headings.Length);
        Assert.Equal(1, headings[0].Level);
        Assert.Equal("Quarterly Report", Text(headings[0].Runs));
        Assert.Equal(2, headings[1].Level);
        Assert.Equal("Overview", Text(headings[1].Runs));

        // Paragraph: bold/italic runs survive.
        WordParagraph para = roundTripped.Blocks.OfType<WordParagraph>().First();
        Assert.Equal("This quarter was strong and steady.", Text(para.Runs));
        Assert.Contains(para.Runs, r => r is { Bold: true, Text: "strong" });
        Assert.Contains(para.Runs, r => r is { Italic: true, Text: "steady" });

        // Lists: kind and items survive.
        WordList[] lists = roundTripped.Blocks.OfType<WordList>().ToArray();
        Assert.Equal(2, lists.Length);
        Assert.False(lists[0].Ordered);
        Assert.Equal(["First point", "Second point"], lists[0].Items.Select(Text));
        Assert.True(lists[1].Ordered);
        Assert.Equal(["Step one", "Step two"], lists[1].Items.Select(Text));

        // Table: header row + body cell survive.
        WordTable table = roundTripped.Blocks.OfType<WordTable>().Single();
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Name", Text(table.Rows[0].Cells[0].Runs));
        Assert.Equal("Role", Text(table.Rows[0].Cells[1].Runs));
        Assert.Equal("Ada", Text(table.Rows[1].Cells[0].Runs));
    }

    [Fact]
    public void Round_trip_starting_from_docx_bytes_preserves_structure()
    {
        // Start from real .docx bytes, the other supported load source.
        byte[] sourceBytes = DocxWriter.Write(SampleDocument());
        WordDocument loaded = DocxReader.Read(sourceBytes);

        string editorHtml = WordHtml.ToHtml(loaded);
        WordDocument saved = WordHtml.FromHtml(editorHtml);
        WordDocument reread = DocxReader.Read(DocxWriter.Write(saved));

        Assert.Equal(
            loaded.Blocks.OfType<WordHeading>().Select(h => (h.Level, Text(h.Runs))),
            reread.Blocks.OfType<WordHeading>().Select(h => (h.Level, Text(h.Runs))));
        Assert.Equal(
            loaded.Blocks.OfType<WordTable>().Single().Rows.Count,
            reread.Blocks.OfType<WordTable>().Single().Rows.Count);
    }

    // ---------------------------------------------------------------------
    // bUnit: renders seeded content over a labeled rich-text toolbar.
    // ---------------------------------------------------------------------

    [Fact]
    public void Renders_seeded_content_over_a_rich_text_editor()
    {
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument())
            .Add(e => e.Label, "Report body"));

        // The editing surface is the shared DxRichTextEditor — contentEditable, role=textbox.
        IElement surface = editor.Find(".dx-rte-surface");
        Assert.Equal("true", surface.GetAttribute("contenteditable"));
        Assert.Equal("textbox", surface.GetAttribute("role"));
        Assert.Equal("Report body", surface.GetAttribute("aria-label"));

        // The surface is seeded with the model's HTML (Value flows into the editor).
        Assert.Equal(WordHtml.ToHtml(SampleDocument()), surface.GetAttribute("value")
            ?? editor.FindComponent<DxRichTextEditor>().Instance.Value);
    }

    [Fact]
    public void Rich_text_toolbar_is_present_and_labeled()
    {
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument()));

        IElement toolbar = editor.Find(".dx-rte-toolbar");
        Assert.Equal("toolbar", toolbar.GetAttribute("role"));
        Assert.Equal("Formatting", toolbar.GetAttribute("aria-label"));

        // The reused formatting tools (bold/italic/underline/heading/lists/clear) are all labeled.
        IRefreshableElementCollection<IElement> tools = editor.FindAll(".dx-rte-tool");
        Assert.Equal(7, tools.Count);
        Assert.All(tools, t => Assert.False(string.IsNullOrEmpty(t.GetAttribute("aria-label"))));
    }

    [Fact]
    public void Status_line_announces_saved_state_and_is_a_live_region()
    {
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument()));

        IElement status = editor.Find(".dx-word-editor-status");
        Assert.Equal("status", status.GetAttribute("role"));
        Assert.Equal("polite", status.GetAttribute("aria-live"));
        Assert.Equal("All changes saved", status.TextContent);
    }

    [Fact]
    public void Status_line_can_be_hidden()
    {
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument())
            .Add(e => e.ShowStatus, false));

        Assert.Empty(editor.FindAll(".dx-word-editor-status"));
    }

    [Fact]
    public void GetDocxBytes_serializes_the_current_document()
    {
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument()));

        byte[] bytes = editor.Instance.GetDocxBytes();

        // Real .docx bytes that read back to the same structure.
        WordDocument reread = DocxReader.Read(bytes);
        Assert.Equal("Quarterly Report", Text(reread.Blocks.OfType<WordHeading>().First().Runs));
    }

    [Fact]
    public void Editor_html_change_re_emits_document_and_docx_and_marks_dirty()
    {
        WordDocument? emitted = null;
        byte[]? savedBytes = null;

        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument())
            .Add(e => e.Sanitizer, new HtmlSanitizer(html => html)) // pass-through for the test
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => emitted = d))
            .Add(e => e.OnSave, EventCallback.Factory.Create<byte[]>(this, b => savedBytes = b)));

        DxRichTextEditor rte = editor.FindComponent<DxRichTextEditor>().Instance;

        // Simulate the editor emitting newly edited HTML (what oninput/exec does live).
        editor.InvokeAsync(() => rte.ValueChanged.InvokeAsync("<h1>Edited Title</h1><p>New body.</p>"));

        Assert.NotNull(emitted);
        Assert.Equal("Edited Title", Text(emitted!.Blocks.OfType<WordHeading>().First().Runs));
        Assert.NotNull(savedBytes);
        Assert.Equal("Edited Title",
            Text(DocxReader.Read(savedBytes!).Blocks.OfType<WordHeading>().First().Runs));
        Assert.True(editor.Instance.IsDirty);

        // MarkSaved clears the indicator.
        editor.InvokeAsync(editor.Instance.MarkSaved);
        Assert.False(editor.Instance.IsDirty);
    }

    private static string Text(IReadOnlyList<WordRun> runs) =>
        string.Concat(runs.Select(r => r.Text));
}
