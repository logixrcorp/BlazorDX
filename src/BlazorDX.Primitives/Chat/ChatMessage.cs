namespace BlazorDX.Primitives.Chat;

/// <summary>Who authored a chat message.</summary>
public enum ChatRole
{
    /// <summary>The end user.</summary>
    User,

    /// <summary>The AI assistant.</summary>
    Assistant,

    /// <summary>A system/context note.</summary>
    System,
}

/// <summary>One turn in a chat transcript.</summary>
/// <param name="Role">Who sent the message.</param>
/// <param name="Content">Message text (Markdown for assistant turns).</param>
public readonly record struct ChatMessage(ChatRole Role, string Content);
