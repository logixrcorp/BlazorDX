using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A drag-and-drop file upload zone built over the framework's <see cref="InputFile"/>
/// (so file selection, drag-drop, and the security model come from the platform — no
/// custom JS). Shows the chosen files with sizes and a remove button. A leaf component;
/// styling is CSS-variable driven (see dx-display.css). The component surfaces the
/// selected <see cref="IBrowserFile"/>s via <see cref="OnFilesSelected"/>; actually
/// streaming them somewhere is the consumer's job.
/// </summary>
public sealed class DxFileUpload : ComponentBase
{
    /// <summary>The accept filter (e.g. <c>image/*,.pdf</c>).</summary>
    [Parameter] public string? Accept { get; set; }

    /// <summary>Allow selecting more than one file.</summary>
    [Parameter] public bool Multiple { get; set; } = true;

    /// <summary>Maximum number of files accepted from a selection.</summary>
    [Parameter] public int MaxFiles { get; set; } = 20;

    /// <summary>Maximum size per file in bytes; larger files are skipped.</summary>
    [Parameter] public long MaxSize { get; set; } = 10L * 1024 * 1024;

    /// <summary>Prompt shown in the drop zone.</summary>
    [Parameter] public string PromptText { get; set; } = "Drag files here, or click to browse";

    /// <summary>Raised with the current set of accepted files whenever it changes.</summary>
    [Parameter] public EventCallback<IReadOnlyList<IBrowserFile>> OnFilesSelected { get; set; }

    /// <summary>Extra CSS classes appended to the root.</summary>
    [Parameter] public string? Class { get; set; }

    private readonly List<IBrowserFile> files = new();

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-upload {Class}".TrimEnd());

        // The label makes the whole zone clickable; the InputFile is stretched over it
        // (see CSS) so files dropped anywhere on the zone hit the native input.
        builder.OpenElement(2, "label");
        builder.AddAttribute(3, "class", "dx-upload-zone");

        builder.OpenComponent<InputFile>(4);
        builder.AddComponentParameter(5, "class", "dx-upload-input");
        builder.AddComponentParameter(6, "OnChange", EventCallback.Factory.Create<InputFileChangeEventArgs>(this, HandleChangeAsync));
        if (Multiple)
        {
            builder.AddComponentParameter(7, "multiple", true);
        }

        if (Accept is not null)
        {
            builder.AddComponentParameter(8, "accept", Accept);
        }

        builder.CloseComponent();

        builder.OpenElement(9, "span");
        builder.AddAttribute(10, "class", "dx-upload-prompt");
        builder.AddContent(11, PromptText);
        builder.CloseElement();

        builder.CloseElement();   // label

        if (files.Count > 0)
        {
            builder.OpenElement(12, "ul");
            builder.AddAttribute(13, "class", "dx-upload-list");
            foreach (IBrowserFile file in files)
            {
                IBrowserFile captured = file;
                builder.OpenElement(14, "li");
                builder.SetKey(file);
                builder.AddAttribute(15, "class", "dx-upload-item");

                builder.OpenElement(16, "span");
                builder.AddAttribute(17, "class", "dx-upload-name");
                builder.AddContent(18, file.Name);
                builder.CloseElement();

                builder.OpenElement(19, "span");
                builder.AddAttribute(20, "class", "dx-upload-size");
                builder.AddContent(21, FormatSize(file.Size));
                builder.CloseElement();

                builder.OpenElement(22, "button");
                builder.AddAttribute(23, "type", "button");
                builder.AddAttribute(24, "class", "dx-upload-remove");
                builder.AddAttribute(25, "aria-label", $"Remove {file.Name}");
                builder.AddAttribute(26, "onclick", EventCallback.Factory.Create(this, () => RemoveAsync(captured)));
                builder.AddContent(27, "×");
                builder.CloseElement();

                builder.CloseElement();
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private async Task HandleChangeAsync(InputFileChangeEventArgs args)
    {
        files.Clear();
        foreach (IBrowserFile file in args.GetMultipleFiles(MaxFiles))
        {
            if (file.Size <= MaxSize)
            {
                files.Add(file);
            }
        }

        await NotifyAsync();
    }

    private async Task RemoveAsync(IBrowserFile file)
    {
        files.Remove(file);
        await NotifyAsync();
    }

    private Task NotifyAsync() =>
        OnFilesSelected.HasDelegate ? OnFilesSelected.InvokeAsync(files) : Task.CompletedTask;

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
    };
}
