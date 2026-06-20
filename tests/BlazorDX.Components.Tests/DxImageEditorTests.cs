using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// DxImageEditor chrome, gating, and the edit-state JSON sent to the canvas bridge
/// (the bridge itself is a recording fake — actual painting is browser-verified).
/// </summary>
public sealed class DxImageEditorTests : TestContext
{
    private sealed class RecordingImageEditor : IImageEditorInterop
    {
        public List<string> Edits { get; } = new();
        public string? LoadedDataUrl { get; private set; }
        public string? DownloadFile { get; private set; }

        public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;
        public ValueTask LoadImageAsync(string canvasId, string dataUrl)
        {
            LoadedDataUrl = dataUrl;
            return ValueTask.CompletedTask;
        }

        public ValueTask<string> RenderAsync(string canvasId, string editsJson)
        {
            Edits.Add(editsJson);
            return ValueTask.FromResult("data:image/png;base64,AAAA");
        }

        public ValueTask DownloadAsync(string canvasId, string filename, string mime)
        {
            DownloadFile = filename;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeCanvasAsync(string canvasId) => ValueTask.CompletedTask;
    }

    private readonly RecordingImageEditor fake = new();

    public DxImageEditorTests()
    {
        Services.AddScoped<IImageEditorInterop>(_ => fake);
    }

    private IRenderedComponent<DxImageEditor> RenderEditor() => RenderComponent<DxImageEditor>();

    private void Load(IRenderedComponent<DxImageEditor> editor)
    {
        InputFileContent file = InputFileContent.CreateFromBinary([1, 2, 3, 4], "pic.png", contentType: "image/png");
        editor.FindComponent<InputFile>().UploadFiles(file);
        editor.WaitForAssertion(() => Assert.NotNull(fake.LoadedDataUrl));
    }

    [Fact]
    public void Renders_toolbar_and_canvas_with_controls_disabled_before_load()
    {
        IRenderedComponent<DxImageEditor> editor = RenderEditor();

        Assert.Single(editor.FindAll("canvas.dx-imgedit-canvas"));
        Assert.Equal(3, editor.FindAll("input[type=range]").Count);   // brightness/contrast/saturation
        Assert.NotEmpty(editor.FindAll(".dx-imgedit-btn"));
        // Every adjustment control is disabled until an image is loaded.
        Assert.All(editor.FindAll(".dx-imgedit-btn"), b => Assert.True(b.HasAttribute("disabled")));
    }

    [Fact]
    public void Loading_an_image_decodes_it_and_does_an_initial_render()
    {
        IRenderedComponent<DxImageEditor> editor = RenderEditor();

        Load(editor);

        Assert.StartsWith("data:image/png;base64,", fake.LoadedDataUrl);
        // The initial render uses the neutral defaults.
        Assert.Contains("\"brightness\":100", fake.Edits[0]);
        Assert.Contains("\"grayscale\":0", fake.Edits[0]);
        Assert.DoesNotContain("disabled", editor.Find("button[aria-label='Rotate right']").OuterHtml);
    }

    [Fact]
    public void Toggling_grayscale_sends_it_to_the_bridge_and_marks_pressed()
    {
        IRenderedComponent<DxImageEditor> editor = RenderEditor();
        Load(editor);

        editor.FindAll(".dx-imgedit-btn").First(b => b.TextContent == "Grayscale").Click();

        Assert.Contains("\"grayscale\":100", fake.Edits[^1]);
        Assert.Equal("true", editor.FindAll(".dx-imgedit-btn").First(b => b.TextContent == "Grayscale").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Rotate_right_advances_the_angle()
    {
        IRenderedComponent<DxImageEditor> editor = RenderEditor();
        Load(editor);

        editor.Find("button[aria-label='Rotate right']").Click();

        Assert.Contains("\"rotate\":90", fake.Edits[^1]);
    }

    [Fact]
    public void Reset_returns_every_edit_to_its_default()
    {
        IRenderedComponent<DxImageEditor> editor = RenderEditor();
        Load(editor);
        editor.FindAll(".dx-imgedit-btn").First(b => b.TextContent == "Sepia").Click();

        editor.Find("button[aria-label='Reset edits']").Click();

        string last = fake.Edits[^1];
        Assert.Contains("\"sepia\":0", last);
        Assert.Contains("\"rotate\":0", last);
        Assert.Contains("\"flipH\":false", last);
    }

    [Fact]
    public void Download_passes_the_configured_file_name()
    {
        IRenderedComponent<DxImageEditor> editor = RenderComponent<DxImageEditor>(p => p.Add(c => c.DownloadName, "out.png"));
        Load(editor);

        editor.Find("button[aria-label='Download image']").Click();

        Assert.Equal("out.png", fake.DownloadFile);
    }
}
