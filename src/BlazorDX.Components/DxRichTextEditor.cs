using BlazorDX.Interop;
using BlazorDX.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A WYSIWYG rich-text editor over a <c>contentEditable</c> region with a
/// formatting toolbar. Edited HTML is read back from the DOM and routed through an
/// injected <see cref="HtmlSanitizer"/> before it becomes <see cref="Value"/> — so
/// the application's own vetted policy decides what HTML survives (BlazorDX ships
/// no HTML parser of its own). Without a real sanitizer the default policy renders
/// content inert; supply one for actual rich editing. Two-way bind via
/// <c>@bind-Value</c>. Styling is token-driven (see dx-richtext.css).
/// </summary>
public sealed class DxRichTextEditor : ComponentBase
{
    private static readonly HtmlSanitizer InertSanitizer = new();

    private readonly string editorId = $"dx-rte-{Guid.NewGuid():N}";
    private string lastEmitted = string.Empty;

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>The sanitizer applied to edited HTML. Defaults to an inert (encode-all) policy.</summary>
    [Parameter] public HtmlSanitizer? Sanitizer { get; set; }

    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public string? Class { get; set; }

    [Inject] private IRichTextInterop Interop { get; set; } = default!;

    private HtmlSanitizer ActiveSanitizer => Sanitizer ?? InertSanitizer;

    private static readonly (string Command, string Value, string Glyph, string Label)[] Tools =
    [
        ("bold", "", "B", "Bold"),
        ("italic", "", "I", "Italic"),
        ("underline", "", "U", "Underline"),
        ("strikeThrough", "", "S", "Strikethrough"),
        ("formatBlock", "<h2>", "H", "Heading"),
        ("insertUnorderedList", "", "•", "Bullet list"),
        ("insertOrderedList", "", "1.", "Numbered list"),
        ("createLink", "", "🔗", "Insert link"),
        ("removeFormat", "", "⌫", "Clear formatting"),
    ];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-rte {Class}".TrimEnd());

        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-rte-toolbar");
        builder.AddAttribute(4, "role", "toolbar");
        builder.AddAttribute(5, "aria-label", "Formatting");

        foreach ((string command, string value, string glyph, string label) in Tools)
        {
            builder.OpenElement(6, "button");
            builder.SetKey(command);
            builder.AddAttribute(7, "type", "button");
            builder.AddAttribute(8, "class", "dx-rte-tool");
            builder.AddAttribute(9, "title", label);
            builder.AddAttribute(10, "aria-label", label);
            // Keep the editor's selection — don't let the button steal focus.
            builder.AddAttribute(11, "onmousedown", EventCallback.Factory.Create<MouseEventArgs>(this, () => { }));
            builder.AddEventPreventDefaultAttribute(12, "onmousedown", true);
            builder.AddAttribute(13, "onclick", EventCallback.Factory.Create(this, () => CommandAsync(command, value)));
            builder.AddContent(14, glyph);
            builder.CloseElement();
        }

        builder.CloseElement();

        builder.OpenElement(15, "div");
        builder.AddAttribute(16, "id", editorId);
        builder.AddAttribute(17, "class", "dx-rte-surface");
        builder.AddAttribute(18, "contenteditable", "true");
        builder.AddAttribute(19, "role", "textbox");
        builder.AddAttribute(20, "aria-multiline", "true");
        if (!string.IsNullOrEmpty(AriaLabel))
        {
            builder.AddAttribute(21, "aria-label", AriaLabel);
        }

        builder.AddAttribute(22, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, _ => SyncFromDomAsync()));
        // The DOM inside the editable is owned by the browser; never render content
        // here after mount, or Blazor's diff would reset the caret.
        builder.CloseElement();

        builder.CloseElement();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !string.IsNullOrEmpty(Value))
        {
            lastEmitted = ActiveSanitizer.Sanitize(Value).Value;
            await Interop.SetHtmlAsync(editorId, lastEmitted);
        }
    }

    private async Task CommandAsync(string command, string value)
    {
        // createLink needs a URL prompt + scheme validation, which lives in the bridge.
        if (command == "createLink")
        {
            await Interop.CreateLinkAsync();
        }
        else
        {
            await Interop.ExecAsync(command, value);
        }

        await SyncFromDomAsync();
    }

    private async Task SyncFromDomAsync()
    {
        string raw = await Interop.GetHtmlAsync(editorId);
        string clean = ActiveSanitizer.Sanitize(raw).Value;
        if (clean == lastEmitted)
        {
            return;
        }

        lastEmitted = clean;
        Value = clean;
        if (ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(clean);
        }
    }
}
