namespace BlazorDX.Demo.Client.Ecm;

/// <summary>A node in the repository folder tree.</summary>
/// <param name="Path">Full path, e.g. "/Policies/HR".</param>
/// <param name="Name">Display name (last segment).</param>
/// <param name="Depth">Indentation depth for the tree (0 = top level).</param>
public sealed record EcmFolder(string Path, string Name, int Depth);

/// <summary>
/// In-memory content repository for the ECM demo: a folder tree plus managed documents with
/// governance metadata, lifecycle state, check-out locks, and version history. Registered
/// <b>Scoped</b> (never Singleton — the BlazorDX analyzer enforces that) so each session gets its
/// own isolated copy.
/// </summary>
public sealed class EcmStore
{
    private readonly List<EcmDocument> _docs = new();
    private int _nextId = 1;

    public EcmStore() => Seed();

    /// <summary>The folder tree, pre-flattened in display order with depth.</summary>
    public IReadOnlyList<EcmFolder> Folders { get; } =
    [
        new("/", "All documents", 0),
        new("/Policies", "Policies", 0),
        new("/Policies/HR", "HR", 1),
        new("/Policies/Security", "Security", 1),
        new("/Contracts", "Contracts", 0),
        new("/Contracts/Vendors", "Vendors", 1),
        new("/Finance", "Finance", 0),
        new("/Engineering", "Engineering", 0),
    ];

    // ---- queries -----------------------------------------------------------

    /// <summary>Documents in a folder (the root "/" returns everything).</summary>
    public IReadOnlyList<EcmDocument> InFolder(string path) =>
        (path == "/" ? _docs : _docs.Where(d => d.Folder == path)).OrderBy(d => d.Name).ToList();

    public EcmDocument? Get(int id) => _docs.FirstOrDefault(d => d.Id == id);

    public int CountByStatus(DocStatus status) => _docs.Count(d => d.Status == status);

    public int CheckedOutCount => _docs.Count(d => d.IsCheckedOut);

    public int Total => _docs.Count;

    // ---- lifecycle ---------------------------------------------------------

    public void CheckOut(EcmDocument doc, string user)
    {
        doc.CheckedOutBy ??= user;
    }

    /// <summary>Checks in a checked-out document, appending a version revision.</summary>
    public void CheckIn(EcmDocument doc, string note)
    {
        if (!doc.IsCheckedOut)
        {
            return;
        }

        string user = doc.CheckedOutBy!;
        doc.CheckedOutBy = null;
        doc.Modified = Now;
        doc.Versions.Add(new DocVersion(NextVersion(doc), user, Now,
            string.IsNullOrWhiteSpace(note) ? "Checked in" : note.Trim()));
    }

    public void SetStatus(EcmDocument doc, DocStatus status)
    {
        if (doc.Status == status)
        {
            return;
        }

        doc.Status = status;
        doc.Modified = Now;
        doc.Versions.Add(new DocVersion(NextVersion(doc), doc.Owner, Now, $"Status → {EcmUi.StatusLabel(status)}"));
    }

    /// <summary>Applies edited metadata and records a revision.</summary>
    public void ApplyMetadata(EcmDocument doc, DocumentMetadata edit)
    {
        doc.Name = edit.Name.Trim();
        doc.Owner = edit.Owner.Trim();
        doc.Classification = edit.Classification;
        doc.Status = edit.Status;
        doc.Summary = edit.Summary.Trim();
        doc.Tags.Clear();
        foreach (string tag in edit.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            doc.Tags.Add(tag);
        }

        doc.Modified = Now;
        doc.Versions.Add(new DocVersion(NextVersion(doc), doc.Owner, Now, "Metadata updated"));
    }

    public static DocumentMetadata MetadataOf(EcmDocument doc) => new()
    {
        Name = doc.Name,
        Owner = doc.Owner,
        Classification = doc.Classification,
        Status = doc.Status,
        Summary = doc.Summary,
        Tags = string.Join(", ", doc.Tags),
    };

