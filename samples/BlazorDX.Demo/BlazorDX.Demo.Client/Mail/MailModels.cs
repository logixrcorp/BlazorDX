using System.Globalization;

namespace BlazorDX.Demo.Client.Mail;

/// <summary>A mailbox folder. Starred is a virtual view (flagged messages), the rest are real folders.</summary>
public enum MailFolderKind
{
    Inbox,
    Starred,
    Sent,
    Drafts,
    Archive,
    Trash,
}

/// <summary>
/// One email message. The body is stored as a small, allow-listed HTML fragment and is always
/// rendered through the app's injected sanitizer before display, so an untrusted message can never
/// inject script — the same security model the Rich Text demo uses.
/// </summary>
public sealed class MailMessage
{
    public int Id { get; set; }

    public string From { get; set; } = string.Empty;

    public string FromEmail { get; set; } = string.Empty;

    public string To { get; set; } = "you@blazordx.com";

    public string Subject { get; set; } = string.Empty;

    /// <summary>One-line snippet shown in the list.</summary>
    public string Preview { get; set; } = string.Empty;

    /// <summary>Body as a small HTML fragment (sanitized on render).</summary>
    public string BodyHtml { get; set; } = string.Empty;

    public DateTime Received { get; set; }

    public bool Unread { get; set; }

    public bool Starred { get; set; }

    public MailFolderKind Folder { get; set; } = MailFolderKind.Inbox;

    public List<string> Labels { get; } = new();

    public List<string> Attachments { get; } = new();

    /// <summary>Up-to-two-letter avatar initials from the sender's display name.</summary>
    public string Initials
    {
        get
        {
            string[] parts = From.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "?";
            }

            char a = char.ToUpperInvariant(parts[0][0]);
            char b = parts.Length > 1 ? char.ToUpperInvariant(parts[^1][0]) : ' ';
            return b == ' ' ? a.ToString() : $"{a}{b}";
        }
    }

    /// <summary>Compact timestamp: time for today, "MMM d" otherwise.</summary>
    public string TimeLabel => Received.Date == DateTime.Today
        ? Received.ToString("HH:mm", CultureInfo.InvariantCulture)
        : Received.ToString("MMM d", CultureInfo.InvariantCulture);
}
