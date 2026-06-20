using BlazorDX.Primitives.Forms;

namespace BlazorDX.Demo.Client;

/// <summary>Meeting priority levels.</summary>
public enum MeetingPriority
{
    Low,
    Normal,
    High,
}

/// <summary>
/// A form model that is also an AI tool. <c>BlazorDX.SourceGen</c> emits
/// <c>MeetingRequestFormModel</c> for it — the descriptor <c>DxForm</c> renders and
/// <c>FormTool</c> turns into a JSON-Schema tool definition.
/// </summary>
[DxFormModel(Name = "schedule_meeting", Description = "Schedule a meeting with a teammate.")]
public sealed class MeetingRequest
{
    [DxField("Title", Required = true, MaxLength = 80, Placeholder = "What's the meeting about?",
        Description = "Meeting title.")]
    public string Title { get; set; } = string.Empty;

    [DxField("Attendees", Min = 1, Max = 50, Description = "Number of attendees.")]
    public int Attendees { get; set; } = 1;

    [DxField("Organizer email", Required = true, Pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        Placeholder = "you@team.io", Description = "Organizer's email address.")]
    public string Email { get; set; } = string.Empty;

    [DxField("Remote", Description = "Whether the meeting is remote.")]
    public bool Remote { get; set; }

    [DxField("Priority", Description = "Priority level.")]
    public MeetingPriority Priority { get; set; }

    [DxField("Notes", Multiline = true, Placeholder = "Agenda, links, context…", Description = "Extra notes.")]
    public string? Notes { get; set; }
}
