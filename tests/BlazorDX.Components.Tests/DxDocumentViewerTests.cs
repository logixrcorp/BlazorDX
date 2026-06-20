using BlazorDX.Components;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>DxDocumentViewer format dispatch, the document switcher, zoom, and kind inference.</summary>
public sealed class DxDocumentViewerTests : TestContext
{
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
}
