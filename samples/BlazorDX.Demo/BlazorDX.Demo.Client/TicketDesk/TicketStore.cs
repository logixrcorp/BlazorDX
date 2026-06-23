namespace BlazorDX.Demo.Client.TicketDesk;

/// <summary>
/// In-memory ITIL service-management data: records (incidents, requests, problems, changes), the
/// CMDB, and the knowledge base. Registered <b>Scoped</b> (never Singleton — the BlazorDX
/// analyzer enforces that), so each session gets its own isolated copy.
/// </summary>
public sealed class TicketStore
{
    private readonly List<Ticket> _records = new();
    private readonly List<ConfigurationItem> _cis = new();
    private readonly List<KnowledgeArticle> _kb = new();
    private readonly Dictionary<RecordType, int> _counters = new()
    {
        [RecordType.Incident] = 1000,
        [RecordType.ServiceRequest] = 1000,
        [RecordType.Problem] = 1000,
        [RecordType.Change] = 1000,
    };

    private int _nextId = 1;

    public TicketStore()
    {
        SeedCis();
        SeedKnowledge();
        SeedRecords();
    }

    // ---- queries -----------------------------------------------------------

    public IReadOnlyList<Ticket> All() => _records;

    public Ticket? Get(int id) => _records.FirstOrDefault(r => r.Id == id);

    public Ticket? ByNumber(string number) => _records.FirstOrDefault(r => r.Number == number);

    public IReadOnlyList<Ticket> OfType(RecordType type) =>
        _records.Where(r => r.Type == type).ToList();

    public IReadOnlyList<TicketRow> Rows(RecordType? type = null) =>
        _records.Where(r => type is null || r.Type == type)
                .OrderBy(r => r.IsResolved).ThenBy(r => r.Priority).ThenByDescending(r => r.Updated)
                .Select(r => r.ToRow()).ToList();

    public IReadOnlyList<ConfigurationItem> Cis => _cis;

    public ConfigurationItem? Ci(string name) => _cis.FirstOrDefault(c => c.Name == name);

    public IReadOnlyList<Ticket> RecordsForCi(string ciName) =>
        _records.Where(r => r.ConfigItem == ciName).OrderBy(r => r.IsResolved).ThenBy(r => r.Priority).ToList();

    public IReadOnlyList<KnowledgeArticle> Articles => _kb;

    // ---- stats -------------------------------------------------------------

    public bool IsOpen(Ticket t) => !Workflow.IsTerminal(t.State);

    public int OpenCount => _records.Count(IsOpen);

    public int Count(RecordType type) => _records.Count(r => r.Type == type);

    public int Count(Priority p) => _records.Count(r => r.Priority == p && IsOpen(r));

    public int OpenOfType(RecordType type) => _records.Count(r => r.Type == type && IsOpen(r));

    public int SlaBreaches => _records.Count(r => r.SlaBreached);

    public int AtRisk => _records.Count(r => !r.IsResolved && !r.SlaBreached && r.SlaConsumedPercent >= 70);

    /// <summary>Share of open records currently within their resolution SLA, 0–100.</summary>
    public double SlaCompliance
    {
        get
        {
            var open = _records.Where(r => !r.IsResolved).ToList();
            return open.Count == 0 ? 100 : Math.Round(100.0 * open.Count(r => !r.SlaBreached) / open.Count);
        }
    }

    public IReadOnlyList<Ticket> ChangesInState(ServiceState state) =>
        _records.Where(r => r.Type == RecordType.Change && r.State == state)
                .OrderByDescending(r => r.Priority == Priority.Critical).ThenBy(r => r.PlannedStart).ToList();

    // ---- commands ----------------------------------------------------------

