namespace BlazorDX.Demo.Client.AiChat;

/// <summary>
/// In-memory data + canned-reply logic for the "/ai-chat" example app: a list of ephemeral AI
/// chat threads, a small reusable prompt library (CRUD), and the keyword-matched canned replies
/// used to drive the real ECDH/AES-GCM handshake against <c>/demo/ai-chat/handshake</c> — same
/// spirit as <c>Pages/Chat.razor</c>'s own <c>Reply()</c> method, no real LLM/API call anywhere.
/// Registered <b>Scoped</b> (never Singleton — the BlazorDX analyzer enforces that) so each
/// session gets its own isolated chat state.
/// </summary>
public sealed class AiChatStore
{
    private readonly List<AiChatThread> _threads = new();
    private readonly List<PromptSnippet> _prompts = new();
    private int _nextThreadId = 1;
    private int _nextMessageId = 1;
    private int _nextPromptId = 1;

    public AiChatStore()
    {
        SeedThreads();
        SeedPrompts();
    }

    public IReadOnlyList<AiChatThread> Threads => _threads.OrderByDescending(t => t.LastActivity).ToList();

    public IReadOnlyList<PromptSnippet> Prompts => _prompts;

    public AiChatThread? Get(int id) => _threads.FirstOrDefault(t => t.Id == id);

    // ---- messages ------------------------------------------------------

    public AiChatMessage AddUserMessage(AiChatThread thread, string text)
    {
        AiChatMessage m = new()
        {
            Id = _nextMessageId++,
            Role = AiChatRole.User,
            Text = text,
            Sent = DateTime.Now,
        };
        thread.Messages.Add(m);
        thread.LastActivity = m.Sent;
        return m;
    }

    /// <summary>
    /// Starts a fresh assistant message: a brand-new, globally-unique session id and the canned
    /// reply text the caller wants the demo broker to encrypt for it. The message carries no
    /// displayed text of its own — <c>SecureEphemeralChat</c> supplies that after a real
    /// handshake against <c>/demo/ai-chat/handshake</c>.
    /// </summary>
    public AiChatMessage AddAssistantMessage(AiChatThread thread, string replyText)
    {
        AiChatMessage m = new()
        {
            Id = _nextMessageId++,
            Role = AiChatRole.Assistant,
            SessionId = $"ai-chat-{thread.Id}-{Guid.NewGuid():N}",
            PendingReplyText = replyText,
            Sent = DateTime.Now,
        };
        thread.Messages.Add(m);
        thread.LastActivity = m.Sent;
        thread.Status = "Answered";
        return m;
    }

    public void MarkWithdrawn(AiChatMessage m) => m.Withdrawn = true;

    // ---- prompt library --------------------------------------------------

    public PromptSnippet AddPrompt(string name, string text)
    {
        PromptSnippet p = new() { Id = _nextPromptId++, Name = name, Text = text };
        _prompts.Add(p);
        return p;
    }

    public void UpdatePrompt(int id, string name, string text)
    {
        PromptSnippet? p = _prompts.FirstOrDefault(x => x.Id == id);
        if (p is null)
        {
            return;
        }

        p.Name = name;
        p.Text = text;
    }

    public void DeletePrompt(int id) => _prompts.RemoveAll(p => p.Id == id);

    // ---- canned replies -------------------------------------------------

    /// <summary>
    /// Keyword-matched canned reply — the only "AI" in this demo. Mirrors
    /// <c>Pages/Chat.razor</c>'s <c>Reply()</c>: no external model or API call, ever. The result
    /// is handed to the demo broker as ciphertext content, never rendered from C# directly.
    /// </summary>
    public static string GenerateReply(string prompt)
    {
        string p = prompt.ToLowerInvariant();

        if (p.Contains("custody") || p.Contains("handoff") || p.Contains("pickup"))
        {
            return "Here's a starting point: a shared handoff checklist (bag packed, school "
                + "folder, medication) synced to both households' calendars, with a reminder "
                + "30 minutes before each transition.";
        }

        if (p.Contains("budget") || p.Contains("expense") || p.Contains("split"))
        {
            return "For a shared expense, log it once and send a split request to the other "
                + "household — it stays a record of who owes what, and never moves money on "
                + "its own.";
        }

        if (p.Contains("chore") || p.Contains("task"))
        {
            return "Try a rotating chore board with no streaks or penalties — just a calm, "
                + "visible queue both households can see and claim from.";
        }

        if (p.Contains("meal") || p.Contains("recipe") || p.Contains("grocery"))
        {
            return "Based on what's already in the pantry, a quick grocery list and three "
                + "meal ideas for the week would round out what's low or expiring soon.";
        }

        if (p.Contains("crypto") || p.Contains("secur") || p.Contains("encrypt"))
        {
            return "This exact reply reached you through a real P-256 ECDH handshake and "
                + "AES-256-GCM decrypt, mounted into a closed Shadow DOM node — the plaintext "
                + "never touched Blazor's render tree.";
        }

        return $"Here's a starting point for \"{prompt.Trim()}\" — try asking about `custody`, "
            + "`budget`, `chores`, `meals`, or `security` for a more specific reply.";
    }

    // ---- seed --------------------------------------------------------------

    private void SeedThreads()
    {
        _threads.Add(new AiChatThread
        {
            Id = _nextThreadId++,
            Title = "Weekend handoff logistics",
            Preview = "Coordinating Friday pickup between two households.",
            LastActivity = DateTime.Now.AddHours(-3),
            Status = "Answered",
        });
        _threads.Add(new AiChatThread
        {
            Id = _nextThreadId++,
            Title = "Shared school-supply budget",
            Preview = "Splitting the back-to-school list two ways.",
            LastActivity = DateTime.Now.AddDays(-1),
            Status = "Answered",
        });
        _threads.Add(new AiChatThread
        {
            Id = _nextThreadId++,
            Title = "This week's chore rotation",
            Preview = "New chat — ask about chores, meals, budget, or custody logistics.",
            LastActivity = DateTime.Now.AddDays(-2),
            Status = "Open",
        });
    }

    private void SeedPrompts()
    {
        _prompts.Add(new PromptSnippet
        {
            Id = _nextPromptId++,
            Name = "Handoff checklist",
            Text = "Suggest a custody handoff checklist for Friday pickups.",
        });
        _prompts.Add(new PromptSnippet
        {
            Id = _nextPromptId++,
            Name = "Split an expense",
            Text = "How should we split this month's shared school expenses?",
        });
        _prompts.Add(new PromptSnippet
        {
            Id = _nextPromptId++,
            Name = "Plan chores",
            Text = "Suggest a calm chore rotation for two households.",
        });
    }
}
