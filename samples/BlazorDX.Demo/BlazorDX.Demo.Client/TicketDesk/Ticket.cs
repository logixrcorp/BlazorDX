using BlazorDX.Primitives.Grid;

namespace BlazorDX.Demo.Client.TicketDesk;

/// <summary>An entry on a record's activity stream (a customer comment or an internal work note).</summary>
public sealed class WorkNote
{
    public required string Author { get; init; }
    public required string Body { get; init; }
    public required DateTime At { get; init; }
    public bool Internal { get; init; }
}

/// <summary>A CMDB Configuration Item — the asset/service a record relates to.</summary>
public sealed class ConfigurationItem
{
    public required string Name { get; init; }
    public CiType Type { get; init; }
    public CiHealth Health { get; set; }
    public string Owner { get; init; } = "Unassigned";
    public string Environment { get; init; } = "Production";
}

/// <summary>A knowledge-base article (workarounds, how-tos, known errors promoted from problems).</summary>
public sealed class KnowledgeArticle
{
    public required string Number { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public string Category { get; init; } = "General";
    public string? RelatedCi { get; init; }
}

/// <summary>
/// An ITIL service-management record (the "ticket"). One aggregate covers Incidents, Service
/// Requests, Problems, and Changes — its <see cref="Type"/> selects the workflow, number prefix,
/// and which extra fields apply. Priority and SLA are <b>derived</b>, never hand-picked.
/// </summary>
public sealed class Ticket
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public RecordType Type { get; set; }
    public string ShortDescription { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ServiceState State { get; set; } = ServiceState.New;
    public Impact Impact { get; set; } = Impact.Medium;
    public Urgency Urgency { get; set; } = Urgency.Medium;

    public string AssignmentGroup { get; set; } = "Service Desk";
    public string AssignedTo { get; set; } = "Unassigned";
    public string Requester { get; set; } = string.Empty;
    public string? ConfigItem { get; set; }
    public string Category { get; set; } = "Inquiry";

    public DateTime Opened { get; set; }
    public DateTime Updated { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? Resolution { get; set; }

    // Change-specific
    public ChangeType? ChangeType { get; set; }
    public ChangeRisk? Risk { get; set; }
    public ApprovalState Approval { get; set; } = ApprovalState.NotRequested;
    public DateTime? PlannedStart { get; set; }

    // Problem-specific
    public bool KnownError { get; set; }
    public string? Workaround { get; set; }

    // Relationships
    public string? RelatedProblem { get; set; }
    public List<string> RelatedIncidents { get; } = new();

    public List<WorkNote> Notes { get; } = new();

    /// <summary>ITIL priority, derived from the Impact × Urgency matrix.</summary>
    public Priority Priority => PriorityMatrix.Compute(Impact, Urgency);

    public SlaTarget Sla => TicketDesk.Sla.For(Priority);

    public DateTime ResolveDueAt => Opened.AddHours(Sla.ResolveHours);

    public bool IsResolved => Workflow.IsResolvedOrBeyond(State);

    /// <summary>True when the resolution SLA has elapsed and the record is still unresolved.</summary>
    public bool SlaBreached => !IsResolved && DateTime.Now > ResolveDueAt;

    /// <summary>0–100% of the resolution SLA window consumed (clamped).</summary>
    public double SlaConsumedPercent
    {
        get
        {
            if (IsResolved)
            {
                return 100;
            }

            double total = (ResolveDueAt - Opened).TotalMinutes;
            double used = (DateTime.Now - Opened).TotalMinutes;
            return total <= 0 ? 100 : Math.Clamp(used / total * 100, 0, 100);
        }
    }

    public TicketRow ToRow() => new()
    {
        Id = Id,
        Number = Number,
        Type = ItilUi.TypeLabel(Type),
        Priority = ItilUi.PriorityLabel(Priority),
        State = ItilUi.StateLabel(State),
        Short = ShortDescription,
        Group = AssignmentGroup,
        AssignedTo = AssignedTo,
        Ci = ConfigItem ?? "—",
        Sla = IsResolved ? "Met" : SlaBreached ? "Breached" : "On track",
        Updated = Updated.ToString("MMM d, HH:mm"),
    };
}

/// <summary>Flat projection for the DataGrid (all string/int — drives BlazorDX.SourceGen).</summary>
[GridRow]
public sealed class TicketRow
{
    [GridColumn("Number", Order = 0)] public string Number { get; set; } = string.Empty;
    [GridColumn("Type", Order = 1)] public string Type { get; set; } = string.Empty;
    [GridColumn("Priority", Order = 2)] public string Priority { get; set; } = string.Empty;
    [GridColumn("State", Order = 3)] public string State { get; set; } = string.Empty;
    [GridColumn("Short description", Order = 4)] public string Short { get; set; } = string.Empty;
    [GridColumn("Assignment group", Order = 5)] public string Group { get; set; } = string.Empty;
    [GridColumn("Assigned to", Order = 6)] public string AssignedTo { get; set; } = string.Empty;
    [GridColumn("CI", Order = 7)] public string Ci { get; set; } = string.Empty;
    [GridColumn("SLA", Order = 8)] public string Sla { get; set; } = string.Empty;
    [GridColumn("Updated", Order = 9)] public string Updated { get; set; } = string.Empty;
    public int Id { get; set; }
}
