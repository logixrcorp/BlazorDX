using System.Collections.ObjectModel;

namespace BlazorDX.MockReportServer;

/// <summary>
/// The SSRS-style data type of a report parameter. Mirrors the values the real
/// SSRS <c>GetItemParameters</c> / REST <c>ParameterDefinitions</c> surface
/// (<c>String</c>, <c>Integer</c>, <c>Boolean</c>, <c>DateTime</c>, <c>Float</c>).
/// </summary>
public enum ReportParameterType
{
    String,
    Integer,
    Boolean,
    DateTime,
    Float,
}

/// <summary>
/// Metadata for one report parameter, shaped after SSRS parameter definitions
/// so a viewer can build a parameter-entry form from it.
/// </summary>
/// <param name="Name">Internal parameter name (used as the query key).</param>
/// <param name="Type">SSRS data type.</param>
/// <param name="Prompt">Human-facing label SSRS would show.</param>
/// <param name="Nullable">Whether the parameter accepts a null/blank value.</param>
/// <param name="MultiValue">Whether the parameter accepts repeated values.</param>
/// <param name="DefaultValue">Default value applied when the caller omits it; <c>null</c> means required.</param>
/// <param name="ValidValues">Optional closed list of allowed values (an SSRS "available values" list).</param>
public sealed record ReportParameter(
    string Name,
    ReportParameterType Type,
    string Prompt,
    bool Nullable,
    bool MultiValue,
    string? DefaultValue,
    IReadOnlyList<string>? ValidValues)
{
    /// <summary>
    /// A parameter is required when it has no default and does not accept null.
    /// This is the condition that drives an <c>rsParameterError</c> when omitted.
    /// </summary>
    public bool IsRequired => DefaultValue is null && !Nullable;
}

/// <summary>One row of a report's canned result set.</summary>
public sealed record ReportRow(IReadOnlyList<string> Cells);

/// <summary>
/// A single canned report: its catalog path, title, declared parameters, and a
/// builder that turns the supplied (validated) parameter values into a
/// deterministic table. The table is what every output format renders from.
/// </summary>
public sealed class ReportDefinition
{
    public required string Path { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<ReportParameter> Parameters { get; init; }

    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>
    /// Produces the report's rows from the resolved parameter values. Kept
    /// deterministic (a pure function of its inputs) so tests can assert on it.
    /// </summary>
    public required Func<IReadOnlyDictionary<string, IReadOnlyList<string>>, IReadOnlyList<ReportRow>> BuildRows { get; init; }
}

/// <summary>
/// The canned, deterministic SSRS catalog the mock serves. Folders and reports
/// are fixed at construction so behaviour is fully reproducible across runs.
/// </summary>
public sealed class ReportCatalog
{
    private readonly Dictionary<string, ReportDefinition> _reports;

