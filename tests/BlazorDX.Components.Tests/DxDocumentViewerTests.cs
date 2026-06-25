using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>DxDocumentViewer format dispatch, the document switcher, zoom, kind inference, and the accessible PDF toolbar.</summary>
public sealed class DxDocumentViewerTests : TestContext
{
    private sealed class RecordingViewerInterop : IDocumentViewerInterop
    {
        public List<string> PrintedFrames { get; } = new();
        public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;
        public ValueTask PrintAsync(string frameId)
        {
            PrintedFrames.Add(frameId);
            return ValueTask.CompletedTask;
        }
    }

    private readonly RecordingViewerInterop interop = new();

    public DxDocumentViewerTests()
    {
        Services.AddScoped<IDocumentViewerInterop>(_ => interop);
    }

    private IRenderedComponent<DxDocumentViewer> Render(params ViewerDocument[] docs) =>
        RenderComponent<DxDocumentViewer>(p => p.Add(c => c.Documents, docs));

    [Theory]
    [InlineData("photo.png", DocumentKind.Image)]
    [InlineData("scan.PDF", DocumentKind.Pdf)]
    [InlineData("notes.md", DocumentKind.Markdown)]
    [InlineData("data.json", DocumentKind.Text)]
    [InlineData("archive.zip", DocumentKind.Unknown)]
    public void Infers_kind_from_the_file_name(string name, DocumentKind expected)
    {
        Assert.Equal(expected, ViewerDocument.KindFromName(name));
    }

    [Fact]
    public void Renders_an_image_in_an_img_with_its_source()
    {
        IRenderedComponent<DxDocumentViewer> v = Render(new ViewerDocument("p.png", DocumentKind.Image, "data:image/png;base64,AAA"));

        Assert.Equal("data:image/png;base64,AAA", v.Find("img.dx-docview-img").GetAttribute("src"));
    }

    [Fact]
    public void Renders_a_pdf_in_a_native_embed()
    {
        IRenderedComponent<DxDocumentViewer> v = Render(new ViewerDocument("doc.pdf", DocumentKind.Pdf, "/files/doc.pdf"));

        var embed = v.Find("embed.dx-docview-pdf");
        Assert.Equal("application/pdf", embed.GetAttribute("type"));
        Assert.Equal("/files/doc.pdf", embed.GetAttribute("src"));
    }

    [Fact]
    public void Renders_markdown_through_the_safe_renderer()
    {
        IRenderedComponent<DxDocumentViewer> v = Render(new ViewerDocument("r.md", DocumentKind.Markdown, "# Title\n\nHello"));

        Assert.Contains("<h1", v.Markup);
        Assert.Contains("Title", v.Markup);
    }

    [Fact]
    public void Renders_text_encoded_in_a_pre()
    {
        IRenderedComponent<DxDocumentViewer> v = Render(new ViewerDocument("x.txt", DocumentKind.Text, "<b>raw</b>"));

        // Content is encoded (no live markup), shown verbatim as text.
        Assert.Contains("&lt;b&gt;raw&lt;/b&gt;", v.Markup);
        Assert.Equal("<b>raw</b>", v.Find("pre.dx-docview-text").TextContent);
    }

    [Fact]
    public void Unknown_kind_offers_a_download_link_only()
    {
        IRenderedComponent<DxDocumentViewer> v = Render(new ViewerDocument("a.zip", DocumentKind.Unknown, "/a.zip"));

        var link = v.Find(".dx-docview-unsupported a");
        Assert.Equal("/a.zip", link.GetAttribute("href"));
    }

    [Fact]
    public void Single_document_shows_no_sidebar()
    {
        IRenderedComponent<DxDocumentViewer> v = Render(new ViewerDocument("p.png", DocumentKind.Image, "x"));

        Assert.Empty(v.FindAll(".dx-docview-list"));
    }

    [Fact]
    public void Multiple_documents_list_in_a_sidebar_and_switch_on_click()
    {
        IRenderedComponent<DxDocumentViewer> v = Render(
            new ViewerDocument("a.png", DocumentKind.Image, "img-a"),
            new ViewerDocument("b.md", DocumentKind.Markdown, "# B"));

        Assert.Equal(2, v.FindAll(".dx-docview-item").Count);
        // Starts on the image.
        Assert.Single(v.FindAll("img.dx-docview-img"));

        v.FindAll(".dx-docview-item")[1].Click();   // switch to the markdown doc

        Assert.Empty(v.FindAll("img.dx-docview-img"));
        Assert.Contains("<h1", v.Markup);
    }

