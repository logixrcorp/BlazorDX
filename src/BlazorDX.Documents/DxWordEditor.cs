using BlazorDX.Components;
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
public sealed class DxWordEditor : ComponentBase
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
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-word-editor {Class}".TrimEnd());

        builder.OpenComponent<DxRichTextEditor>(2);
        builder.AddComponentParameter(3, nameof(DxRichTextEditor.Value), editorHtml);
        builder.AddComponentParameter(4, nameof(DxRichTextEditor.ValueChanged),
            EventCallback.Factory.Create<string?>(this, OnEditorHtmlChangedAsync));
        builder.AddComponentParameter(5, nameof(DxRichTextEditor.Sanitizer), Sanitizer);
        builder.AddComponentParameter(6, nameof(DxRichTextEditor.AriaLabel), Label);
        builder.AddComponentParameter(7, nameof(DxRichTextEditor.Class), "dx-word-editor-surface");
        builder.CloseComponent();

        if (ShowStatus)
        {
            builder.OpenElement(8, "p");
            builder.AddAttribute(9, "class", "dx-word-editor-status");
            builder.AddAttribute(10, "role", "status");
            builder.AddAttribute(11, "aria-live", "polite");
            builder.AddContent(12, dirty ? "Unsaved changes" : "All changes saved");
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private async Task OnEditorHtmlChangedAsync(string? html)
    {
        string next = html ?? string.Empty;

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
