using BlazorDX.Primitives.Grid;

namespace BlazorDX.Demo.Client.TicketDesk;

public enum TicketStatus { Open, InProgress, Blocked, Resolved, Closed }

public enum TicketPriority { Low, Medium, High, Urgent }

public enum TicketCategory { Bug, Feature, Question, Incident, Task }

/// <summary>A single comment on a ticket's activity thread.</summary>
public sealed class TicketComment
{
    public required string Author { get; init; }
    public required string Body { get; init; }
    public required DateTime At { get; init; }
}

/// <summary>
/// The rich ticket aggregate held by <see cref="TicketStore"/>. The grid binds to the
/// flat <see cref="TicketRow"/> projection instead, so this can carry comments and dates
/// the grid doesn't show.
/// </summary>
public sealed class Ticket
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TicketStatus Status { get; set; }
    public TicketPriority Priority { get; set; }
    public TicketCategory Category { get; set; }
    public string Requester { get; set; } = string.Empty;
    public string Assignee { get; set; } = "Unassigned";
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public List<TicketComment> Comments { get; } = new();

    public TicketRow ToRow() => new()
    {
        Id = Id,
        Title = Title,
        Status = Status.ToString(),
        Priority = Priority.ToString(),
        Category = Category.ToString(),
        Assignee = Assignee,
        Updated = Updated.ToString("MMM d, HH:mm"),
    };
}

/// <summary>
/// Flat, all-string/int projection of a <see cref="Ticket"/> for the DataGrid. Mirrors the
/// PersonRow pattern: <c>[GridRow]</c> + <c>[GridColumn]</c> drive BlazorDX.SourceGen, which
/// emits <c>TicketRowGridAccessor</c> at build time — zero reflection in the grid.
/// </summary>
[GridRow]
public sealed class TicketRow
{
    [GridColumn("ID", Order = 0)] public int Id { get; set; }
    [GridColumn("Title", Order = 1)] public string Title { get; set; } = string.Empty;
    [GridColumn("Status", Order = 2)] public string Status { get; set; } = string.Empty;
    [GridColumn("Priority", Order = 3)] public string Priority { get; set; } = string.Empty;
    [GridColumn("Category", Order = 4)] public string Category { get; set; } = string.Empty;
    [GridColumn("Assignee", Order = 5)] public string Assignee { get; set; } = string.Empty;
    [GridColumn("Updated", Order = 6)] public string Updated { get; set; } = string.Empty;
}
