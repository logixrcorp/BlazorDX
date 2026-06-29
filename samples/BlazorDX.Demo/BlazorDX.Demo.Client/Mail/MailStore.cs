namespace BlazorDX.Demo.Client.Mail;

/// <summary>A mailbox folder's display metadata.</summary>
/// <param name="Kind">The folder.</param>
/// <param name="Label">Display name.</param>
/// <param name="Icon">Emoji glyph for the rail.</param>
public sealed record MailFolderInfo(MailFolderKind Kind, string Label, string Icon);

/// <summary>
/// In-memory mailbox for the email-client demo: seeded messages across folders, plus read/star/move
/// and a compose→send path. Registered <b>Scoped</b> (never Singleton — the BlazorDX analyzer
/// enforces that) so each session gets its own isolated mailbox.
/// </summary>
public sealed class MailStore
{
    private readonly List<MailMessage> _messages = new();
    private int _nextId = 1;

    public MailStore() => Seed();

    public IReadOnlyList<MailFolderInfo> Folders { get; } =
    [
        new(MailFolderKind.Inbox, "Inbox", "📥"),
        new(MailFolderKind.Starred, "Starred", "★"),
        new(MailFolderKind.Sent, "Sent", "📤"),
        new(MailFolderKind.Drafts, "Drafts", "📝"),
        new(MailFolderKind.Archive, "Archive", "🗄"),
        new(MailFolderKind.Trash, "Trash", "🗑"),
    ];

    // ---- queries -----------------------------------------------------------

    /// <summary>Messages in a folder, newest first. "Starred" is the virtual flagged view.</summary>
    public IReadOnlyList<MailMessage> InFolder(MailFolderKind kind)
    {
        IEnumerable<MailMessage> q = kind == MailFolderKind.Starred
            ? _messages.Where(m => m.Starred && m.Folder != MailFolderKind.Trash)
            : _messages.Where(m => m.Folder == kind);
        return q.OrderByDescending(m => m.Received).ToList();
    }

    public MailMessage? Get(int id) => _messages.FirstOrDefault(m => m.Id == id);

    /// <summary>Unread count for the folder badge (0 is hidden by the UI).</summary>
    public int UnreadCount(MailFolderKind kind) => kind switch
    {
        MailFolderKind.Starred => _messages.Count(m => m.Starred && m.Unread && m.Folder != MailFolderKind.Trash),
        _ => _messages.Count(m => m.Folder == kind && m.Unread),
    };

    // ---- operations --------------------------------------------------------

    public void MarkRead(MailMessage m, bool read = true) => m.Unread = !read;

    public void ToggleStar(MailMessage m) => m.Starred = !m.Starred;

    public void Move(MailMessage m, MailFolderKind to) => m.Folder = to;

    /// <summary>Sends a composed message: it lands in Sent, newest.</summary>
    public MailMessage Send(string to, string subject, string bodyHtml)
    {
        MailMessage m = new()
        {
            Id = _nextId++,
            From = "You",
            FromEmail = "you@blazordx.com",
            To = string.IsNullOrWhiteSpace(to) ? "(no recipient)" : to.Trim(),
            Subject = string.IsNullOrWhiteSpace(subject) ? "(no subject)" : subject.Trim(),
            Preview = Snippet(bodyHtml),
            BodyHtml = bodyHtml,
            Received = DateTime.Now,
            Unread = false,
            Folder = MailFolderKind.Sent,
        };
        _messages.Add(m);
        return m;
    }

    // Strips tags for the list preview snippet (display-only; rendering still goes through the sanitizer).
    private static string Snippet(string html)
    {
        string text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text).Replace("\n", " ").Trim();
        while (text.Contains("  "))
        {
            text = text.Replace("  ", " ");
        }

        return text.Length > 120 ? text[..120] + "…" : text;
    }

    // ---- seed --------------------------------------------------------------

    private void Add(string from, string email, string subject, string body,
        int hoursAgo, MailFolderKind folder, bool unread, bool starred, params string[] labels)
    {
        MailMessage m = new()
        {
            Id = _nextId++,
            From = from,
            FromEmail = email,
            Subject = subject,
            Preview = Snippet(body),
            BodyHtml = body,
            Received = DateTime.Now.AddHours(-hoursAgo),
            Unread = unread,
            Starred = starred,
            Folder = folder,
        };
        m.Labels.AddRange(labels);
        _messages.Add(m);
    }

    private void Seed()
    {
        Add("Dana Ortiz", "dana@contoso.com", "Q3 planning notes",
            "<p>Hi,</p><p>Sharing the <strong>Q3 planning</strong> summary ahead of Thursday:</p>" +
            "<ul><li>Lock the roadmap by the 15th</li><li>Two new hires in platform</li><li>Budget review with finance</li></ul>" +
            "<p>Let me know if you want to add anything.</p><p>— Dana</p>",
            2, MailFolderKind.Inbox, unread: true, starred: true, "Work", "Planning");
        Add("GitHub", "noreply@github.com", "[BlazorDX] CI passed on main",
            "<p>The <strong>CI</strong> workflow succeeded for commit <code>4bf26e8</code>.</p>" +
            "<p><a href=\"https://github.com/logixrcorp/BlazorDX\">View the run</a></p>",
            5, MailFolderKind.Inbox, unread: true, starred: false, "Dev");
        Add("Priya Patel", "priya@contoso.com", "Re: Vendor MSA signature",
            "<p>The countersigned MSA is attached. Filing it under Contracts/Vendors.</p><p>Thanks!</p>",
            26, MailFolderKind.Inbox, unread: false, starred: false, "Legal");
        Add("Sam Reyes", "sam@contoso.com", "Security review scheduled",
            "<p>Booked the security review for next Tuesday at 10:00.</p><p>Agenda: incident response plan + retention schedule.</p>",
            49, MailFolderKind.Inbox, unread: false, starred: true, "Security");
        Add("Lee Chan", "lee@contoso.com", "Expense report reminder",
            "<p>Friendly reminder to submit March expenses by Friday.</p><p>Use the standard template.</p>",
            73, MailFolderKind.Inbox, unread: false, starred: false);
        Add("Jordan Kim", "jordan@contoso.com", "ADR draft for review",
            "<p>Drafted the ADR for the model-driven editor. Comments welcome.</p>",
            120, MailFolderKind.Archive, unread: false, starred: false, "Dev");
        Add("You", "you@blazordx.com", "Re: Q3 planning notes",
            "<p>Looks great — I'll add the hiring timeline before Thursday.</p>",
            1, MailFolderKind.Sent, unread: false, starred: false);
        Add("You", "you@blazordx.com", "Welcome packet (draft)",
            "<p>Draft welcome packet for new hires. Still needs the benefits section.</p>",
            30, MailFolderKind.Drafts, unread: false, starred: false);
    }
}
