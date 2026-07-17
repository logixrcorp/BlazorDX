namespace BlazorDX.Demo.Client.AiChat;

/// <summary>Who authored a message in an ephemeral AI chat thread.</summary>
public enum AiChatRole
{
    User,
    Assistant,
}

/// <summary>
/// One message in an <see cref="AiChatThread"/>. User messages are plain, ordinary Blazor state
/// (<see cref="Text"/> is rendered directly). Assistant messages are never rendered from
/// <see cref="PendingReplyText"/> directly — that field only exists to hand the canned reply to
/// the demo broker (<c>/demo/ai-chat/handshake</c>) at handshake time. The text a user actually
/// sees always comes back through the real ECDH + AES-GCM decrypt path via
/// <c>SecureEphemeralChat</c>, mounted into its own closed Shadow DOM node keyed by
/// <see cref="SessionId"/>.
/// </summary>
public sealed class AiChatMessage
{
    public int Id { get; set; }

    public AiChatRole Role { get; set; }

    /// <summary>Set only for <see cref="AiChatRole.User"/> messages — rendered directly.</summary>
    public string Text { get; set; } = string.Empty;

    public DateTime Sent { get; set; }

    /// <summary>
    /// Set only for <see cref="AiChatRole.Assistant"/> messages: the globally-unique, one-shot
    /// ephemeral session id this message's <c>SecureEphemeralChat</c> mounts under. Each
    /// assistant message gets its own fresh id — the underlying wasm session store's
    /// <c>end_session</c> call after every handshake means a session id can never safely be
    /// reused for a second message.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// The canned reply text the demo broker should encrypt for this message's handshake. Never
    /// rendered directly — see the type doc comment.
    /// </summary>
    public string PendingReplyText { get; set; } = string.Empty;

    /// <summary>
    /// True once a real WITHDRAW event (pushed through BlazorDX.Conduit) or a detected tamper
    /// has torn this message's mounted content down.
    /// </summary>
    public bool Withdrawn { get; set; }
}

/// <summary>One ephemeral AI chat thread in the sidebar list.</summary>
public sealed class AiChatThread
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Short flavor text shown in the thread list for a thread with no messages yet. Seeded
    /// threads carry no actual message history: a one-shot ephemeral session cannot be
    /// re-decrypted after the fact (see docs/adr/0016), so there is no honest way to "seed" a
    /// past ephemeral reply — only new, live ones.
    /// </summary>
    public string Preview { get; set; } = string.Empty;

    public DateTime LastActivity { get; set; }

    public string Status { get; set; } = "Open";

    public List<AiChatMessage> Messages { get; } = new();
}

/// <summary>A named, reusable prompt in the prompt library.</summary>
public sealed class PromptSnippet
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}