    [Fact]
    public void Zooming_an_image_changes_its_width()
    {
        IRenderedComponent<DxDocumentViewer> v = Render(new ViewerDocument("p.png", DocumentKind.Image, "x"));

        v.Find("button[aria-label='Zoom in']").Click();

        Assert.Contains("width:125%", v.Find("img.dx-docview-img").GetAttribute("style"));
    }

    // ---- PDF toolbar hardening (WCAG 2.2 AA) ----

    [Fact]
    public void Pdf_embed_carries_a_non_empty_title_for_an_accessible_name()
    {
        IRenderedComponent<DxDocumentViewer> v = Render(new ViewerDocument("doc.pdf", DocumentKind.Pdf, "/files/doc.pdf"));

        string? title = v.Find("embed.dx-docview-pdf").GetAttribute("title");
        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.Equal("doc.pdf", title);
    }

    [Fact]
    public void Pdf_embed_falls_back_to_a_generic_title_when_the_name_is_blank()
    {
        IRenderedComponent<DxDocumentViewer> v = Render(new ViewerDocument("  ", DocumentKind.Pdf, "/files/doc.pdf"));

        string? title = v.Find("embed.dx-docview-pdf").GetAttribute("title");
        Assert.False(string.IsNullOrWhiteSpace(title));
    }

    [Fact]
    public void Pdf_offers_an_accessible_download_fallback_link_pointing_at_the_source()
    {
        IRenderedComponent<DxDocumentViewer> v = Render(new ViewerDocument("doc.pdf", DocumentKind.Pdf, "/files/doc.pdf"));

        var link = v.Find(".dx-docview-pdf-fallback a");
        Assert.Equal("/files/doc.pdf", link.GetAttribute("href"));
        Assert.Equal("doc.pdf", link.GetAttribute("download"));
    }

    [Fact]
    public void Pdf_toolbar_renders_labeled_download_print_and_open_controls()
    {
        IRenderedComponent<DxDocumentViewer> v = Render(new ViewerDocument("doc.pdf", DocumentKind.Pdf, "/files/doc.pdf"));

        // Actions are grouped with an accessible group name.
        var group = v.Find(".dx-docview-actions");
        Assert.Equal("Document actions", group.GetAttribute("aria-label"));

        var download = v.Find("a[aria-label='Download doc.pdf']");
        Assert.Equal("/files/doc.pdf", download.GetAttribute("href"));
        Assert.Equal("doc.pdf", download.GetAttribute("download"));

        var print = v.Find("button[aria-label='Print doc.pdf']");
        Assert.Equal("button", print.GetAttribute("type"));

        var open = v.Find("a[aria-label='Open doc.pdf in a new tab']");
        Assert.Equal("/files/doc.pdf", open.GetAttribute("href"));
        Assert.Equal("_blank", open.GetAttribute("target"));
        Assert.Contains("noopener", open.GetAttribute("rel"));
    }

    [Fact]
    public void Print_button_invokes_the_interop_with_the_pdf_frame_id()
    {
        IRenderedComponent<DxDocumentViewer> v = Render(new ViewerDocument("doc.pdf", DocumentKind.Pdf, "/files/doc.pdf"));

        string embedId = v.Find("embed.dx-docview-pdf").GetAttribute("id")!;
        v.Find("button[aria-label='Print doc.pdf']").Click();

        Assert.Single(interop.PrintedFrames);
        Assert.Equal(embedId, interop.PrintedFrames[0]);
    }

    [Fact]
    public void Toolbar_can_be_turned_off_but_the_download_fallback_remains()
    {
        IRenderedComponent<DxDocumentViewer> v = RenderComponent<DxDocumentViewer>(p => p
            .Add(c => c.Documents, new[] { new ViewerDocument("doc.pdf", DocumentKind.Pdf, "/files/doc.pdf") })
            .Add(c => c.ShowToolbar, false));

        Assert.Empty(v.FindAll(".dx-docview-toolbar"));
        // The accessible fallback link is independent of the toolbar.
        Assert.Equal("/files/doc.pdf", v.Find(".dx-docview-pdf-fallback a").GetAttribute("href"));
    }

    [Fact]
    public void Individual_action_buttons_can_be_toggled_off()
    {
        IRenderedComponent<DxDocumentViewer> v = RenderComponent<DxDocumentViewer>(p => p
            .Add(c => c.Documents, new[] { new ViewerDocument("doc.pdf", DocumentKind.Pdf, "/files/doc.pdf") })
            .Add(c => c.ShowPrint, false)
            .Add(c => c.ShowOpenInNewTab, false));

        Assert.Empty(v.FindAll("button[aria-label='Print doc.pdf']"));
        Assert.Empty(v.FindAll("a[aria-label='Open doc.pdf in a new tab']"));
        // Download stays.
        Assert.Single(v.FindAll("a[aria-label='Download doc.pdf']"));
    }
}
