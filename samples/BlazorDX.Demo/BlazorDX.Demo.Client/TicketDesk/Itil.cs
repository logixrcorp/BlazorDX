namespace BlazorDX.Demo.Client.TicketDesk;

/// <summary>The ITIL 4 practice a record belongs to. Drives its number prefix and workflow.</summary>
public enum RecordType { Incident, ServiceRequest, Problem, Change }

/// <summary>Business impact — how widely the issue is felt (1 = High).</summary>
public enum Impact { High = 1, Medium = 2, Low = 3 }

/// <summary>Urgency — how quickly it must be resolved (1 = High).</summary>
public enum Urgency { High = 1, Medium = 2, Low = 3 }

/// <summary>ITIL priority, derived from Impact × Urgency (P1 Critical … P5 Planning).</summary>
public enum Priority { Critical = 1, High = 2, Moderate = 3, Low = 4, Planning = 5 }

/// <summary>
/// Unified workflow state. Incidents/Requests/Problems use the New…Closed flow; Changes use the
/// Assess…Review flow. Allowed transitions per type live in <see cref="Workflow"/>.
/// </summary>
public enum ServiceState
{
    New, Assigned, InProgress, OnHold, Resolved, Closed, Cancelled,
    Assess, Authorize, Scheduled, Implement, Review,
}

public enum ChangeType { Standard, Normal, Emergency }

public enum ChangeRisk { Low, Medium, High }

public enum ApprovalState { NotRequested, Requested, Approved, Rejected }

public enum CiType { Application, Server, Database, NetworkDevice, BusinessService, Workstation }

public enum CiHealth { Operational, Degraded, Down, Maintenance }

/// <summary>The ITIL Impact × Urgency → Priority matrix (the standard 3×3 service-desk grid).</summary>
public static class PriorityMatrix
{
    public static Priority Compute(Impact impact, Urgency urgency) => (impact, urgency) switch
    {
        (Impact.High, Urgency.High) => Priority.Critical,
        (Impact.High, Urgency.Medium) => Priority.High,
        (Impact.Medium, Urgency.High) => Priority.High,
        (Impact.High, Urgency.Low) => Priority.Moderate,
        (Impact.Medium, Urgency.Medium) => Priority.Moderate,
        (Impact.Low, Urgency.High) => Priority.Moderate,
        (Impact.Medium, Urgency.Low) => Priority.Low,
        (Impact.Low, Urgency.Medium) => Priority.Low,
        (Impact.Low, Urgency.Low) => Priority.Planning,
        _ => Priority.Moderate,
    };
}

/// <summary>Response and resolution targets (in hours) per priority — a simplified SLA policy.</summary>
public readonly record struct SlaTarget(double ResponseHours, double ResolveHours);

public static class Sla
{
    public static SlaTarget For(Priority p) => p switch
    {
        Priority.Critical => new SlaTarget(0.25, 4),
        Priority.High => new SlaTarget(1, 8),
        Priority.Moderate => new SlaTarget(4, 24),
        Priority.Low => new SlaTarget(8, 72),
        Priority.Planning => new SlaTarget(24, 120),
        _ => new SlaTarget(4, 24),
    };
}

/// <summary>Allowed next states per record type — the ITIL lifecycle, enforced in the UI.</summary>
public static class Workflow
{
    private static readonly ServiceState[] ServiceFlow =
        { ServiceState.New, ServiceState.Assigned, ServiceState.InProgress, ServiceState.OnHold, ServiceState.Resolved, ServiceState.Closed, ServiceState.Cancelled };

    private static readonly ServiceState[] ChangeFlow =
        { ServiceState.New, ServiceState.Assess, ServiceState.Authorize, ServiceState.Scheduled, ServiceState.Implement, ServiceState.Review, ServiceState.Closed, ServiceState.Cancelled };

    public static IReadOnlyList<ServiceState> StatesFor(RecordType type) =>
        type == RecordType.Change ? ChangeFlow : ServiceFlow;

    public static bool IsTerminal(ServiceState s) => s is ServiceState.Closed or ServiceState.Cancelled;

    public static bool IsResolvedOrBeyond(ServiceState s) =>
        s is ServiceState.Resolved or ServiceState.Closed or ServiceState.Review or ServiceState.Cancelled;
}

/// <summary>Standard ITIL assignment groups (functional escalation targets).</summary>
public static class Groups
{
    public static readonly string[] All =
        { "Service Desk", "Network", "Database", "Application Support", "Field Services", "Change Management", "Security" };
}
