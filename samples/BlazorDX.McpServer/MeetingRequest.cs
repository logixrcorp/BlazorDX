using BlazorDX.Primitives.Forms;

namespace BlazorDX.McpServer;

/// <summary>
/// One annotated model — the same kind <c>DxForm</c> renders for a human — becomes the AI
/// tool this server exposes. The <c>BlazorDX.SourceGen</c> generator emits
/// <c>MeetingRequestFormModel</c> (its <see cref="IFormModel{TModel}"/>) at build time.
/// </summary>
[DxFormModel(Name = "schedule_meeting", Description = "Schedule a meeting with a teammate.")]
public sealed class MeetingRequest
{
    [DxField("Title", Required = true, Description = "Meeting title.")]
    public string Title { get; set; } = string.Empty;

    [DxField("Attendees", Min = 1, Max = 50, Description = "Number of attendees.")]
    public int Attendees { get; set; } = 1;
}