    private static string NextVersion(EcmDocument doc)
    {
        string current = doc.VersionLabel;
        int dot = current.IndexOf('.');
        if (dot > 0
            && int.TryParse(current[..dot], out int major)
            && int.TryParse(current[(dot + 1)..], out int minor))
        {
            return $"{major}.{minor + 1}";
        }

        return "1.1";
    }

    private static DateTime Now => DateTime.Now;

    // ---- seed --------------------------------------------------------------

    private void Add(string name, string folder, string ext, long size, string owner,
        DocStatus status, DocClassification cls, int agedDays, string summary, params string[] tags)
    {
        EcmDocument doc = new()
        {
            Id = _nextId++,
            Name = name,
            Folder = folder,
            Extension = ext,
            Size = size,
            Owner = owner,
            Status = status,
            Classification = cls,
            Modified = DateTime.Now.AddDays(-agedDays),
            RetentionUntil = DateTime.Now.AddYears(cls == DocClassification.Restricted ? 7 : 3),
            Summary = summary,
        };
        doc.Tags.AddRange(tags);
        doc.Versions.Add(new DocVersion("1.0", owner, doc.Modified.AddDays(-2), "Initial draft"));
        if (status != DocStatus.Draft)
        {
            doc.Versions.Add(new DocVersion("1.1", owner, doc.Modified, EcmUi.StatusLabel(status)));
        }

        _docs.Add(doc);
    }

    private void Seed()
    {
        Add("Employee Handbook 2026", "/Policies/HR", "docx", 248_400, "Dana Ortiz",
            DocStatus.Published, DocClassification.Internal, 12,
            "Company-wide handbook covering conduct, leave, and benefits.", "handbook", "2026", "hr");
        Add("Remote Work Policy", "/Policies/HR", "pdf", 92_100, "Dana Ortiz",
            DocStatus.Approved, DocClassification.Internal, 30, "Eligibility and expectations for remote work.", "policy", "remote");
        Add("Information Security Policy", "/Policies/Security", "pdf", 311_900, "Sam Reyes",
            DocStatus.Published, DocClassification.Confidential, 5, "ISO-27001-aligned security policy.", "security", "iso27001");
        Add("Incident Response Plan", "/Policies/Security", "docx", 188_250, "Sam Reyes",
            DocStatus.InReview, DocClassification.Restricted, 1, "Runbook for security incident handling.", "security", "runbook");
        Add("Acme MSA (signed)", "/Contracts/Vendors", "pdf", 540_000, "Priya Patel",
            DocStatus.Published, DocClassification.Confidential, 60, "Master service agreement with Acme Corp.", "contract", "acme");
        Add("Globex SOW Q3", "/Contracts/Vendors", "docx", 132_700, "Priya Patel",
            DocStatus.Draft, DocClassification.Confidential, 0, "Statement of work draft for Globex.", "contract", "sow");
        Add("FY26 Budget", "/Finance", "xlsx", 421_300, "Lee Chan",
            DocStatus.Approved, DocClassification.Restricted, 9, "Fiscal-year 2026 operating budget.", "finance", "budget", "2026");
        Add("Expense Report Template", "/Finance", "xlsx", 38_900, "Lee Chan",
            DocStatus.Published, DocClassification.Internal, 45, "Standard expense reimbursement template.", "finance", "template");
        Add("Architecture Decision Records", "/Engineering", "docx", 96_500, "Jordan Kim",
            DocStatus.InReview, DocClassification.Internal, 2, "Consolidated ADRs for the platform.", "engineering", "adr");
        Add("API Style Guide", "/Engineering", "pdf", 72_200, "Jordan Kim",
            DocStatus.Published, DocClassification.Public, 20, "Public REST/JSON conventions.", "engineering", "api");
        Add("Data Retention Schedule", "/Policies", "xlsx", 54_000, "Sam Reyes",
            DocStatus.Approved, DocClassification.Confidential, 15, "Record categories and retention windows.", "governance", "retention");
        Add("Vendor Onboarding Checklist", "/Contracts", "docx", 41_800, "Priya Patel",
            DocStatus.Draft, DocClassification.Internal, 3, "Steps to onboard a new vendor.", "contract", "checklist");
    }
}
