using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>The kind of document a <see cref="DxDocumentViewer"/> knows how to render.</summary>
public enum DocumentKind
{
    /// <summary>A raster or vector image, rendered in an <c>&lt;img&gt;</c>.</summary>
    Image,

    /// <summary>A PDF, rendered with the browser's native viewer via <c>&lt;embed&gt;</c>.</summary>
    Pdf,

    /// <summary>Markdown source, rendered safely through <see cref="DxMarkdown"/>.</summary>
    Markdown,

    /// <summary>Plain text or code, rendered in a <c>&lt;pre&gt;</c>.</summary>
    Text,

    /// <summary>Anything else — only a download link is offered.</summary>
    Unknown,
}

/// <summary>
/// One document to show in a <see cref="DxDocumentViewer"/>. For
/// <see cref="DocumentKind.Image"/> and <see cref="DocumentKind.Pdf"/>,
/// <paramref name="Source"/> is a URL or data URL; for
/// <see cref="DocumentKind.Markdown"/> and <see cref="DocumentKind.Text"/> it is the
/// literal content (so the viewer needs no network access of its own).
/// </summary>
public sealed record ViewerDocument(string Name, DocumentKind Kind, string Source)
{
    /// <summary>Infers the kind from a file name's extension.</summary>
    public static DocumentKind KindFromName(string name)
    {
        int dot = name.LastIndexOf('.');
        string ext = dot >= 0 ? name[(dot + 1)..].ToLowerInvariant() : string.Empty;
        return ext switch
        {
            "png" or "jpg" or "jpeg" or "gif" or "webp" or "svg" or "bmp" or "avif" => DocumentKind.Image,
            "pdf" => DocumentKind.Pdf,
            "md" or "markdown" => DocumentKind.Markdown,
            "txt" or "log" or "csv" or "json" or "xml" or "yml" or "yaml" or "cs" or "js" or "ts" or "html" or "css" => DocumentKind.Text,
            _ => DocumentKind.Unknown,
        };
    }
}

/// <summary>
/// A multi-format document viewer. It dispatches on each document's
/// <see cref="DocumentKind"/>: images render in an <c>&lt;img&gt;</c> with zoom, PDFs
/// in the browser's native <c>&lt;embed&gt;</c> viewer (no bundled PDF engine),
/// Markdown through the safe <see cref="DxMarkdown"/> renderer, and text/code in a
/// <c>&lt;pre&gt;</c>. With more than one document a sidebar switches between them.
/// A leaf component; styling is CSS-variable driven (see dx-display.css).
/// </summary>
public sealed class DxDocumentViewer : ComponentBase
{
    /// <summary>The documents to show. The first is selected initially.</summary>
    [Parameter] public IReadOnlyList<ViewerDocument> Documents { get; set; } = [];

    /// <summary>Extra CSS classes appended to the viewer root.</summary>
    [Parameter] public string? Class { get; set; }

    private int current;
    private int zoom = 100;

    private ViewerDocument? Active =>
        current >= 0 && current < Documents.Count ? Documents[current] : null;

    private void Select(int index)
    {
        current = index;
        zoom = 100;   // reset zoom when switching documents
    }