    public ReportCatalog()
    {
        var reports = new[]
        {
            BuildSalesMonthly(),
            BuildHrHeadcount(),
        };

        _reports = reports.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>All reports, ordered by path for deterministic listings.</summary>
    public IReadOnlyList<ReportDefinition> Reports =>
        new ReadOnlyCollection<ReportDefinition>(
            _reports.Values.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList());

    /// <summary>Looks up a report by its catalog path (case-insensitive, as SSRS treats paths).</summary>
    public ReportDefinition? Find(string path) =>
        _reports.TryGetValue(path, out var def) ? def : null;

    /// <summary>
    /// Returns the immediate catalog children under a folder path, for
    /// <c>rs:Command=ListChildren</c>. A child is a report whose parent folder
    /// equals <paramref name="folderPath"/>.
    /// </summary>
    public IReadOnlyList<CatalogItem> ListChildren(string folderPath)
    {
        var normalized = NormalizeFolder(folderPath);
        var items = new List<CatalogItem>();

        foreach (var report in _reports.Values)
        {
            var parent = ParentFolder(report.Path);
            if (string.Equals(parent, normalized, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new CatalogItem(report.Path, NameOf(report.Path), "Report"));
            }
        }

        // Surface immediate sub-folders too, like the real ListChildren does.
        var folders = _reports.Values
            .Select(r => ParentFolder(r.Path))
            .Where(p => IsImmediateChildFolder(normalized, p))
            .Select(p => new CatalogItem(p, NameOf(p), "Folder"))
            .DistinctBy(c => c.Path, StringComparer.OrdinalIgnoreCase);

        items.AddRange(folders);
        return items.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsImmediateChildFolder(string parent, string candidate)
    {
        if (string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidateParent = ParentFolder(candidate);
        return string.Equals(candidateParent, parent, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return "/";
        }

        var trimmed = folder.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    private static string ParentFolder(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx <= 0 ? "/" : path[..idx];
    }

    private static string NameOf(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx < 0 ? path : path[(idx + 1)..];
    }

    private static ReportDefinition BuildSalesMonthly()
    {
        var parameters = new ReportParameter[]
        {
            new(
                Name: "Region",
                Type: ReportParameterType.String,
                Prompt: "Sales region",
                Nullable: false,
                MultiValue: true,
                DefaultValue: null,
                ValidValues: new[] { "West", "East", "North", "South" }),
            new(
                Name: "Year",
                Type: ReportParameterType.Integer,
                Prompt: "Fiscal year",
                Nullable: false,
                MultiValue: false,
                DefaultValue: "2026",
                ValidValues: null),
        };

        return new ReportDefinition
        {
            Path = "/Sales/Monthly",
            Title = "Monthly Sales",
            Parameters = parameters,
            Columns = new[] { "Region", "Year", "Month", "Revenue" },
            BuildRows = values =>
            {
                var regions = values.TryGetValue("Region", out var r) && r.Count > 0
                    ? r
                    : new[] { "West" };
                var year = values.TryGetValue("Year", out var y) && y.Count > 0 ? y[0] : "2026";

                var rows = new List<ReportRow>();
                var months = new[] { "Jan", "Feb", "Mar" };
                foreach (var region in regions)
                {
                    for (var i = 0; i < months.Length; i++)
                    {
                        // Deterministic synthetic revenue so assertions are stable.
                        var revenue = 1000 + (region.Length * 100) + (i * 250);
                        rows.Add(new ReportRow(new[]
                        {
                            region,
                            year,
                            months[i],
                            revenue.ToString("C0", System.Globalization.CultureInfo.InvariantCulture),
                        }));
                    }
                }

                return rows;
            },
        };
    }

    private static ReportDefinition BuildHrHeadcount()
    {
        var parameters = new ReportParameter[]
        {
            new(
                Name: "Department",
                Type: ReportParameterType.String,
                Prompt: "Department",
                Nullable: false,
                MultiValue: false,
                DefaultValue: null,
                ValidValues: null),
        };

        return new ReportDefinition
        {
            Path = "/HR/Headcount",
            Title = "Department Headcount",
            Parameters = parameters,
            Columns = new[] { "Department", "Role", "Headcount" },
            BuildRows = values =>
            {
                var dept = values.TryGetValue("Department", out var d) && d.Count > 0 ? d[0] : "Unknown";
                var roles = new[] { "Manager", "Engineer", "Analyst" };
                var rows = new List<ReportRow>();
                for (var i = 0; i < roles.Length; i++)
                {
                    var headcount = 2 + (dept.Length % 5) + i;
                    rows.Add(new ReportRow(new[]
                    {
                        dept,
                        roles[i],
                        headcount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    }));
                }

                return rows;
            },
        };
    }
}

/// <summary>One entry returned by <c>rs:Command=ListChildren</c>.</summary>
/// <param name="Path">Full catalog path.</param>
/// <param name="Name">Leaf name.</param>
/// <param name="Type">SSRS item type, e.g. <c>Report</c> or <c>Folder</c>.</param>
public sealed record CatalogItem(string Path, string Name, string Type);
