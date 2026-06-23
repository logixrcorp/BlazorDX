namespace BlazorDX.Demo.Client.TicketDesk;

/// <summary>Maps ITIL enums to display labels and badge CSS classes (see app.css td-* rules).</summary>
public static class ItilUi
{
    public static string TypeLabel(RecordType t) => t switch
    {
        RecordType.ServiceRequest => "Service Request",
        _ => t.ToString(),
    };

    public static string TypePrefix(RecordType t) => t switch
    {
        RecordType.Incident => "INC",
        RecordType.ServiceRequest => "REQ",
        RecordType.Problem => "PRB",
        RecordType.Change => "CHG",
        _ => "REC",
    };

    public static string StateLabel(ServiceState s) => s switch
    {
        ServiceState.InProgress => "In Progress",
        ServiceState.OnHold => "On Hold",
        _ => s.ToString(),
    };

    public static string PriorityLabel(Priority p) => p switch
    {
        Priority.Critical => "P1 Critical",
        Priority.High => "P2 High",
        Priority.Moderate => "P3 Moderate",
        Priority.Low => "P4 Low",
        Priority.Planning => "P5 Planning",
        _ => p.ToString(),
    };

    public static string PriorityClass(Priority p) => "td-badge td-pri-" + p.ToString().ToLowerInvariant();

    public static string TypeClass(RecordType t) => "td-badge td-type-" + t.ToString().ToLowerInvariant();

    public static string StateClass(ServiceState s) => "td-badge td-state-" + StateGroup(s);

    public static string SlaClass(Ticket t) =>
        "td-badge " + (t.IsResolved ? "td-sla-met" : t.SlaBreached ? "td-sla-breach" : "td-sla-ok");

    public static string SlaLabel(Ticket t) => t.IsResolved ? "Met" : t.SlaBreached ? "Breached" : "On track";

    public static string CiHealthClass(CiHealth h) => "td-badge td-ci-" + h.ToString().ToLowerInvariant();

    private static string StateGroup(ServiceState s) => s switch
    {
        ServiceState.New => "new",
        ServiceState.Assigned or ServiceState.InProgress or ServiceState.Assess
            or ServiceState.Authorize or ServiceState.Scheduled or ServiceState.Implement => "active",
        ServiceState.OnHold => "hold",
        ServiceState.Resolved or ServiceState.Review => "resolved",
        ServiceState.Closed => "closed",
        ServiceState.Cancelled => "cancelled",
        _ => "new",
    };

    public static readonly RecordType[] Types =
        { RecordType.Incident, RecordType.ServiceRequest, RecordType.Problem, RecordType.Change };

    public static readonly Priority[] Priorities =
        { Priority.Critical, Priority.High, Priority.Moderate, Priority.Low, Priority.Planning };
}