    protected override void OnParametersSet()
    {
        if (current >= Documents.Count)
        {
            current = 0;
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-docview {Class}".TrimEnd());

        if (Documents.Count > 1)
        {
            BuildSidebar(builder);
        }

        builder.OpenElement(100, "div");
        builder.AddAttribute(101, "class", "dx-docview-main");

        if (Active is null)
        {
            builder.OpenElement(102, "div");
            builder.AddAttribute(103, "class", "dx-docview-empty");
            builder.AddContent(104, "No document");
            builder.CloseElement();
        }
        else
        {
            BuildToolbar(builder, Active);
            BuildBody(builder, Active);
        }

        builder.CloseElement();   // main
        builder.CloseElement();   // root
    }

    private void BuildSidebar(RenderTreeBuilder builder)
    {
        builder.OpenElement(10, "nav");
        builder.AddAttribute(11, "class", "dx-docview-list");
        builder.AddAttribute(12, "aria-label", "Documents");

        for (int i = 0; i < Documents.Count; i++)
        {
            int index = i;
            ViewerDocument doc = Documents[i];
            builder.OpenElement(13, "button");
            builder.SetKey(doc);
            builder.AddAttribute(14, "type", "button");
            builder.AddAttribute(15, "class", index == current ? "dx-docview-item dx-docview-active" : "dx-docview-item");
            builder.AddAttribute(16, "aria-current", index == current ? "true" : "false");
            builder.AddAttribute(17, "onclick", EventCallback.Factory.Create(this, () => Select(index)));

            builder.OpenElement(18, "span");
            builder.AddAttribute(19, "class", "dx-docview-item-name");
            builder.AddContent(20, doc.Name);
            builder.CloseElement();

            builder.OpenElement(21, "span");
            builder.AddAttribute(22, "class", "dx-docview-kind");
            builder.AddContent(23, doc.Kind.ToString());
            builder.CloseElement();

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildToolbar(RenderTreeBuilder builder, ViewerDocument doc)
    {
        builder.OpenElement(110, "div");
        builder.AddAttribute(111, "class", "dx-docview-toolbar");

        builder.OpenElement(112, "span");
        builder.AddAttribute(113, "class", "dx-docview-title");
        builder.AddContent(114, doc.Name);
        builder.CloseElement();

        builder.OpenElement(115, "span");
        builder.AddAttribute(116, "class", "dx-docview-badge");
        builder.AddContent(117, doc.Kind.ToString());
        builder.CloseElement();

        if (doc.Kind == DocumentKind.Image)
        {
            builder.OpenElement(120, "div");
            builder.AddAttribute(121, "class", "dx-docview-zoom");
            ZoomButton(builder, 122, "−", "Zoom out", () => zoom = Math.Max(25, zoom - 25));
            builder.OpenElement(128, "span");
            builder.AddAttribute(129, "class", "dx-docview-zoom-value");
            builder.AddContent(130, $"{zoom}%");
            builder.CloseElement();
            ZoomButton(builder, 131, "+", "Zoom in", () => zoom = Math.Min(400, zoom + 25));
            ZoomButton(builder, 137, "Reset", "Reset zoom", () => zoom = 100);
            builder.CloseElement();
        }

        if (doc.Kind is DocumentKind.Image or DocumentKind.Pdf)
        {
            builder.OpenElement(145, "a");
            builder.AddAttribute(146, "class", "dx-docview-download");
            builder.AddAttribute(147, "href", doc.Source);
            builder.AddAttribute(148, "download", doc.Name);
            builder.AddContent(149, "⭳ Download");
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void ZoomButton(RenderTreeBuilder builder, int seq, string label, string ariaLabel, System.Action onClick)
    {
        builder.OpenElement(seq, "button");
        builder.AddAttribute(seq + 1, "type", "button");
        builder.AddAttribute(seq + 2, "class", "dx-docview-zoom-btn");
        builder.AddAttribute(seq + 3, "aria-label", ariaLabel);
        builder.AddAttribute(seq + 4, "onclick", EventCallback.Factory.Create(this, onClick));
        builder.AddContent(seq + 5, label);
        builder.CloseElement();
    }

    private void BuildBody(RenderTreeBuilder builder, ViewerDocument doc)
    {
        builder.OpenElement(200, "div");
        builder.AddAttribute(201, "class", "dx-docview-body");

        switch (doc.Kind)
        {
            case DocumentKind.Image:
                builder.OpenElement(210, "img");
                builder.AddAttribute(211, "class", "dx-docview-img");
                builder.AddAttribute(212, "src", doc.Source);
                builder.AddAttribute(213, "alt", doc.Name);
                builder.AddAttribute(214, "style", $"width:{zoom}%");
                builder.CloseElement();
                break;

            case DocumentKind.Pdf:
                builder.OpenElement(220, "embed");
                builder.AddAttribute(221, "class", "dx-docview-pdf");
                builder.AddAttribute(222, "type", "application/pdf");
                builder.AddAttribute(223, "src", doc.Source);
                builder.AddAttribute(224, "title", doc.Name);
                builder.CloseElement();
                break;

            case DocumentKind.Markdown:
                builder.OpenComponent<DxMarkdown>(230);
                builder.AddComponentParameter(231, nameof(DxMarkdown.Value), doc.Source);
                builder.AddComponentParameter(232, nameof(DxMarkdown.Class), "dx-docview-markdown");
                builder.CloseComponent();
                break;

            case DocumentKind.Text:
                builder.OpenElement(240, "pre");
                builder.AddAttribute(241, "class", "dx-docview-text");
                builder.AddAttribute(242, "tabindex", "0");
                builder.AddContent(243, doc.Source);
                builder.CloseElement();
                break;

            default:
                builder.OpenElement(250, "div");
                builder.AddAttribute(251, "class", "dx-docview-unsupported");
                builder.AddContent(252, "Preview not available for this file type. ");
                builder.OpenElement(253, "a");
                builder.AddAttribute(254, "href", doc.Source);
                builder.AddAttribute(255, "download", doc.Name);
                builder.AddContent(256, "Download");
                builder.CloseElement();
                builder.CloseElement();
                break;
        }

        builder.CloseElement();
    }
}
