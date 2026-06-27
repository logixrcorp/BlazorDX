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
        // The editor's "Download .docx" action resolves the download bridge; null off-browser.
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
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

        // The reused formatting tools (B/I/U/S, heading, lists, align ×4, link, clear) are labeled.
        IRefreshableElementCollection<IElement> tools = editor.FindAll(".dx-rte-tool");
        Assert.Equal(13, tools.Count);
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
        Assert.StartsWith("All changes saved", status.TextContent); // followed by the doc stats
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

    [Fact]
    public void Toolbar_shows_a_download_button_by_default()
    {
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument()));

        Assert.Single(editor.FindAll(".dx-word-toolbar")); // the document toolbar (rte has its own)
        Assert.Single(editor.FindAll("[aria-label='Download the document as a .docx file']"));
    }

    [Fact]
    public void Download_button_serializes_the_document_without_error()
    {
        // NullGridDomInterop makes the file save a no-op; this asserts the click path
        // (DocxWriter.Write -> bridge) doesn't throw.
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument()));

        editor.Find("[aria-label='Download the document as a .docx file']").Click();
    }

    [Fact]
    public void Status_line_reports_word_character_and_paragraph_counts()
    {
        // SampleDocument: 2 headings + 1 paragraph = 3 "paragraphs"; the paragraph reads
        // "This quarter was strong and steady." = 6 words. Characters/words also include the
        // headings, lists, and table cells.
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument()));

        string status = editor.Find(".dx-word-editor-status").TextContent;

        Assert.Contains("words", status);
        Assert.Contains("characters", status);
        Assert.Contains("3 paragraphs", status); // 2 headings + 1 paragraph
    }

    [Fact]
    public void Underline_and_strikethrough_survive_the_full_round_trip()
    {
        WordDocument original = new(
        [
            new WordParagraph(
            [
                new WordRun("plain "),
                new WordRun("under", Underline: true),
                new WordRun(" "),
                new WordRun("struck", Strike: true),
                new WordRun(" "),
                new WordRun("both", Underline: true, Strike: true),
            ]),
        ]);

        // model -> editor HTML -> model, then -> .docx -> model. Both legs must preserve
        // underline and strikethrough (previously underline was silently dropped on save).
        WordDocument viaHtml = WordHtml.FromHtml(WordHtml.ToHtml(original));
        WordDocument viaDocx = DocxReader.Read(DocxWriter.Write(viaHtml));

        foreach (WordDocument doc in new[] { viaHtml, viaDocx })
        {
            WordParagraph p = doc.Blocks.OfType<WordParagraph>().Single();
            Assert.Equal("plain under struck both", Text(p.Runs));
            Assert.Contains(p.Runs, r => r is { Text: "under", Underline: true, Strike: false });
            Assert.Contains(p.Runs, r => r is { Text: "struck", Underline: false, Strike: true });
            Assert.Contains(p.Runs, r => r is { Text: "both", Underline: true, Strike: true });
        }
    }

    [Fact]
    public void Hyperlinks_survive_the_full_round_trip_and_unsafe_urls_are_stripped()
    {
        WordDocument original = new(
        [
            new WordParagraph(
            [
                new WordRun("Visit "),
                new WordRun("our site", Href: "https://example.com/docs"),
                new WordRun(" today."),
            ]),
        ]);

        // model -> HTML -> model, then -> .docx -> model. The link (via a w:hyperlink
        // relationship in the .docx) must survive both legs.
        WordDocument viaHtml = WordHtml.FromHtml(WordHtml.ToHtml(original));
        WordDocument viaDocx = DocxReader.Read(DocxWriter.Write(viaHtml));

        foreach (WordDocument doc in new[] { viaHtml, viaDocx })
        {
            WordParagraph p = doc.Blocks.OfType<WordParagraph>().Single();
            Assert.Equal("Visit our site today.", Text(p.Runs));
            Assert.Contains(p.Runs, r => r is { Text: "our site", Href: "https://example.com/docs" });
        }
    }

    [Fact]
    public void Unsafe_hyperlink_urls_are_rejected_on_parse()
    {
        // A javascript: URL is dropped: the link text stays, but it never becomes a
        // clickable hostile URL.
        WordDocument doc = WordHtml.FromHtml("<p><a href=\"javascript:alert(1)\">click</a></p>");

        WordParagraph p = doc.Blocks.OfType<WordParagraph>().Single();
        Assert.Equal("click", Text(p.Runs));
        Assert.All(p.Runs, r => Assert.Null(r.Href));
    }

    [Fact]
    public void Find_bar_toggles_and_counts_matches()
    {
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument()));

        Assert.Empty(editor.FindAll(".dx-word-findbar"));
        editor.Find("[aria-label='Find and replace']").Click();
        Assert.Single(editor.FindAll(".dx-word-findbar"));

        editor.Find(".dx-word-find-input").Input("Overview"); // the "Overview" heading
        Assert.Contains("1 match", editor.Find(".dx-word-find-count").TextContent);
    }

    [Fact]
    public void Replace_all_rewrites_the_document_and_raises_change()
    {
        WordDocument? changed = null;
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument())
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        editor.Find("[aria-label='Find and replace']").Click();
        editor.Find(".dx-word-find-input").Input("quarter");
        editor.Find(".dx-word-replace-input").Input("period");
        editor.FindAll(".dx-word-find-btn").Single(b => b.TextContent.Contains("Replace all")).Click();

        Assert.NotNull(changed);
        string text = string.Concat(
            changed!.Blocks.OfType<WordParagraph>().SelectMany(b => b.Runs).Select(r => r.Text));
        Assert.Contains("period", text);
        Assert.DoesNotContain("quarter", text);
        Assert.True(editor.Instance.IsDirty);
    }

    [Fact]
    public void Find_bar_has_previous_next_match_navigation()
    {
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument()));
        editor.Find("[aria-label='Find and replace']").Click();

        Assert.Single(editor.FindAll("[aria-label='Previous match']"));
        Assert.Single(editor.FindAll("[aria-label='Next match']"));

        // Clicking next drives the editor's FindNextAsync through the (null) bridge,
        // which returns 0 off-browser — the path must not throw.
        editor.Find(".dx-word-find-input").Input("steady");
        editor.Find("[aria-label='Next match']").Click();
    }

    [Fact]
    public void Match_case_makes_the_search_case_sensitive()
    {
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument()));
        editor.Find("[aria-label='Find and replace']").Click();

        editor.Find(".dx-word-find-input").Input("overview");           // lower-case
        Assert.Contains("1 match", editor.Find(".dx-word-find-count").TextContent);

        editor.Find(".dx-word-find-case input").Change(true);           // now case-sensitive
        Assert.Contains("0 matches", editor.Find(".dx-word-find-count").TextContent);
    }

    [Fact]
    public void Paragraph_alignment_survives_the_full_round_trip()
    {
        WordDocument original = new(
        [
            new WordParagraph([new WordRun("centered")], WordAlignment.Center),
            new WordParagraph([new WordRun("right")], WordAlignment.End),
            new WordHeading(2, [new WordRun("just head")], WordAlignment.Justify),
            new WordParagraph([new WordRun("default")]),
        ]);

        WordDocument viaHtml = WordHtml.FromHtml(WordHtml.ToHtml(original));
        WordDocument viaDocx = DocxReader.Read(DocxWriter.Write(viaHtml));

        foreach (WordDocument doc in new[] { viaHtml, viaDocx })
        {
            WordParagraph[] paras = doc.Blocks.OfType<WordParagraph>().ToArray();
            Assert.Equal(WordAlignment.Center, paras.Single(p => Text(p.Runs) == "centered").Alignment);
            Assert.Equal(WordAlignment.End, paras.Single(p => Text(p.Runs) == "right").Alignment);
            Assert.Equal(WordAlignment.Start, paras.Single(p => Text(p.Runs) == "default").Alignment);
            Assert.Equal(WordAlignment.Justify, doc.Blocks.OfType<WordHeading>().Single().Alignment);
        }
    }

    [Fact]
    public void Text_color_and_highlight_survive_the_full_round_trip()
    {
        WordDocument original = new(
        [
            new WordParagraph(
            [
                new WordRun("plain "),
                new WordRun("red", Color: "#ff0000"),
                new WordRun(" "),
                new WordRun("hl", Highlight: "#ffff00"),
                new WordRun(" "),
                new WordRun("both", Color: "#0000ff", Highlight: "#00ff00"),
            ]),
        ]);

        WordDocument viaHtml = WordHtml.FromHtml(WordHtml.ToHtml(original));
        WordDocument viaDocx = DocxReader.Read(DocxWriter.Write(viaHtml));

        foreach (WordDocument doc in new[] { viaHtml, viaDocx })
        {
            WordParagraph p = doc.Blocks.OfType<WordParagraph>().Single();
            Assert.Equal("plain red hl both", Text(p.Runs));
            Assert.Contains(p.Runs, r => r is { Text: "red", Color: "#ff0000", Highlight: null });
            Assert.Contains(p.Runs, r => r is { Text: "hl", Color: null, Highlight: "#ffff00" });
            Assert.Contains(p.Runs, r => r is { Text: "both", Color: "#0000ff", Highlight: "#00ff00" });
        }
    }

    [Fact]
    public void Nested_lists_round_trip_their_indent_levels()
    {
        // A (top) > B (nested) > C (back to top).
        WordDocument original = new(
        [
            new WordList(false,
            [
                [new WordRun("A")],
                [new WordRun("B")],
                [new WordRun("C")],
            ],
            Levels: [0, 1, 0]),
        ]);

        WordDocument viaHtml = WordHtml.FromHtml(WordHtml.ToHtml(original));
        WordDocument viaDocx = DocxReader.Read(DocxWriter.Write(viaHtml));

        foreach (WordDocument doc in new[] { viaHtml, viaDocx })
        {
            WordList list = doc.Blocks.OfType<WordList>().Single();
            Assert.Equal(["A", "B", "C"], list.Items.Select(Text));
            Assert.Equal(0, list.LevelOf(0));
            Assert.Equal(1, list.LevelOf(1));
            Assert.Equal(0, list.LevelOf(2));
        }
    }

    [Fact]
    public void Embedded_images_round_trip_their_bytes_alt_and_size()
    {
        byte[] pixels = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5, 6, 7, 8]; // stand-in PNG bytes
        WordDocument original = new(
        [
            new WordParagraph([new WordRun("Before")]),
            new WordImage(pixels, "image/png", "A red dot", 64, 48),
            new WordParagraph([new WordRun("After")]),
        ]);

        // model -> HTML (data URL) -> model, then -> .docx (media part + <w:drawing>) -> model.
        WordDocument viaHtml = WordHtml.FromHtml(WordHtml.ToHtml(original));
        WordDocument viaDocx = DocxReader.Read(DocxWriter.Write(viaHtml));

        foreach (WordDocument doc in new[] { viaHtml, viaDocx })
        {
            WordImage img = doc.Blocks.OfType<WordImage>().Single();
            Assert.Equal(pixels, img.Data);
            Assert.Equal("image/png", img.ContentType);
            Assert.Equal("A red dot", img.AltText);
            Assert.Equal(64, img.Width);
            Assert.Equal(48, img.Height);
        }
    }

    [Fact]
    public void Undo_and_redo_restore_prior_editor_states()
    {
        WordDocument? changed = null;
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument())
            .Add(e => e.Sanitizer, new HtmlSanitizer(h => h))
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        DxRichTextEditor rte = editor.FindComponent<DxRichTextEditor>().Instance;

        // An edit makes the prior state undoable.
        editor.InvokeAsync(() => rte.ValueChanged.InvokeAsync("<h1>Edited Title</h1>"));
        Assert.Equal("Edited Title", Text(changed!.Blocks.OfType<WordHeading>().First().Runs));
        Assert.True(editor.Instance.CanUndo);

        // Undo returns to the original document; redo re-applies the edit.
        editor.Find("[aria-label='Undo']").Click();
        Assert.Equal("Quarterly Report", Text(changed!.Blocks.OfType<WordHeading>().First().Runs));
        Assert.True(editor.Instance.CanRedo);

        editor.Find("[aria-label='Redo']").Click();
        Assert.Equal("Edited Title", Text(changed!.Blocks.OfType<WordHeading>().First().Runs));
    }

    private static string Text(IReadOnlyList<WordRun> runs) =>
        string.Concat(runs.Select(r => r.Text));
}
