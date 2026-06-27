using BlazorDX.Components;
using BlazorDX.Interop;
using BlazorDX.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Documents;

/// <summary>
/// A round-trip <c>.docx</c> editor: the editable sibling of <see cref="DxWordViewer"/>.
/// It loads a <see cref="WordDocument"/> (or raw <c>.docx</c> bytes), bridges the model
/// to clean semantic HTML with <see cref="WordHtml.ToHtml(WordDocument)"/>, seeds the
/// shared <see cref="DxRichTextEditor"/> as the editing surface, and converts the edited
/// HTML back to a model with <see cref="WordHtml.FromHtml(string)"/> so it can be written
/// out via <see cref="DocxWriter.Write(WordDocument)"/>.
/// </summary>
/// <remarks>
/// <para>
/// The editing surface is <em>entirely</em> the existing <see cref="DxRichTextEditor"/> —
/// its labeled toolbar, formatting commands, keyboard handling, <c>[JSImport]</c> interop,
/// and the injected <see cref="HtmlSanitizer"/> are all reused, not re-implemented. This
/// component is the thin model↔editor bridge layered on top: there is no new
/// <c>contentEditable</c>, no new interop, and no <c>MarkupString</c>. The two safety nets
/// for editor HTML are the sanitizer the host injects into <see cref="DxRichTextEditor"/>
/// and <see cref="WordHtml.FromHtml(string)"/>'s allow-listed tag subset, which together
/// decide what survives into the saved model.
/// </para>
/// <para>
/// On every edit the component re-derives the <see cref="WordDocument"/> from the editor's
/// HTML and raises <see cref="DocumentChanged"/> (so <c>@bind-Document</c> works) plus
/// <see cref="OnSave"/> with freshly serialized <c>.docx</c> bytes. A <see cref="IsDirty"/>
/// flag and an optional visible dirty indicator track unsaved edits; <see cref="MarkSaved"/>
/// clears it once the host has persisted the bytes.
/// </para>
/// <para>
/// As with the rest of the document stack this is zero-reflection, AOT-/trim-safe, and uses
/// constant <see cref="RenderTreeBuilder"/> sequence numbers throughout.
/// </para>
/// </remarks>
public sealed partial class DxWordEditor : ComponentBase
{
    private static readonly WordDocument Empty = new([]);

    // The HTML currently loaded into / read back from the editing surface. Held so a
    // host-driven Document change can re-seed the editor without clobbering live edits.
    private string editorHtml = string.Empty;

    // The last document we projected from editorHtml. Compared by HTML, not value, so a
    // round-trip that coalesces runs does not spuriously re-raise change callbacks.
    private string lastSeededHtml = string.Empty;

    private bool dirty;

    /// <summary>
    /// The document being edited. Bind with <c>@bind-Document</c>: the editor seeds from
    /// this on load, and re-emits it (via <see cref="DocumentChanged"/>) on every edit.
    /// </summary>
    [Parameter] public WordDocument? Document { get; set; }

    /// <summary>Raised with the re-parsed <see cref="WordDocument"/> after each edit.</summary>
    [Parameter] public EventCallback<WordDocument> DocumentChanged { get; set; }

    /// <summary>
    /// Optional raw <c>.docx</c> bytes to load instead of <see cref="Document"/>. When set,
    /// the bytes are read with <see cref="DocxReader"/> on first render and take precedence
    /// over <see cref="Document"/> for seeding the surface.
    /// </summary>
    [Parameter] public byte[]? DocxBytes { get; set; }

    /// <summary>
    /// Raised after each edit with the current document serialized to <c>.docx</c> bytes
    /// (via <see cref="DocxWriter.Write(WordDocument)"/>) — ready to download or persist.
    /// </summary>
    [Parameter] public EventCallback<byte[]> OnSave { get; set; }

    /// <summary>
    /// The sanitizer applied to edited HTML by the underlying <see cref="DxRichTextEditor"/>.
    /// Defaults to the editor's inert (encode-all) policy; inject a vetted policy for real
    /// rich editing. Required for any markup to survive editing.
    /// </summary>
    [Parameter] public HtmlSanitizer? Sanitizer { get; set; }

    /// <summary>Accessible name for the editing surface. Defaults to "Document".</summary>
    [Parameter] public string Label { get; set; } = "Document";

    /// <summary>
    /// When <see langword="true"/> (the default), a small status line shows whether there
    /// are unsaved edits. Driven by <see cref="IsDirty"/> and announced via <c>aria-live</c>.
    /// </summary>
    [Parameter] public bool ShowStatus { get; set; } = true;

