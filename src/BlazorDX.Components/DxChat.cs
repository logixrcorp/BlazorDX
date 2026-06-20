using BlazorDX.Primitives.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// An AI chat surface: a scrolling transcript of role-styled message bubbles plus
/// a composer. Assistant messages render through <see cref="DxMarkdown"/> (so the
/// model's Markdown is shown safely), user messages render as plain text. A typing
/// indicator shows while <see cref="Busy"/>. Send with the button or Ctrl+Enter;
/// <see cref="OnSend"/> receives the text and the composer clears. Styling is
/// token-driven (see dx-chat.css).
/// </summary>
public sealed class DxChat : ComponentBase
{
    private string draft = string.Empty;

    [Parameter] public IReadOnlyList<ChatMessage> Messages { get; set; } = [];

    /// <summary>Raised with the composed text when the user sends.</summary>
    [Parameter] public EventCallback<string> OnSend { get; set; }

    /// <summary>Show a typing indicator (e.g. while awaiting a response).</summary>
    [Parameter] public bool Busy { get; set; }

    /// <summary>Render assistant message content as Markdown (default true).</summary>
    [Parameter] public bool RenderMarkdown { get; set; } = true;

    [Parameter] public string Placeholder { get; set; } = "Send a message…  (Ctrl+Enter)";

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chat {Class}".TrimEnd());

        BuildTranscript(builder);
        BuildComposer(builder);

        builder.CloseElement();
    }

    private void BuildTranscript(RenderTreeBuilder builder)
    {
        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-chat-log");
        builder.AddAttribute(4, "role", "log");
        builder.AddAttribute(5, "aria-live", "polite");

        for (int i = 0; i < Messages.Count; i++)
        {
            BuildMessage(builder, Messages[i], i);
        }

        if (Busy)
        {
            BuildTyping(builder);
        }

        builder.CloseElement();
    }

    private void BuildMessage(RenderTreeBuilder builder, ChatMessage message, int index)
    {
        bool assistant = message.Role == ChatRole.Assistant;
        builder.OpenElement(6, "div");
        builder.SetKey(index);
        builder.AddAttribute(7, "class", $"dx-chat-msg dx-chat-{message.Role.ToString().ToLowerInvariant()}");

        builder.OpenElement(8, "div");
        builder.AddAttribute(9, "class", "dx-chat-role");
        builder.AddContent(10, message.Role.ToString());
        builder.CloseElement();

        builder.OpenElement(11, "div");
        builder.AddAttribute(12, "class", "dx-chat-bubble");
        if (assistant && RenderMarkdown)
        {
            builder.OpenComponent<DxMarkdown>(13);
            builder.AddComponentParameter(14, nameof(DxMarkdown.Value), message.Content);
            builder.CloseComponent();
        }
        else
        {
            builder.AddContent(15, message.Content);
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private static void BuildTyping(RenderTreeBuilder builder)
    {
        builder.OpenElement(16, "div");
        builder.AddAttribute(17, "class", "dx-chat-msg dx-chat-assistant");
        builder.OpenElement(18, "div");
        builder.AddAttribute(19, "class", "dx-chat-bubble dx-chat-typing");
        builder.AddAttribute(20, "aria-label", "Assistant is typing");
        for (int i = 0; i < 3; i++)
        {
            builder.OpenElement(21, "span");
            builder.SetKey(i);
            builder.AddAttribute(22, "class", "dx-chat-dot");
            builder.CloseElement();
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private void BuildComposer(RenderTreeBuilder builder)
    {
        builder.OpenElement(23, "div");
        builder.AddAttribute(24, "class", "dx-chat-composer");

        builder.OpenElement(25, "textarea");
        builder.AddAttribute(26, "class", "dx-chat-input");
        builder.AddAttribute(27, "rows", 1);
        builder.AddAttribute(28, "placeholder", Placeholder);
        builder.AddAttribute(29, "aria-label", "Message");
        builder.AddAttribute(30, "value", draft);
        builder.AddAttribute(31, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, e => draft = e.Value as string ?? string.Empty));
        builder.AddAttribute(32, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));
        builder.CloseElement();

        builder.OpenElement(33, "button");
        builder.AddAttribute(34, "type", "button");
        builder.AddAttribute(35, "class", "dx-chat-send");
        builder.AddAttribute(36, "aria-label", "Send");
        builder.AddAttribute(37, "disabled", Busy || string.IsNullOrWhiteSpace(draft));
        builder.AddAttribute(38, "onclick", EventCallback.Factory.Create(this, SendAsync));
        builder.AddContent(39, "Send");
        builder.CloseElement();

        builder.CloseElement();
    }

    // Ctrl+Enter sends. Plain Enter inserts a newline (multi-line compose), so we
    // never need to preventDefault — keeping the composer pure-Blazor.
    private Task OnKeyDownAsync(KeyboardEventArgs args) =>
        args is { Key: "Enter", CtrlKey: true } ? SendAsync() : Task.CompletedTask;

    private Task SendAsync()
    {
        if (Busy || string.IsNullOrWhiteSpace(draft))
        {
            return Task.CompletedTask;
        }

        string text = draft.Trim();
        draft = string.Empty;
        return OnSend.HasDelegate ? OnSend.InvokeAsync(text) : Task.CompletedTask;
    }
}
