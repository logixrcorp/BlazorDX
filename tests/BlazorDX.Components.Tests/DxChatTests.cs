using BlazorDX.Components;
using BlazorDX.Primitives.Chat;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Chat transcript rendering, Markdown reuse, and the composer.</summary>
public sealed class DxChatTests : TestContext
{
    private static IReadOnlyList<ChatMessage> Convo() =>
    [
        new ChatMessage(ChatRole.User, "hi"),
        new ChatMessage(ChatRole.Assistant, "Hello **there**"),
    ];

    [Fact]
    public void Renders_a_bubble_per_message_with_role_classes()
    {
        IRenderedComponent<DxChat> chat = RenderComponent<DxChat>(parameters => parameters
            .Add(c => c.Messages, Convo()));

        Assert.Equal(2, chat.FindAll(".dx-chat-msg").Count);
        Assert.Single(chat.FindAll(".dx-chat-user"));
        Assert.Single(chat.FindAll(".dx-chat-assistant"));
    }

    [Fact]
    public void Assistant_content_renders_as_markdown_user_as_text()
    {
        IRenderedComponent<DxChat> chat = RenderComponent<DxChat>(parameters => parameters
            .Add(c => c.Messages, Convo()));

        // Assistant bubble: Markdown -> <strong>.
        Assert.Contains("<strong>there</strong>", chat.Find(".dx-chat-assistant .dx-chat-bubble").InnerHtml);
        // User bubble: plain text, no markup injection.
        Assert.Equal("hi", chat.Find(".dx-chat-user .dx-chat-bubble").TextContent);
    }

    [Fact]
    public void Markdown_can_be_disabled_for_assistant()
    {
        IRenderedComponent<DxChat> chat = RenderComponent<DxChat>(parameters => parameters
            .Add(c => c.Messages, Convo())
            .Add(c => c.RenderMarkdown, false));

        Assert.DoesNotContain("<strong>", chat.Find(".dx-chat-assistant .dx-chat-bubble").InnerHtml);
        Assert.Contains("Hello **there**", chat.Find(".dx-chat-assistant .dx-chat-bubble").TextContent);
    }

    [Fact]
    public void Typing_indicator_shows_only_when_busy()
    {
        IRenderedComponent<DxChat> chat = RenderComponent<DxChat>(parameters => parameters
            .Add(c => c.Messages, Convo()));
        Assert.Empty(chat.FindAll(".dx-chat-typing"));

        chat.SetParametersAndRender(parameters => parameters.Add(c => c.Busy, true));
        Assert.Single(chat.FindAll(".dx-chat-typing"));
    }

    [Fact]
    public void Send_button_is_disabled_until_text_is_entered()
    {
        IRenderedComponent<DxChat> chat = RenderComponent<DxChat>(parameters => parameters
            .Add(c => c.Messages, Convo()));

        Assert.True(chat.Find(".dx-chat-send").HasAttribute("disabled"));

        chat.Find(".dx-chat-input").Input("hello");
        Assert.False(chat.Find(".dx-chat-send").HasAttribute("disabled"));
    }

    [Fact]
    public void Sending_raises_on_send_and_clears_the_composer()
    {
        string? sent = null;
        IRenderedComponent<DxChat> chat = RenderComponent<DxChat>(parameters => parameters
            .Add(c => c.Messages, Convo())
            .Add(c => c.OnSend, t => sent = t));

        chat.Find(".dx-chat-input").Input("  draft text  ");
        chat.Find(".dx-chat-send").Click();

        Assert.Equal("draft text", sent);   // trimmed
        Assert.Equal(string.Empty, chat.Find(".dx-chat-input").GetAttribute("value"));
    }

    [Fact]
    public void Ctrl_enter_sends()
    {
        string? sent = null;
        IRenderedComponent<DxChat> chat = RenderComponent<DxChat>(parameters => parameters
            .Add(c => c.Messages, Convo())
            .Add(c => c.OnSend, t => sent = t));

        chat.Find(".dx-chat-input").Input("via keyboard");
        chat.Find(".dx-chat-input").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        Assert.Equal("via keyboard", sent);
    }

    [Fact]
    public void Busy_blocks_sending()
    {
        string? sent = null;
        IRenderedComponent<DxChat> chat = RenderComponent<DxChat>(parameters => parameters
            .Add(c => c.Messages, Convo())
            .Add(c => c.Busy, true)
            .Add(c => c.OnSend, t => sent = t));

        chat.Find(".dx-chat-input").Input("blocked");
        chat.Find(".dx-chat-input").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        Assert.Null(sent);
    }
}
