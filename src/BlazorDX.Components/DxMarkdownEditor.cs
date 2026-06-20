using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A split Markdown editor: a source textarea on the left and a live, sanitized
/// preview on the right (rendered through <see cref="DxMarkdown"/>). Two-way bind
/// the source via <c>@bind-Value</c>. Styling is token-driven (see dx-markdown.css).
/// </summary>
public sealed class DxMarkdownEditor : ComponentBase
{
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public int Rows { get; set; } = 12;

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-md-editor {Class}".TrimEnd());

        builder.OpenElement(2, "textarea");
        builder.AddAttribute(3, "class", "dx-md-source");
        builder.AddAttribute(4, "rows", Rows);
        builder.AddAttribute(5, "spellcheck", "false");
        builder.AddAttribute(6, "aria-label", "Markdown source");
        builder.AddAttribute(7, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, OnInputAsync));
        builder.AddContent(8, Value);
        builder.CloseElement();

        builder.OpenComponent<DxMarkdown>(9);
        builder.AddComponentParameter(10, nameof(DxMarkdown.Value), Value);
        builder.AddComponentParameter(11, nameof(DxMarkdown.Class), "dx-md-preview");
        builder.CloseComponent();

        builder.CloseElement();
    }

    private Task OnInputAsync(ChangeEventArgs args)
    {
        string? text = args.Value as string;
        Value = text;
        return ValueChanged.HasDelegate ? ValueChanged.InvokeAsync(text) : Task.CompletedTask;
    }
}