    /// <summary>Optional extra CSS class on the editor root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Shows a document toolbar above the editor with a "Download .docx" action. On by
    /// default. (The formatting toolbar is the underlying rich-text editor's own.)
    /// </summary>
    [Parameter] public bool ShowToolbar { get; set; } = true;

    /// <summary>File name used by the toolbar's "Download .docx" action.</summary>
    [Parameter] public string DownloadFileName { get; set; } = "document.docx";

    private const string DocxMime =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>The client-side download bridge (browser real / SSR + tests null).</summary>
    [Inject] private IGridDomInterop Interop { get; set; } = default!;

    /// <summary>Whether there are edits not yet acknowledged via <see cref="MarkSaved"/>.</summary>
    public bool IsDirty => dirty;

    private WordDocument Current => Document ?? Empty;

    /// <summary>
    /// Returns the current edited document serialized to <c>.docx</c> bytes — the same bytes
    /// passed to <see cref="OnSave"/>. Lets a host fetch the file on demand (e.g. a Save
    /// button) without subscribing to the callback.
    /// </summary>
    public byte[] GetDocxBytes() => DocxWriter.Write(Current);

    /// <summary>
    /// Clears the dirty flag. Call this once the host has persisted/downloaded the bytes so
    /// the status indicator reflects a saved state.
    /// </summary>
    public void MarkSaved()
    {
        if (!dirty)
        {
            return;
        }

        dirty = false;
        StateHasChanged();
    }

