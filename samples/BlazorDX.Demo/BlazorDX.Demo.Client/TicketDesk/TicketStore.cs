namespace BlazorDX.Demo.Client.TicketDesk;

/// <summary>
/// In-memory ticket data for the TicketDesk demo. Registered <b>Scoped</b> (never Singleton —
/// per the BlazorDX state-isolation rule the analyzer enforces), so each user/circuit gets its
/// own copy and edits never leak across sessions.
/// </summary>
public sealed class TicketStore
{
    private readonly List<Ticket> _tickets = new();
    private int _nextId = 1;

    public TicketStore() => Seed();

    public IReadOnlyList<Ticket> All() => _tickets;

    public Ticket? Get(int id) => _tickets.FirstOrDefault(t => t.Id == id);

    public IReadOnlyList<TicketRow> Rows() =>
        _tickets.OrderByDescending(t => t.Priority).ThenBy(t => t.Id).Select(t => t.ToRow()).ToList();

    public Ticket Add(NewTicket form)
    {
        Ticket ticket = new()
        {
            Id = _nextId++,
            Title = form.Title.Trim(),
            Description = form.Description.Trim(),
            Priority = form.Priority,
            Category = form.Category,
            Requester = string.IsNullOrWhiteSpace(form.Requester) ? "Anonymous" : form.Requester.Trim(),
            Assignee = string.IsNullOrWhiteSpace(form.Assignee) ? "Unassigned" : form.Assignee.Trim(),
            Status = TicketStatus.Open,
            Created = DateTime.Now,
            Updated = DateTime.Now,
        };
        _tickets.Insert(0, ticket);
        return ticket;
    }

    public void SetStatus(int id, TicketStatus status)
    {
        if (Get(id) is { } t && t.Status != status)
        {
            t.Status = status;
            t.Updated = DateTime.Now;
        }
    }

    public void AddComment(int id, string author, string body)
    {
        if (Get(id) is { } t && !string.IsNullOrWhiteSpace(body))
        {
            t.Comments.Add(new TicketComment { Author = string.IsNullOrWhiteSpace(author) ? "You" : author.Trim(), Body = body.Trim(), At = DateTime.Now });
            t.Updated = DateTime.Now;
        }
    }

    public int Count(TicketStatus status) => _tickets.Count(t => t.Status == status);

    public int Count(TicketPriority priority) => _tickets.Count(t => t.Priority == priority);

    public bool IsOpen(Ticket t) => t.Status is not (TicketStatus.Resolved or TicketStatus.Closed);

    public int OpenCount => _tickets.Count(IsOpen);

    /// <summary>Share of all tickets that are resolved or closed, 0–100.</summary>
    public double ResolutionRate =>
        _tickets.Count == 0 ? 0 : Math.Round(100.0 * _tickets.Count(t => !IsOpen(t)) / _tickets.Count);

