using BlazorDX.Interop;
using BlazorDX.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>A color toolbar command: <c>foreColor</c> (text) or <c>hiliteColor</c>
/// (highlight) paired with a CSS color. Passed to a model-driven host via
/// <see cref="DxRichTextEditor.OnColorCommand"/>.</summary>
public readonly record struct ColorCommandArgs(string Command, string Color);

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

    /// <summary>
    /// Optional interceptor for the model-drivable toolbar commands: <c>bold</c>, <c>italic</c>,
    /// <c>underline</c>, <c>strikeThrough</c>, <c>removeFormat</c>, and the four
    /// <c>justify*</c> alignments. When set, those buttons invoke this callback instead of
    /// <c>document.execCommand</c>, so a host can apply the edit to its own model and re-seed
    /// the surface (the model-driven editing core, ADR-0015). All other tools keep their
    /// built-in behavior.
    /// </summary>
    [Parameter] public EventCallback<string> OnCommand { get; set; }

    /// <summary>Invoked for the undo keyboard shortcut (Ctrl/Cmd+Z), so a host can drive its
    /// own history instead of the browser's contentEditable undo.</summary>
    [Parameter] public EventCallback OnUndo { get; set; }

    /// <summary>Invoked for the redo keyboard shortcut (Ctrl/Cmd+Y or Shift+Ctrl/Cmd+Z).</summary>
    [Parameter] public EventCallback OnRedo { get; set; }

    /// <summary>
    /// Optional interceptor for the color inputs (<c>foreColor</c> text, <c>hiliteColor</c>
    /// highlight). When set, picking a color invokes this callback instead of applying the
    /// color through the browser, so a model-driven host owns the edit (ADR-0015).
    /// </summary>
    [Parameter] public EventCallback<ColorCommandArgs> OnColorCommand { get; set; }

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
        ("justifyLeft", "", "⇤", "Align left"),
        ("justifyCenter", "", "↔", "Align center"),
        ("justifyRight", "", "⇥", "Align right"),
        ("justifyFull", "", "≡", "Justify"),
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

        // Color pickers (native <input type=color>) — not .dx-rte-tool buttons. The bridge
        // restores the editor selection before applying, so clicking the swatch is safe.
        BuildColorInput(builder, 20, "foreColor", "Text color", "#000000");
        BuildColorInput(builder, 30, "hiliteColor", "Highlight color", "#ffff00");

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
        if (!firstRender)
        {
            return;
        }

        if (!string.IsNullOrEmpty(Value))
        {
            lastEmitted = ActiveSanitizer.Sanitize(Value).Value;
            await Interop.SetHtmlAsync(editorId, lastEmitted);
        }

        // Ctrl/Cmd keyboard shortcuts route through the same command/history path as the
        // toolbar, with the bridge suppressing the browser's native (model-bypassing) defaults.
        await Interop.SubscribeShortcutsAsync(editorId, OnShortcut);
    }

    // The bridge calls this (off the JS keydown) with a mapped command; marshal back onto the
    // renderer's context and run it through the normal command/history path.
    private void OnShortcut(string command) => _ = InvokeAsync(() => HandleShortcutAsync(command));

    private async Task HandleShortcutAsync(string command)
    {
        switch (command)
        {
            case "undo":
                if (OnUndo.HasDelegate)
                {
                    await OnUndo.InvokeAsync();
                }

                break;
            case "redo":
                if (OnRedo.HasDelegate)
                {
                    await OnRedo.InvokeAsync();
                }

                break;
            default:
                await CommandAsync(command, string.Empty);
                break;
        }
    }

    private void BuildColorInput(RenderTreeBuilder builder, int seq, string command, string label, string initial)
    {
        builder.OpenElement(seq, "input");
        builder.AddAttribute(seq + 1, "type", "color");
        builder.AddAttribute(seq + 2, "class", "dx-rte-color");
        builder.AddAttribute(seq + 3, "aria-label", label);
        builder.AddAttribute(seq + 4, "title", label);
        builder.AddAttribute(seq + 5, "value", initial);
        builder.AddAttribute(seq + 6, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(
            this, e => CommandColorAsync(command, e.Value?.ToString() ?? initial)));
        builder.CloseElement();
    }

    private async Task CommandAsync(string command, string value)
    {
        // Model-driven host owns the basic inline formats: hand off and let it edit + re-seed.
        if (OnCommand.HasDelegate && IsModelCommand(command))
        {
            await OnCommand.InvokeAsync(command);
            return;
        }

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

    private static bool IsModelCommand(string command) =>
        command is "bold" or "italic" or "underline" or "strikeThrough" or "removeFormat"
            or "formatBlock" or "insertUnorderedList" or "insertOrderedList" or "createLink"
            or "justifyLeft" or "justifyCenter" or "justifyRight" or "justifyFull";

    /// <summary>Prompts for a URL (http/https/mailto only) and returns it, or empty on cancel —
    /// the model-driven host then sets the link itself.</summary>
    public ValueTask<string> PromptLinkAsync() => Interop.PromptLinkAsync();

    /// <summary>The current owned selection as <c>"containerIndex,start,end"</c> (see
    /// <see cref="IRichTextInterop.GetSelectionRangeAsync"/>), or empty if unaddressable.</summary>
    public ValueTask<string> GetSelectionRangeAsync() => Interop.GetSelectionRangeAsync(editorId);

    /// <summary>Restores a selection by run-container index and character offsets, refocusing
    /// the editor — used after a model edit re-renders the surface.</summary>
    public ValueTask SetSelectionRangeAsync(int containerIndex, int start, int end) =>
        Interop.SetSelectionRangeAsync(editorId, containerIndex, start, end);

    /// <summary>
    /// Replaces the editing surface's HTML in place (no re-mount), so a model-driven host can
    /// push a freshly rendered model into the DOM and then restore the caret with
    /// <see cref="SetSelectionRangeAsync"/>. The HTML is sanitized like the initial seed.
    /// </summary>
    public async Task ReseedAsync(string html)
    {
        lastEmitted = ActiveSanitizer.Sanitize(html ?? string.Empty).Value;
        await Interop.SetHtmlAsync(editorId, lastEmitted);
    }

    private async Task CommandColorAsync(string command, string color)
    {
        if (OnColorCommand.HasDelegate)
        {
            await OnColorCommand.InvokeAsync(new ColorCommandArgs(command, color));
            return; // host owns the edit + re-seed + selection
        }

        await Interop.ApplyColorAsync(command, color);
        await SyncFromDomAsync();
    }

    /// <summary>
    /// Selects and scrolls to the next (or previous) occurrence of <paramref name="query"/>
    /// in the editing surface, wrapping at the ends. Returns the 1-based match index, or 0
    /// if there are no matches. The selection is owned via the bridge (no model mapping).
    /// </summary>
    public ValueTask<int> FindNextAsync(string query, bool forward, bool caseSensitive) =>
        Interop.FindInEditorAsync(editorId, query, forward, caseSensitive);

    /// <summary>
    /// Reports the caret's table position as <c>"tableIndex,rowIndex,colIndex"</c> (0-based,
    /// table index in document order), or an empty string when the caret is not in a table.
    /// </summary>
    public ValueTask<string> GetTableCellAsync() => Interop.GetTableCellAsync(editorId);

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
