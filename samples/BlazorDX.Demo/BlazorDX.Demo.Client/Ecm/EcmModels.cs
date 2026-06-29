using System.ComponentModel.DataAnnotations;
using BlazorDX.Primitives.Forms;
using BlazorDX.Primitives.Grid;

namespace BlazorDX.Demo.Client.Ecm;

/// <summary>Sensitivity label that drives access expectations and the badge colour.</summary>
public enum DocClassification
{
    Public,
    Internal,
    Confidential,
    Restricted,
}

/// <summary>Where a document sits in its review/publish lifecycle.</summary>
public enum DocStatus
{
    Draft,
    InReview,
    Approved,
    Published,
    Archived,
}

/// <summary>One immutable revision in a document's version history.</summary>
/// <param name="Label">Version label, e.g. "1.3".</param>
/// <param name="Author">Who saved the revision.</param>
/// <param name="At">When it was saved.</param>
/// <param name="Note">The change note.</param>
public sealed record DocVersion(string Label, string Author, DateTime At, string Note);

/// <summary>
/// A managed document in the content repository. One record carries the binary's metadata, its
/// place in the folder tree, the governance fields (classification, retention), the lifecycle
/// state, the check-out lock, and the full version history. The grid sees a flat
/// <see cref="DocumentRow"/> projection; the editor edits a <see cref="DocumentMetadata"/> model.
/// </summary>
public sealed class EcmDocument
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Folder path, e.g. "/Policies/HR".</summary>
    public string Folder { get; set; } = "/";

    public string Extension { get; set; } = "pdf";

    public long Size { get; set; }

    public string Owner { get; set; } = "Unassigned";

    public DocStatus Status { get; set; } = DocStatus.Draft;

    public DocClassification Classification { get; set; } = DocClassification.Internal;

    public string? CheckedOutBy { get; set; }

    public DateTime Modified { get; set; }

    public DateTime? RetentionUntil { get; set; }

    public string Summary { get; set; } = string.Empty;

    public List<string> Tags { get; } = new();

    public List<DocVersion> Versions { get; } = new();

    public bool IsCheckedOut => CheckedOutBy is not null;

    public string VersionLabel => Versions.Count > 0 ? Versions[^1].Label : "1.0";

    public DocumentRow ToRow() => new()
    {
        Id = Id,
        Name = Name,
        Type = Extension.ToUpperInvariant(),
        Status = EcmUi.StatusLabel(Status),
        Classification = Classification.ToString(),
        Owner = Owner,
        Version = VersionLabel,
        Size = EcmUi.FormatSize(Size),
        Lock = IsCheckedOut ? $"🔒 {CheckedOutBy}" : "—",
        Modified = Modified.ToString("MMM d, yyyy"),
    };
}

/// <summary>Flat projection for the document grid (all string/int — drives BlazorDX.SourceGen).</summary>
[GridRow]
public sealed class DocumentRow
{
    [GridColumn("Name", Order = 0)] public string Name { get; set; } = string.Empty;
    [GridColumn("Type", Order = 1)] public string Type { get; set; } = string.Empty;
    [GridColumn("Status", Order = 2)] public string Status { get; set; } = string.Empty;
    [GridColumn("Classification", Order = 3)] public string Classification { get; set; } = string.Empty;
    [GridColumn("Owner", Order = 4)] public string Owner { get; set; } = string.Empty;
    [GridColumn("Version", Order = 5)] public string Version { get; set; } = string.Empty;
    [GridColumn("Size", Order = 6)] public string Size { get; set; } = string.Empty;
    [GridColumn("Lock", Order = 7)] public string Lock { get; set; } = string.Empty;
    [GridColumn("Modified", Order = 8)] public string Modified { get; set; } = string.Empty;
    public int Id { get; set; }
}

/// <summary>
/// The editable metadata of a document — one model that <see cref="BlazorDX.Components.DxForm{TModel}"/>
/// renders as a form (enum fields become dropdowns) and validates via DataAnnotations.
/// </summary>
[DxFormModel(Name = "document_metadata", Description = "Edit a managed document's governance metadata.")]
public sealed class DocumentMetadata
{
    [Required, StringLength(120, MinimumLength = 2)]
    [Display(Name = "Name", Order = 0, Prompt = "Document title")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Classification", Order = 1)]
    public DocClassification Classification { get; set; } = DocClassification.Internal;

    [Display(Name = "Status", Order = 2)]
    public DocStatus Status { get; set; } = DocStatus.Draft;

    [Required, StringLength(80, MinimumLength = 2)]
    [Display(Name = "Owner", Order = 3)]
    public string Owner { get; set; } = string.Empty;

    [Display(Name = "Tags (comma-separated)", Order = 4, Prompt = "policy, 2026, hr")]
    public string Tags { get; set; } = string.Empty;

    [StringLength(400)]
    [Display(Name = "Summary", Order = 5)]
    public string Summary { get; set; } = string.Empty;
}

/// <summary>Shared label/format helpers for the ECM demo.</summary>
public static class EcmUi
{
    public static string StatusLabel(DocStatus s) => s switch
    {
        DocStatus.InReview => "In review",
        _ => s.ToString(),
    };

    public static string StatusVariant(DocStatus s) => s switch
    {
        DocStatus.Draft => "default",
        DocStatus.InReview => "warning",
        DocStatus.Approved => "info",
        DocStatus.Published => "success",
        DocStatus.Archived => "default",
        _ => "default",
    };

    public static string ClassificationVariant(DocClassification c) => c switch
    {
        DocClassification.Public => "success",
        DocClassification.Internal => "info",
        DocClassification.Confidential => "warning",
        DocClassification.Restricted => "danger",
        _ => "default",
    };

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double kb = bytes / 1024.0;
        return kb < 1024 ? $"{kb:0.#} KB" : $"{kb / 1024:0.#} MB";
    }
}