    private void Seed()
    {
        // (title, status, priority, category, requester, assignee, ageDays)
        (string Title, TicketStatus S, TicketPriority P, TicketCategory C, string By, string To, int Age)[] seed =
        {
            ("Login button does nothing on Safari 17", TicketStatus.Open, TicketPriority.Urgent, TicketCategory.Bug, "Dana Reed", "Sam Cho", 0),
            ("Export to Excel drops the last column", TicketStatus.InProgress, TicketPriority.High, TicketCategory.Bug, "Priya Nair", "Sam Cho", 1),
            ("Add dark-mode toggle to the dashboard", TicketStatus.Open, TicketPriority.Medium, TicketCategory.Feature, "Marco Diaz", "Unassigned", 2),
            ("Webhook retries hammer our endpoint", TicketStatus.Blocked, TicketPriority.High, TicketCategory.Incident, "Ops Bot", "Lena Park", 1),
            ("How do I rotate an API key?", TicketStatus.Resolved, TicketPriority.Low, TicketCategory.Question, "Tomás Lee", "Ana Vidal", 3),
            ("Grid scroll jitters at 100k rows on Firefox", TicketStatus.Open, TicketPriority.Medium, TicketCategory.Bug, "Jess Wu", "Sam Cho", 0),
            ("SSO login loops for Okta users", TicketStatus.InProgress, TicketPriority.Urgent, TicketCategory.Incident, "Will Frey", "Lena Park", 0),
            ("Add CSV import for contacts", TicketStatus.Open, TicketPriority.Medium, TicketCategory.Feature, "Nina Roth", "Unassigned", 4),
            ("Typo on the pricing page", TicketStatus.Closed, TicketPriority.Low, TicketCategory.Task, "Guest", "Ana Vidal", 6),
            ("Charts render blank on iPad", TicketStatus.Open, TicketPriority.High, TicketCategory.Bug, "Kai Brandt", "Sam Cho", 1),
            ("Bulk-assign tickets to a teammate", TicketStatus.Open, TicketPriority.Low, TicketCategory.Feature, "Marco Diaz", "Unassigned", 5),
            ("Password reset email never arrives", TicketStatus.Blocked, TicketPriority.Urgent, TicketCategory.Incident, "Dana Reed", "Lena Park", 0),
            ("Add keyboard shortcuts cheat-sheet", TicketStatus.Resolved, TicketPriority.Low, TicketCategory.Feature, "Jess Wu", "Ana Vidal", 7),
            ("Timezone shown wrong in activity log", TicketStatus.InProgress, TicketPriority.Medium, TicketCategory.Bug, "Tomás Lee", "Sam Cho", 2),
            ("Can we get a Slack integration?", TicketStatus.Open, TicketPriority.Medium, TicketCategory.Question, "Nina Roth", "Unassigned", 3),
            ("PDF export cuts off wide tables", TicketStatus.Open, TicketPriority.High, TicketCategory.Bug, "Priya Nair", "Sam Cho", 1),
            ("Search ignores accented characters", TicketStatus.Open, TicketPriority.Medium, TicketCategory.Bug, "Sofía Marín", "Unassigned", 2),
            ("Onboarding tour gets stuck on step 3", TicketStatus.InProgress, TicketPriority.High, TicketCategory.Bug, "Will Frey", "Lena Park", 0),
            ("Add 2FA via authenticator app", TicketStatus.Open, TicketPriority.High, TicketCategory.Feature, "Kai Brandt", "Unassigned", 4),
            ("Invoice PDF shows $0.00 total", TicketStatus.Blocked, TicketPriority.Urgent, TicketCategory.Incident, "Acct Team", "Sam Cho", 0),
            ("Mobile nav overlaps the content", TicketStatus.Resolved, TicketPriority.Medium, TicketCategory.Bug, "Jess Wu", "Ana Vidal", 1),
            ("Allow custom statuses on the board", TicketStatus.Open, TicketPriority.Low, TicketCategory.Feature, "Marco Diaz", "Unassigned", 8),
            ("Rate limit error on report endpoint", TicketStatus.Open, TicketPriority.Medium, TicketCategory.Incident, "Ops Bot", "Lena Park", 1),
            ("Document the MCP tool surface", TicketStatus.InProgress, TicketPriority.Low, TicketCategory.Task, "Sam Cho", "Sam Cho", 2),
            ("Drag-drop loses focus with VoiceOver", TicketStatus.Open, TicketPriority.High, TicketCategory.Bug, "A11y Audit", "Lena Park", 0),
            ("Add weekly digest email", TicketStatus.Closed, TicketPriority.Low, TicketCategory.Feature, "Nina Roth", "Ana Vidal", 9),
            ("Filter menu won't close on Esc", TicketStatus.Resolved, TicketPriority.Medium, TicketCategory.Bug, "Tomás Lee", "Sam Cho", 2),
            ("Pricing tiers unclear for nonprofits", TicketStatus.Open, TicketPriority.Low, TicketCategory.Question, "Guest", "Unassigned", 5),
        };

        DateTime now = DateTime.Now;
        foreach (var s in seed)
        {
            Ticket t = new()
            {
                Id = _nextId++,
                Title = s.Title,
                Status = s.S,
                Priority = s.P,
                Category = s.C,
                Requester = s.By,
                Assignee = s.To,
                Description = $"Reported by {s.By}. {s.C} ticket — see the activity thread for details and current status.",
                Created = now.AddDays(-s.Age).AddHours(-2),
                Updated = now.AddHours(-s.Age * 5),
            };
            if (s.S is TicketStatus.Resolved or TicketStatus.Closed)
            {
                t.Comments.Add(new TicketComment { Author = s.To, Body = "Fixed and verified. Closing this out — reopen if it recurs.", At = t.Updated });
            }
            else if (s.P is TicketPriority.Urgent)
            {
                t.Comments.Add(new TicketComment { Author = s.To, Body = "Escalated — investigating now.", At = t.Updated });
            }

            _tickets.Add(t);
        }
    }
}
