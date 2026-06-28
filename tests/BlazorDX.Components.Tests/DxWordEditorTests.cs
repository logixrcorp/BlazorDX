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

        // The reused formatting tools (B/I/U/S, super/sub, heading, lists, align ×4, link, clear) are labeled.
        IRefreshableElementCollection<IElement> tools = editor.FindAll(".dx-rte-tool");
        Assert.Equal(17, tools.Count);
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

    [Fact]
    public void Table_toolbar_inserts_and_deletes_rows_and_columns_at_the_caret_cell()
    {
        // Fake bridge: the caret sits in table 0, row 1, col 0.
        FakeRichTextInterop fake = new() { TableCell = "0,1,0" };
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, SampleDocument()) // table: 2 rows × 2 cols
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        // Each click edits the same source document (one-way binding here), so each assert
        // reflects that single op applied to the 2×2 sample table.
        static WordTable T(WordDocument? d) => d!.Blocks.OfType<WordTable>().Single();

        editor.Find("[aria-label='Insert table row']").Click();
        Assert.Equal(3, T(changed).Rows.Count); // 2 rows -> 3

        editor.Find("[aria-label='Delete table row']").Click();
        Assert.Single(T(changed).Rows); // 2 rows -> 1

        editor.Find("[aria-label='Insert table column']").Click();
        Assert.All(T(changed).Rows, r => Assert.Equal(3, r.Cells.Count)); // 2 cols -> 3

        editor.Find("[aria-label='Delete table column']").Click();
        Assert.All(T(changed).Rows, r => Assert.Single(r.Cells)); // 2 cols -> 1
    }

    [Fact]
    public void ModelDriven_bold_toggles_the_format_on_the_selected_range_through_the_model()
    {
        // Owned selection: run-container 0 (the paragraph), characters [0,5) = "Hello".
        FakeRichTextInterop fake = new() { SelectionRange = "0,0,5" };
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        WordDocument doc = new([new WordParagraph([new WordRun("Hello world")])]);
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.EditingCore, EditingCore.ModelDriven)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        editor.Find("[aria-label='Bold']").Click();

        WordParagraph para = changed!.Blocks.OfType<WordParagraph>().Single();
        Assert.Equal("Hello world", Text(para.Runs)); // text unchanged
        Assert.Equal("Hello", para.Runs[0].Text);     // split exactly at the selection end
        Assert.True(para.Runs[0].Bold);               // selection is bolded
        Assert.False(para.Runs[^1].Bold);             // the remainder is untouched
        Assert.Equal((0, 0, 5), fake.RestoredSelection); // caret restored after the re-seed

        // Toggling a range that is already fully bold clears it and coalesces back to one run.
        WordDocument? cleared = null;
        WordDocument boldDoc = new([new WordParagraph([new WordRun("Hello", Bold: true), new WordRun(" world")])]);
        IRenderedComponent<DxWordEditor> editor2 = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, boldDoc)
            .Add(e => e.EditingCore, EditingCore.ModelDriven)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => cleared = d)));

        editor2.Find("[aria-label='Bold']").Click();

        WordParagraph reverted = cleared!.Blocks.OfType<WordParagraph>().Single();
        Assert.Equal("Hello world", Text(reverted.Runs));
        Assert.Single(reverted.Runs);                  // split fragments merged back together
        Assert.False(reverted.Runs[0].Bold);
    }

    [Fact]
    public void ModelDriven_undo_redo_round_trips_the_model_and_restores_the_caret_on_redo()
    {
        FakeRichTextInterop fake = new() { SelectionRange = "0,0,5" }; // selection over "Hello"
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        WordDocument doc = new([new WordParagraph([new WordRun("Hello world")])]);
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.EditingCore, EditingCore.ModelDriven)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        static WordParagraph P(WordDocument? d) => d!.Blocks.OfType<WordParagraph>().Single();

        editor.Find("[aria-label='Bold']").Click();
        Assert.True(P(changed).Runs[0].Bold); // model edit applied
        Assert.True(editor.Instance.CanUndo);

        // Undo restores the prior model state in place (no re-mount) — the run is plain again.
        editor.Find("[aria-label='Undo']").Click();
        Assert.All(P(changed).Runs, r => Assert.False(r.Bold));
        Assert.True(editor.Instance.CanRedo);

        // Redo re-applies the model state AND restores the selection captured with the edit.
        fake.ClearRestored();
        editor.Find("[aria-label='Redo']").Click();
        Assert.True(P(changed).Runs[0].Bold);
        Assert.Equal((0, 0, 5), fake.RestoredSelection);
    }

    [Fact]
    public void ModelDriven_align_center_sets_paragraph_alignment_at_the_caret_block()
    {
        // Caret in run-container 1 (the paragraph); alignment ignores the empty range.
        FakeRichTextInterop fake = new() { SelectionRange = "1,3,3" };
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        WordDocument doc = new(
        [
            new WordHeading(1, [new WordRun("Title")]),    // container 0
            new WordParagraph([new WordRun("Body text")]), // container 1
        ]);
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.EditingCore, EditingCore.ModelDriven)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        editor.Find("[aria-label='Align center']").Click();

        WordParagraph para = changed!.Blocks.OfType<WordParagraph>().Single();
        Assert.Equal(WordAlignment.Center, para.Alignment);
        Assert.Equal(WordAlignment.Start, changed!.Blocks.OfType<WordHeading>().Single().Alignment); // heading untouched
    }

    [Fact]
    public void ModelDriven_clear_formatting_strips_emphasis_and_color_over_the_selection()
    {
        FakeRichTextInterop fake = new() { SelectionRange = "0,0,5" }; // "Hello"
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        WordDocument doc = new(
        [
            new WordParagraph([new WordRun("Hello", Bold: true, Italic: true, Color: "#ff0000"), new WordRun(" world")]),
        ]);
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.EditingCore, EditingCore.ModelDriven)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        editor.Find("[aria-label='Clear formatting']").Click();

        WordParagraph para = changed!.Blocks.OfType<WordParagraph>().Single();
        Assert.Equal("Hello world", Text(para.Runs));
        Assert.Single(para.Runs); // stripped fragment coalesced with " world"
        WordRun run = para.Runs[0];
        Assert.False(run.Bold);
        Assert.False(run.Italic);
        Assert.Null(run.Color);
    }

    [Fact]
    public void ModelDriven_heading_button_toggles_paragraph_to_heading_and_back()
    {
        FakeRichTextInterop fake = new() { SelectionRange = "0,2,2" }; // caret in container 0
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        WordDocument doc = new([new WordParagraph([new WordRun("Section")], WordAlignment.Center)]);
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.EditingCore, EditingCore.ModelDriven)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        // Paragraph -> heading (level 2), preserving runs and alignment.
        editor.Find("[aria-label='Heading']").Click();
        WordHeading h = changed!.Blocks.OfType<WordHeading>().Single();
        Assert.Equal(2, h.Level);
        Assert.Equal("Section", Text(h.Runs));
        Assert.Equal(WordAlignment.Center, h.Alignment);

        // A fresh heading document toggles back to a paragraph.
        WordDocument? back = null;
        WordDocument headingDoc = new([new WordHeading(2, [new WordRun("Section")], WordAlignment.Center)]);
        IRenderedComponent<DxWordEditor> editor2 = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, headingDoc)
            .Add(e => e.EditingCore, EditingCore.ModelDriven)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => back = d)));

        editor2.Find("[aria-label='Heading']").Click();
        WordParagraph para = back!.Blocks.OfType<WordParagraph>().Single();
        Assert.Empty(back!.Blocks.OfType<WordHeading>());
        Assert.Equal("Section", Text(para.Runs));
        Assert.Equal(WordAlignment.Center, para.Alignment);
    }

    [Fact]
    public void ModelDriven_text_color_sets_the_run_color_over_the_selection()
    {
        FakeRichTextInterop fake = new() { SelectionRange = "0,0,5" }; // "Hello"
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        WordDocument doc = new([new WordParagraph([new WordRun("Hello world")])]);
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.EditingCore, EditingCore.ModelDriven)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        editor.Find("[aria-label='Text color']").Change("#ff0000");

        WordParagraph para = changed!.Blocks.OfType<WordParagraph>().Single();
        Assert.Equal("Hello world", Text(para.Runs));
        Assert.Equal("#ff0000", para.Runs[0].Color); // "Hello" colored
        Assert.Equal("Hello", para.Runs[0].Text);
        Assert.Null(para.Runs[^1].Color);            // " world" untouched
    }

    [Fact]
    public void ModelDriven_bullet_button_merges_a_paragraph_into_the_adjacent_list()
    {
        // Caret in container 1 — the paragraph that follows a one-item bullet list.
        FakeRichTextInterop fake = new() { SelectionRange = "1,0,0" };
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        WordDocument doc = new(
        [
            new WordList(false, [[new WordRun("First")]]), // container 0 (one item)
            new WordParagraph([new WordRun("Second")]),    // container 1
        ]);
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.EditingCore, EditingCore.ModelDriven)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        editor.Find("[aria-label='Bullet list']").Click();

        WordList list = Assert.IsType<WordList>(Assert.Single(changed!.Blocks)); // merged into one list
        Assert.False(list.Ordered);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal("First", Text(list.Items[0]));
        Assert.Equal("Second", Text(list.Items[1]));
    }

    [Fact]
    public void ModelDriven_bullet_button_unlists_a_middle_item_and_splits_the_list()
    {
        // Caret in container 1 — the middle item of a 3-item bullet list.
        FakeRichTextInterop fake = new() { SelectionRange = "1,0,0" };
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        WordDocument doc = new(
        [
            new WordList(false, [[new WordRun("A")], [new WordRun("B")], [new WordRun("C")]]),
        ]);
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.EditingCore, EditingCore.ModelDriven)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        // Toggling the same (unordered) kind on a list item un-lists it -> paragraph.
        editor.Find("[aria-label='Bullet list']").Click();

        Assert.Collection(changed!.Blocks,
            b => Assert.Equal("A", Text(Assert.IsType<WordList>(b).Items.Single())),
            b => Assert.Equal("B", Text(Assert.IsType<WordParagraph>(b).Runs)), // un-listed in the middle
            b => Assert.Equal("C", Text(Assert.IsType<WordList>(b).Items.Single())));
    }

    [Fact]
    public void ModelDriven_link_button_sets_the_href_on_the_selected_run()
    {
        FakeRichTextInterop fake = new() { SelectionRange = "0,0,5", LinkUrl = "https://example.com" };
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        WordDocument doc = new([new WordParagraph([new WordRun("Hello world")])]);
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.EditingCore, EditingCore.ModelDriven)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        editor.Find("[aria-label='Insert link']").Click();

        WordParagraph para = changed!.Blocks.OfType<WordParagraph>().Single();
        Assert.Equal("Hello world", Text(para.Runs));
        Assert.Equal("https://example.com", para.Runs[0].Href); // "Hello" linked
        Assert.Equal("Hello", para.Runs[0].Text);
        Assert.Null(para.Runs[^1].Href);                        // " world" untouched
    }

    [Fact]
    public void Keyboard_shortcut_bold_edits_the_model_and_undo_shortcut_reverts()
    {
        FakeRichTextInterop fake = new() { SelectionRange = "0,0,5" }; // selection over "Hello"
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        WordDocument doc = new([new WordParagraph([new WordRun("Hello world")])]);
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.EditingCore, EditingCore.ModelDriven)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        Assert.NotNull(fake.Shortcut); // the editor subscribed its shortcut handler on first render

        // Ctrl+B → model-driven bold over the selection (not the browser's execCommand).
        editor.InvokeAsync(() => fake.Shortcut!("bold"));
        editor.WaitForAssertion(() =>
        {
            WordParagraph para = changed!.Blocks.OfType<WordParagraph>().Single();
            Assert.True(para.Runs[0].Bold);
            Assert.Equal("Hello", para.Runs[0].Text);
        });
        Assert.True(editor.Instance.CanUndo);

        // Ctrl+Z → our model history undo (not the browser's contentEditable undo).
        editor.InvokeAsync(() => fake.Shortcut!("undo"));
        editor.WaitForAssertion(() =>
            Assert.All(changed!.Blocks.OfType<WordParagraph>().Single().Runs, r => Assert.False(r.Bold)));
    }

    [Fact]
    public void Insert_image_adds_a_WordImage_block_after_the_caret_block()
    {
        // A valid 1×1 PNG.
        const string png =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
        FakeRichTextInterop fake = new() { SelectionRange = "0,0,0", ImageData = $"image/png|{png}" };
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        WordDocument doc = new(
        [
            new WordHeading(1, [new WordRun("Title")]),  // container 0 (caret here)
            new WordParagraph([new WordRun("Body")]),    // container 1
        ]);
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        editor.Find("[aria-label='Insert an image']").Click();

        editor.WaitForAssertion(() =>
        {
            Assert.NotNull(changed);
            Assert.Equal(3, changed!.Blocks.Count);
            WordImage img = Assert.IsType<WordImage>(changed!.Blocks[1]); // inserted after the heading
            Assert.Equal("image/png", img.ContentType);
            Assert.NotEmpty(img.Data);
        });
    }

    [Fact]
    public void ModelDriven_superscript_and_font_controls_edit_the_selected_run()
    {
        FakeRichTextInterop fake = new() { SelectionRange = "0,0,5" }; // "Hello"
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        WordDocument doc = new([new WordParagraph([new WordRun("Hello world")])]);
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, doc) // model-driven is the default core now
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        static WordRun First(WordDocument? d) => d!.Blocks.OfType<WordParagraph>().Single().Runs[0];

        editor.Find("[aria-label='Superscript']").Click();
        Assert.Equal(WordVerticalAlign.Superscript, First(changed).VerticalAlign);
        Assert.Equal("Hello", First(changed).Text);

        editor.Find("[aria-label='Font family']").Change("Arial");
        Assert.Equal("Arial", First(changed).FontFamily);

        editor.Find("[aria-label='Font size']").Change("18");
        Assert.Equal(18d, First(changed).FontSizePoints);
    }

    [Fact]
    public void Typography_round_trips_through_html()
    {
        WordDocument doc = new(
        [
            new WordParagraph(
            [
                new WordRun("E=mc", FontFamily: "Arial", FontSizePoints: 14),
                new WordRun("2", VerticalAlign: WordVerticalAlign.Superscript),
            ]),
        ]);

        WordDocument round = WordHtml.FromHtml(WordHtml.ToHtml(doc));
        WordParagraph para = round.Blocks.OfType<WordParagraph>().Single();

        Assert.Equal("Arial", para.Runs[0].FontFamily);
        Assert.Equal(14d, para.Runs[0].FontSizePoints);
        Assert.Equal(WordVerticalAlign.Superscript, para.Runs[^1].VerticalAlign);
        Assert.Equal("E=mc2", string.Concat(para.Runs.Select(r => r.Text)));
    }

    [Fact]
    public void ModelDriven_style_dropdown_sets_block_to_heading_and_back_to_normal()
    {
        FakeRichTextInterop fake = new() { SelectionRange = "0,0,0" }; // caret in container 0
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, new WordDocument([new WordParagraph([new WordRun("Title text")], WordAlignment.Center)]))
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        editor.Find("[aria-label='Paragraph style']").Change("2"); // Heading 2
        WordHeading h = changed!.Blocks.OfType<WordHeading>().Single();
        Assert.Equal(2, h.Level);
        Assert.Equal("Title text", Text(h.Runs));
        Assert.Equal(WordAlignment.Center, h.Alignment); // alignment preserved across the style change

        // Heading -> Normal on a fresh heading document.
        WordDocument? back = null;
        IRenderedComponent<DxWordEditor> editor2 = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, new WordDocument([new WordHeading(2, [new WordRun("Title text")])]))
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => back = d)));

        editor2.Find("[aria-label='Paragraph style']").Change("0"); // Normal
        Assert.Empty(back!.Blocks.OfType<WordHeading>());
        Assert.Equal("Title text", Text(back!.Blocks.OfType<WordParagraph>().Single().Runs));
    }

    [Fact]
    public void ModelDriven_line_spacing_and_indent_edit_the_block()
    {
        FakeRichTextInterop fake = new() { SelectionRange = "0,0,0" }; // caret in container 0
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, new WordDocument([new WordParagraph([new WordRun("Body")])]))
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        editor.Find("[aria-label='Line spacing']").Change("1.5");
        Assert.Equal(1.5, changed!.Blocks.OfType<WordParagraph>().Single().LineSpacing);

        editor.Find("[aria-label='Increase indent']").Click();
        Assert.Equal(1, changed!.Blocks.OfType<WordParagraph>().Single().IndentLevel);
    }

    [Fact]
    public void Paragraph_spacing_and_indent_round_trip_through_html_and_docx()
    {
        WordDocument doc = new(
            [new WordParagraph([new WordRun("Body")], WordAlignment.Start, LineSpacing: 1.5, IndentLevel: 2)]);

        WordParagraph viaHtml = WordHtml.FromHtml(WordHtml.ToHtml(doc)).Blocks.OfType<WordParagraph>().Single();
        Assert.Equal(1.5, viaHtml.LineSpacing);
        Assert.Equal(2, viaHtml.IndentLevel);

        WordParagraph viaDocx = DocxReader.Read(DocxWriter.Write(doc)).Blocks.OfType<WordParagraph>().Single();
        Assert.Equal(1.5, viaDocx.LineSpacing);
        Assert.Equal(2, viaDocx.IndentLevel);
    }

    private static string Text(IReadOnlyList<WordRun> runs) =>
        string.Concat(runs.Select(r => r.Text));
}