    public Ticket Add(NewTicket form)
    {
        Ticket t = new()
        {
            Id = _nextId++,
            Type = form.Type,
            Number = NextNumber(form.Type),
            ShortDescription = form.ShortDescription.Trim(),
            Description = form.Description.Trim(),
            Impact = form.Impact,
            Urgency = form.Urgency,
            Requester = string.IsNullOrWhiteSpace(form.Requester) ? "Anonymous" : form.Requester.Trim(),
            ConfigItem = string.IsNullOrWhiteSpace(form.ConfigItem) ? null : form.ConfigItem.Trim(),
            AssignmentGroup = GroupFor(form.Type),
            Category = form.Type == RecordType.ServiceRequest ? "Request" : "Inquiry",
            State = ServiceState.New,
            Opened = DateTime.Now,
            Updated = DateTime.Now,
        };

        if (form.Type == RecordType.Change)
        {
            t.ChangeType = TicketDesk.ChangeType.Normal;
            t.Risk = ChangeRisk.Medium;
            t.Approval = ApprovalState.NotRequested;
        }

        _records.Insert(0, t);
        return t;
    }

    public void Transition(int id, ServiceState state)
    {
        if (Get(id) is { } t && t.State != state)
        {
            t.State = state;
            t.Updated = DateTime.Now;
            t.ResolvedAt = Workflow.IsResolvedOrBeyond(state) ? (t.ResolvedAt ?? DateTime.Now) : null;
        }
    }

    public void Approve(int id, bool approved)
    {
        if (Get(id) is { Type: RecordType.Change } t)
        {
            t.Approval = approved ? ApprovalState.Approved : ApprovalState.Rejected;
            t.State = approved ? ServiceState.Scheduled : ServiceState.Cancelled;
            t.Updated = DateTime.Now;
        }
    }

    public void AddNote(int id, string author, string body, bool internalNote)
    {
        if (Get(id) is { } t && !string.IsNullOrWhiteSpace(body))
        {
            t.Notes.Add(new WorkNote
            {
                Author = string.IsNullOrWhiteSpace(author) ? "You" : author.Trim(),
                Body = body.Trim(),
                At = DateTime.Now,
                Internal = internalNote,
            });
            t.Updated = DateTime.Now;
        }
    }

    // ---- helpers -----------------------------------------------------------

    private string NextNumber(RecordType type) => $"{ItilUi.TypePrefix(type)}{++_counters[type]:D7}";

    private static string GroupFor(RecordType type) => type switch
    {
        RecordType.Change => "Change Management",
        RecordType.Problem => "Application Support",
        _ => "Service Desk",
    };

    private void SeedCis()
    {
        (string Name, CiType Type, CiHealth Health, string Owner)[] cis =
        {
            ("Email Service", CiType.BusinessService, CiHealth.Operational, "Collaboration"),
            ("Payment Gateway", CiType.BusinessService, CiHealth.Degraded, "Commerce"),
            ("CRM Application", CiType.Application, CiHealth.Operational, "Sales Ops"),
            ("Auth / SSO", CiType.BusinessService, CiHealth.Operational, "Security"),
            ("VPN Service", CiType.BusinessService, CiHealth.Down, "Network"),
            ("db-prod-primary", CiType.Database, CiHealth.Operational, "DBA Team"),
            ("web-prod-01", CiType.Server, CiHealth.Operational, "Platform"),
            ("core-switch-a", CiType.NetworkDevice, CiHealth.Maintenance, "Network"),
            ("HR Portal", CiType.Application, CiHealth.Operational, "People Ops"),
            ("File Storage", CiType.BusinessService, CiHealth.Operational, "Platform"),
        };
        foreach (var c in cis)
        {
            _cis.Add(new ConfigurationItem { Name = c.Name, Type = c.Type, Health = c.Health, Owner = c.Owner });
        }
    }

    private void SeedKnowledge()
    {
        _kb.Add(new KnowledgeArticle { Number = "KB0001001", Title = "Resetting your SSO password", Category = "Access", RelatedCi = "Auth / SSO", Body = "Use the self-service portal: Profile → Security → Reset password. If you are locked out, the Service Desk can issue a temporary password after identity verification." });
        _kb.Add(new KnowledgeArticle { Number = "KB0001002", Title = "Workaround: Payment Gateway timeouts at peak", Category = "Known Error", RelatedCi = "Payment Gateway", Body = "Until PRB is resolved, retry the transaction once after 30 seconds. The gateway connection pool is being increased under a scheduled change." });
        _kb.Add(new KnowledgeArticle { Number = "KB0001003", Title = "Connecting to the VPN", Category = "How-to", RelatedCi = "VPN Service", Body = "Install the corporate VPN client, sign in with SSO, and select the nearest region. Split-tunnel is enabled for SaaS apps." });
        _kb.Add(new KnowledgeArticle { Number = "KB0001004", Title = "Requesting a new laptop", Category = "How-to", Body = "Open a Service Request of type Hardware. Standard models ship in 3–5 business days; non-standard models require manager approval." });
        _kb.Add(new KnowledgeArticle { Number = "KB0001005", Title = "Known error: CRM export drops the last column", Category = "Known Error", RelatedCi = "CRM Application", Body = "A fix is scheduled. Workaround: add a trailing blank column before exporting, or use the API export which is unaffected." });
    }