    protected override void OnParametersSet()
    {
        // Seed (or re-seed) the editor HTML from the model. DocxBytes wins when present.
        // We only overwrite when the host supplies a genuinely different document than the
        // one our last edit produced, so binding does not fight live editing.
        WordDocument source = DocxBytes is { Length: > 0 } ? DocxReader.Read(DocxBytes) : Current;
        string html = WordHtml.ToHtml(source);

        if (html != lastSeededHtml)
        {
            editorHtml = html;
            lastSeededHtml = html;
            if (!_restoring)
            {
                // A genuinely new external document resets the history baseline.
                ResetHistory(html);
            }
        }

        _baselineHtml ??= html; // initialize on first load (incl. an empty document)
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-word-editor {Class}".TrimEnd());

        if (ShowToolbar)
        {
            builder.OpenElement(2, "div");
            builder.AddAttribute(3, "class", "dx-word-toolbar");
            builder.AddAttribute(4, "role", "toolbar");
            builder.AddAttribute(5, "aria-label", "Document");

            builder.OpenElement(6, "button");
            builder.AddAttribute(7, "type", "button");
            builder.AddAttribute(8, "class", "dx-word-toolbar-btn");
            builder.AddAttribute(9, "aria-label", "Download the document as a .docx file");
            builder.AddAttribute(10, "title", "Download the document as a .docx file");
            builder.AddAttribute(11, "onclick", EventCallback.Factory.Create(this, DownloadAsync));
            builder.AddContent(12, "Download .docx");
            builder.CloseElement();

            builder.OpenElement(13, "button");
            builder.AddAttribute(14, "type", "button");
            builder.AddAttribute(15, "class", "dx-word-toolbar-btn");
            builder.AddAttribute(16, "aria-label", "Find and replace");
            builder.AddAttribute(17, "title", "Find and replace");
            builder.AddAttribute(18, "aria-expanded", showFind ? "true" : "false");
            builder.AddAttribute(19, "onclick", EventCallback.Factory.Create(this, ToggleFind));
            builder.AddContent(20, "Find & replace");
            builder.CloseElement();

            ToolbarButton(builder, 30, "Undo", "Undo", UndoAsync, !CanUndo);
            ToolbarButton(builder, 40, "Redo", "Redo", RedoAsync, !CanRedo);

            // Table editing — act on the table the caret is in (a no-op elsewhere).
            ToolbarButton(builder, 50, "+ Row", "Insert table row", () => TableEditAsync(TableOp.InsertRow), false);
            ToolbarButton(builder, 60, "− Row", "Delete table row", () => TableEditAsync(TableOp.DeleteRow), false);
            ToolbarButton(builder, 70, "+ Col", "Insert table column", () => TableEditAsync(TableOp.InsertColumn), false);
            ToolbarButton(builder, 80, "− Col", "Delete table column", () => TableEditAsync(TableOp.DeleteColumn), false);

            builder.CloseElement();
        }

        if (showFind)
        {
            builder.OpenRegion(18);
            BuildFindBar(builder);
            builder.CloseRegion();
        }

        builder.OpenComponent<DxRichTextEditor>(20);
        // Re-keyed on a find/replace so the editor re-mounts and re-seeds from the updated
        // model (it deliberately ignores Value changes after mount to protect the caret).
        builder.SetKey(editorEpoch);
        builder.AddComponentParameter(21, nameof(DxRichTextEditor.Value), editorHtml);
        builder.AddComponentParameter(22, nameof(DxRichTextEditor.ValueChanged),
            EventCallback.Factory.Create<string?>(this, OnEditorHtmlChangedAsync));
        builder.AddComponentParameter(23, nameof(DxRichTextEditor.Sanitizer), Sanitizer);
        builder.AddComponentParameter(24, nameof(DxRichTextEditor.AriaLabel), Label);
        builder.AddComponentParameter(25, nameof(DxRichTextEditor.Class), "dx-word-editor-surface");
        builder.AddComponentReferenceCapture(26, rte => _rte = (DxRichTextEditor)rte);
        builder.CloseComponent();

        if (ShowStatus)
        {
            DocumentStats stats = ComputeStats(Current);

            builder.OpenElement(30, "p");
            builder.AddAttribute(31, "class", "dx-word-editor-status");
            builder.AddAttribute(32, "role", "status");
            builder.AddAttribute(33, "aria-live", "polite");
            builder.AddContent(34, $"{(dirty ? "Unsaved changes" : "All changes saved")} · " +
                $"{stats.Words:N0} words · {stats.Characters:N0} characters · {stats.Paragraphs:N0} paragraphs");
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private async Task DownloadAsync() =>
        await Interop.DownloadBytesAsync(DownloadFileName, DocxMime, DocxWriter.Write(Current));

    private void ToolbarButton(
        RenderTreeBuilder builder, int seq, string text, string label, Func<Task> handler, bool disabled)
    {
        builder.OpenElement(seq, "button");
        builder.AddAttribute(seq + 1, "type", "button");
        builder.AddAttribute(seq + 2, "class", "dx-word-toolbar-btn");
        builder.AddAttribute(seq + 3, "aria-label", label);
        builder.AddAttribute(seq + 4, "title", label);
        builder.AddAttribute(seq + 5, "disabled", disabled);
        builder.AddAttribute(seq + 6, "onclick", EventCallback.Factory.Create(this, handler));
        builder.AddContent(seq + 7, text);
        builder.CloseElement();
    }

    private readonly record struct DocumentStats(int Words, int Characters, int Paragraphs);

    // Walks the model once: words/characters span every run (headings, paragraphs, list
    // items, table cells); "paragraphs" counts the prose blocks (headings + paragraphs).
    private static DocumentStats ComputeStats(WordDocument document)
    {
        int words = 0;
        int characters = 0;
        int paragraphs = 0;

        void CountRuns(IReadOnlyList<WordRun> runs)
        {
            foreach (WordRun run in runs)
            {
                characters += run.Text.Length;
                words += CountWords(run.Text);
            }
        }

        foreach (WordBlock block in document.Blocks)
        {
            switch (block)
            {
                case WordHeading heading:
                    paragraphs++;
                    CountRuns(heading.Runs);
                    break;
                case WordParagraph paragraph:
                    paragraphs++;
                    CountRuns(paragraph.Runs);
                    break;
                case WordList list:
                    foreach (IReadOnlyList<WordRun> item in list.Items)
                    {
                        CountRuns(item);
                    }

                    break;
                case WordTable table:
                    foreach (WordTableRow row in table.Rows)
                    {
                        foreach (WordTableCell cell in row.Cells)
                        {
                            CountRuns(cell.Runs);
                        }
                    }

                    break;
            }
        }

        return new DocumentStats(words, characters, paragraphs);
    }

    private static int CountWords(string text)
    {
        int count = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }

        return count;
    }

    private async Task OnEditorHtmlChangedAsync(string? html)
    {
        string next = html ?? string.Empty;
        CaptureHistory(next); // record the prior state so this edit is undoable

        // The editor already sanitized this HTML. Re-parse it into the model; this is the
        // load→save bridge and the path the round-trip test exercises directly.
        WordDocument parsed = WordHtml.FromHtml(next);

        editorHtml = next;
        // Record what the model now serializes to, so a re-render does not re-seed the
        // surface (which would reset the caret) from the document we just produced.
        lastSeededHtml = WordHtml.ToHtml(parsed);
        dirty = true;

        if (DocumentChanged.HasDelegate)
        {
            await DocumentChanged.InvokeAsync(parsed);
        }

        if (OnSave.HasDelegate)
        {
            await OnSave.InvokeAsync(DocxWriter.Write(parsed));
        }
    }
}
