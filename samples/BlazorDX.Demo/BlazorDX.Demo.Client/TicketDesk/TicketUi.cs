namespace BlazorDX.Demo.Client.TicketDesk;

/// <summary>Maps ticket enums to badge CSS classes and human labels (see app.css td-* rules).</summary>
public static class TicketUi
{
    public static string StatusClass(TicketStatus s) => "td-badge td-st-" + s.ToString().ToLowerInvariant();

    public static string PriorityClass(TicketPriority p) => "td-badge td-pri-" + p.ToString().ToLowerInvariant();

    public static string Label(TicketStatus s) => s switch
    {
        TicketStatus.InProgress => "In progress",
        _ => s.ToString(),
    };

    public static readonly TicketStatus[] Statuses =
        { TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Blocked, TicketStatus.Resolved, TicketStatus.Closed };

    public static readonly TicketPriority[] Priorities =
        { TicketPriority.Urgent, TicketPriority.High, TicketPriority.Medium, TicketPriority.Low };
}
