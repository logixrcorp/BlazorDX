using BlazorDX.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A canvas-backed image editor: load an image, adjust brightness / contrast /
/// saturation, toggle grayscale / sepia / invert, rotate and flip, then download
/// the result. Editing is non-destructive — every change repaints the canvas from
/// the original decoded bitmap (held in the TypeScript bridge). The .NET side owns
/// the edit state; the DOM/canvas work happens in <c>image-editor.js</c> via
/// <see cref="IImageEditorInterop"/>. Styling is CSS-variable driven (see
/// dx-display.css).
/// </summary>
public sealed class DxImageEditor : ComponentBase, IAsyncDisposable
{
    private readonly string canvasId = $"dx-imgedit-{Guid.NewGuid():N}";

    private int brightness = 100, contrast = 100, saturate = 100;
    private int grayscale, sepia, invert, rotate;
    private bool flipH, flipV;
    private bool loaded;

    /// <summary>Largest image accepted from the file picker, in bytes.</summary>
    [Parameter] public long MaxSize { get; set; } = 12L * 1024 * 1024;

    /// <summary>File name used for the downloaded PNG.</summary>
    [Parameter] public string DownloadName { get; set; } = "edited.png";

    /// <summary>Optional image to load on first render (a data URL or same-origin URL).</summary>
    [Parameter] public string? InitialSource { get; set; }

    /// <summary>Raised after each render with the current image as a PNG data URL.</summary>
    [Parameter] public EventCallback<string> OnChange { get; set; }

    /// <summary>Extra CSS classes appended to the editor root.</summary>
    [Parameter] public string? Class { get; set; }

    [Inject] private IImageEditorInterop Interop { get; set; } = default!;

    private string EditsJson() =>
        $"{{\"brightness\":{brightness},\"contrast\":{contrast},\"saturate\":{saturate}," +
        $"\"grayscale\":{grayscale},\"sepia\":{sepia},\"invert\":{invert}," +
        $"\"rotate\":{rotate},\"flipH\":{Json(flipH)},\"flipV\":{Json(flipV)}}}";

    private static string Json(bool value) => value ? "true" : "false";