    private void SeedRecords()
    {
        DateTime now = DateTime.Now;

        // Incidents: (short, impact, urgency, state, group, assignee, ci, ageHrs)
        Add(RecordType.Incident, "Email delivery delayed for all staff", Impact.High, Urgency.High, ServiceState.InProgress, "Service Desk", "Sam Cho", "Email Service", 1.5, problem: "PRB0001001");
        Add(RecordType.Incident, "Payment Gateway returning 504 at checkout", Impact.High, Urgency.High, ServiceState.New, "Service Desk", "Unassigned", "Payment Gateway", 0.3, problem: "PRB0001002");
        Add(RecordType.Incident, "VPN down for remote office", Impact.High, Urgency.Medium, ServiceState.OnHold, "Network", "Lena Park", "VPN Service", 6, note: "Carrier ticket opened; awaiting ISP.");
        Add(RecordType.Incident, "CRM export drops the last column", Impact.Medium, Urgency.Medium, ServiceState.InProgress, "Application Support", "Priya Nair", "CRM Application", 20, problem: "PRB0001003");
        Add(RecordType.Incident, "User cannot sign in via SSO", Impact.Low, Urgency.High, ServiceState.Resolved, "Service Desk", "Ana Vidal", "Auth / SSO", 30);
        Add(RecordType.Incident, "Reports page loads slowly", Impact.Low, Urgency.Low, ServiceState.New, "Application Support", "Unassigned", "CRM Application", 48);
        Add(RecordType.Incident, "File share read-only for Finance", Impact.Medium, Urgency.High, ServiceState.Assigned, "Platform", "Will Frey", "File Storage", 2);
        Add(RecordType.Incident, "Intermittent 500s on web-prod-01", Impact.Medium, Urgency.Medium, ServiceState.New, "Platform", "Unassigned", "web-prod-01", 3);
        Add(RecordType.Incident, "Database failover alert (resolved)", Impact.High, Urgency.High, ServiceState.Closed, "Database", "DBA Team", "db-prod-primary", 96);

        // Service Requests
        Add(RecordType.ServiceRequest, "New laptop for new hire", Impact.Low, Urgency.Medium, ServiceState.Assigned, "Field Services", "Kai Brandt", null, 5);
        Add(RecordType.ServiceRequest, "Access to CRM for Marketing team", Impact.Medium, Urgency.Medium, ServiceState.New, "Service Desk", "Unassigned", "CRM Application", 7);
        Add(RecordType.ServiceRequest, "Install Adobe license", Impact.Low, Urgency.Low, ServiceState.InProgress, "Service Desk", "Ana Vidal", null, 12);
        Add(RecordType.ServiceRequest, "VPN access for contractor", Impact.Medium, Urgency.High, ServiceState.New, "Security", "Unassigned", "VPN Service", 1);
        Add(RecordType.ServiceRequest, "Shared mailbox for support@", Impact.Low, Urgency.Low, ServiceState.Resolved, "Service Desk", "Sam Cho", "Email Service", 40);
        Add(RecordType.ServiceRequest, "Increase storage quota", Impact.Low, Urgency.Medium, ServiceState.Closed, "Platform", "Will Frey", "File Storage", 72);

        // Problems
        Add(RecordType.Problem, "Recurring email delivery delays at peak", Impact.High, Urgency.Medium, ServiceState.InProgress, "Application Support", "Sam Cho", "Email Service", 18, known: false);
        Add(RecordType.Problem, "Payment Gateway connection-pool exhaustion", Impact.High, Urgency.High, ServiceState.Assigned, "Application Support", "Priya Nair", "Payment Gateway", 10, known: true, workaround: "Retry once after 30s; pool size increase scheduled (CHG).");
        Add(RecordType.Problem, "CRM export column truncation", Impact.Medium, Urgency.Low, ServiceState.Resolved, "Application Support", "Priya Nair", "CRM Application", 60, known: true, workaround: "Add a trailing blank column, or use the API export.");

        // Changes
        Add(RecordType.Change, "Increase Payment Gateway connection pool", Impact.High, Urgency.High, ServiceState.Authorize, "Change Management", "Priya Nair", "Payment Gateway", 4, chg: TicketDesk.ChangeType.Normal, risk: ChangeRisk.High, approval: ApprovalState.Requested);
        Add(RecordType.Change, "Quarterly OS patching — web tier", Impact.Medium, Urgency.Low, ServiceState.Scheduled, "Platform", "Will Frey", "web-prod-01", 24, chg: TicketDesk.ChangeType.Normal, risk: ChangeRisk.Medium, approval: ApprovalState.Approved);
        Add(RecordType.Change, "Rotate TLS certificate — Email Service", Impact.Medium, Urgency.Medium, ServiceState.Implement, "Platform", "Sam Cho", "Email Service", 2, chg: TicketDesk.ChangeType.Standard, risk: ChangeRisk.Low, approval: ApprovalState.Approved);
        Add(RecordType.Change, "Core switch firmware upgrade", Impact.High, Urgency.Medium, ServiceState.Assess, "Network", "Lena Park", "core-switch-a", 8, chg: TicketDesk.ChangeType.Normal, risk: ChangeRisk.High, approval: ApprovalState.NotRequested);
        Add(RecordType.Change, "Add read replica to db-prod-primary", Impact.Medium, Urgency.Low, ServiceState.New, "Database", "DBA Team", "db-prod-primary", 1, chg: TicketDesk.ChangeType.Normal, risk: ChangeRisk.Medium, approval: ApprovalState.NotRequested);
        Add(RecordType.Change, "Emergency: block malicious IP range", Impact.High, Urgency.High, ServiceState.Review, "Security", "Lena Park", "core-switch-a", 30, chg: TicketDesk.ChangeType.Emergency, risk: ChangeRisk.High, approval: ApprovalState.Approved);

        void Add(RecordType type, string shortDesc, Impact impact, Urgency urgency, ServiceState state,
            string group, string assignee, string? ci, double ageHrs,
            string? problem = null, string? note = null, bool known = false, string? workaround = null,
            ChangeType? chg = null, ChangeRisk? risk = null, ApprovalState approval = ApprovalState.NotRequested)
        {
            Ticket t = new()
            {
                Id = _nextId++,
                Type = type,
                Number = NextNumber(type),
                ShortDescription = shortDesc,
                Description = $"{ItilUi.TypeLabel(type)} affecting {ci ?? "no specific CI"}. See the activity stream for diagnosis and current status.",
                Impact = impact,
                Urgency = urgency,
                State = state,
                AssignmentGroup = group,
                AssignedTo = assignee,
                Requester = "Service Desk",
                ConfigItem = ci,
                Category = type == RecordType.ServiceRequest ? "Request" : type == RecordType.Change ? "Change" : "Inquiry",
                Opened = now.AddHours(-ageHrs),
                Updated = now.AddHours(-ageHrs / 3),
                RelatedProblem = problem,
                KnownError = known,
                Workaround = workaround,
                ChangeType = chg,
                Risk = risk,
                Approval = approval,
                PlannedStart = type == RecordType.Change ? now.AddHours(ageHrs) : null,
            };
            if (Workflow.IsResolvedOrBeyond(state))
            {
                t.ResolvedAt = now.AddHours(-ageHrs / 4);
                t.Resolution = "Resolved and verified.";
            }

            if (note is not null)
            {
                t.Notes.Add(new WorkNote { Author = assignee, Body = note, At = t.Updated, Internal = true });
            }

            _records.Add(t);
        }
    }
}