    private static int Parse(ChangeEventArgs e, int fallback) =>
        int.TryParse(e.Value as string, out int v) ? v : fallback;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !loaded && !string.IsNullOrEmpty(InitialSource))
        {
            await LoadSourceAsync(InitialSource);
            StateHasChanged();
        }
    }

    private async Task LoadAsync(InputFileChangeEventArgs args)
    {
        IBrowserFile file = args.File;
        using MemoryStream buffer = new();
        await file.OpenReadStream(MaxSize).CopyToAsync(buffer);
        string mime = string.IsNullOrEmpty(file.ContentType) ? "image/png" : file.ContentType;
        await LoadSourceAsync($"data:{mime};base64,{Convert.ToBase64String(buffer.ToArray())}");
    }

    private async Task LoadSourceAsync(string source)
    {
        await Interop.LoadImageAsync(canvasId, source);
        loaded = true;
        ResetState();
        await RenderAsync();
    }

    private async Task RenderAsync()
    {
        if (!loaded)
        {
            return;
        }

        string result = await Interop.RenderAsync(canvasId, EditsJson());
        if (OnChange.HasDelegate)
        {
            await OnChange.InvokeAsync(result);
        }
    }

    private void ResetState()
    {
        brightness = contrast = saturate = 100;
        grayscale = sepia = invert = rotate = 0;
        flipH = flipV = false;
    }

    private Task ResetAsync()
    {
        ResetState();
        return RenderAsync();
    }

    private Task ToggleAsync(Func<int> get, Action<int> set)
    {
        set(get() > 0 ? 0 : 100);
        return RenderAsync();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-imgedit {Class}".TrimEnd());

        BuildToolbar(builder);

        // Stage: the canvas (always in the DOM so its id resolves) + a load prompt.
        builder.OpenElement(300, "div");
        builder.AddAttribute(301, "class", "dx-imgedit-stage");

        if (!loaded)
        {
            builder.OpenElement(302, "span");
            builder.AddAttribute(303, "class", "dx-imgedit-empty");
            builder.AddContent(304, "Open an image to start editing");
            builder.CloseElement();
        }

        builder.OpenElement(305, "canvas");
        builder.AddAttribute(306, "id", canvasId);
        builder.AddAttribute(307, "class", "dx-imgedit-canvas");
        builder.AddAttribute(308, "role", "img");
        builder.AddAttribute(309, "aria-label", "Edited image preview");
        if (!loaded)
        {
            builder.AddAttribute(310, "hidden", true);
        }

        builder.CloseElement();
        builder.CloseElement();   // stage

        builder.CloseElement();   // root
    }

    private void BuildToolbar(RenderTreeBuilder builder)
    {
        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-imgedit-toolbar");

        // Open image (native InputFile inside a styled label).
        builder.OpenElement(4, "label");
        builder.AddAttribute(5, "class", "dx-imgedit-open");
        builder.OpenComponent<InputFile>(6);
        builder.AddComponentParameter(7, "class", "dx-imgedit-file");
        builder.AddComponentParameter(8, "accept", "image/*");
        builder.AddComponentParameter(9, "OnChange", EventCallback.Factory.Create<InputFileChangeEventArgs>(this, LoadAsync));
        builder.CloseComponent();
        builder.AddContent(10, loaded ? "Replace image" : "Open image");
        builder.CloseElement();

        Slider(builder, 20, "Brightness", brightness, v => brightness = v);
        Slider(builder, 40, "Contrast", contrast, v => contrast = v);
        Slider(builder, 60, "Saturation", saturate, v => saturate = v);

        builder.OpenElement(80, "div");
        builder.AddAttribute(81, "class", "dx-imgedit-actions");
        Toggle(builder, 90, "Grayscale", grayscale, () => grayscale, v => grayscale = v);
        Toggle(builder, 100, "Sepia", sepia, () => sepia, v => sepia = v);
        Toggle(builder, 110, "Invert", invert, () => invert, v => invert = v);
        Action(builder, 120, "⟲", "Rotate left", () => { rotate = (rotate + 270) % 360; return RenderAsync(); });
        Action(builder, 130, "⟳", "Rotate right", () => { rotate = (rotate + 90) % 360; return RenderAsync(); });
        Action(builder, 140, "⇋", "Flip horizontal", () => { flipH = !flipH; return RenderAsync(); });
        Action(builder, 150, "⇅", "Flip vertical", () => { flipV = !flipV; return RenderAsync(); });
        Action(builder, 160, "Reset", "Reset edits", ResetAsync);
        Action(builder, 170, "⭳ Download", "Download image",
            () => Interop.DownloadAsync(canvasId, DownloadName, "image/png").AsTask());
        builder.CloseElement();

        builder.CloseElement();   // toolbar
    }

    private void Slider(RenderTreeBuilder builder, int seq, string label, int value, Action<int> set)
    {
        builder.OpenElement(seq, "label");
        builder.AddAttribute(seq + 1, "class", "dx-imgedit-slider");
        builder.AddContent(seq + 2, label);

        builder.OpenElement(seq + 3, "input");
        builder.AddAttribute(seq + 4, "type", "range");
        builder.AddAttribute(seq + 5, "min", "0");
        builder.AddAttribute(seq + 6, "max", "200");
        builder.AddAttribute(seq + 7, "value", value);
        builder.AddAttribute(seq + 8, "aria-label", label);
        if (!loaded)
        {
            builder.AddAttribute(seq + 9, "disabled", true);
        }

        builder.AddAttribute(seq + 10, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, async e =>
        {
            set(Parse(e, value));
            await RenderAsync();
        }));
        builder.CloseElement();
        builder.CloseElement();
    }

    private void Toggle(RenderTreeBuilder builder, int seq, string label, int value, Func<int> get, Action<int> set)
    {
        builder.OpenElement(seq, "button");
        builder.AddAttribute(seq + 1, "type", "button");
        builder.AddAttribute(seq + 2, "class", value > 0 ? "dx-imgedit-btn dx-imgedit-on" : "dx-imgedit-btn");
        builder.AddAttribute(seq + 3, "aria-pressed", value > 0 ? "true" : "false");
        if (!loaded)
        {
            builder.AddAttribute(seq + 4, "disabled", true);
        }

        builder.AddAttribute(seq + 5, "onclick", EventCallback.Factory.Create(this, () => ToggleAsync(get, set)));
        builder.AddContent(seq + 6, label);
        builder.CloseElement();
    }

    private void Action(RenderTreeBuilder builder, int seq, string label, string ariaLabel, Func<Task> onClick)
    {
        builder.OpenElement(seq, "button");
        builder.AddAttribute(seq + 1, "type", "button");
        builder.AddAttribute(seq + 2, "class", "dx-imgedit-btn");
        builder.AddAttribute(seq + 3, "aria-label", ariaLabel);
        if (!loaded)
        {
            builder.AddAttribute(seq + 4, "disabled", true);
        }

        builder.AddAttribute(seq + 5, "onclick", EventCallback.Factory.Create(this, onClick));
        builder.AddContent(seq + 6, label);
        builder.CloseElement();
    }

    public async ValueTask DisposeAsync()
    {
        if (loaded)
        {
            await Interop.DisposeCanvasAsync(canvasId);
        }
    }
}
