using System.ComponentModel.DataAnnotations;
using BlazorDX.Primitives.Forms;

namespace BlazorDX.Demo.Client.TicketDesk;

/// <summary>
/// ITIL record intake. One <c>[DxFormModel]</c> renders the form, validates it, AND projects an
/// <c>open_record</c> MCP/AI tool. Note it captures <b>Impact</b> and <b>Urgency</b> — never
/// Priority, which ITIL derives from them. Enum fields render as dropdowns automatically.
/// </summary>
[DxFormModel(Name = "open_record", Description = "Open an ITIL service record (incident, request, problem, or change).")]
public sealed class NewTicket
{
    [Display(Name = "Record type", Description = "Which ITIL practice this belongs to.", Order = 0)]
    public RecordType Type { get; set; } = RecordType.Incident;

    [Required]
    [StringLength(100, MinimumLength = 5)]
    [Display(Name = "Short description", Order = 1, Prompt = "One-line summary")]
    public string ShortDescription { get; set; } = string.Empty;

    [Required]
    [StringLength(4000, MinimumLength = 10)]
    [Display(Name = "Description", Order = 2, Prompt = "Details, steps, expected vs actual…")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Impact", Description = "How widely is it felt?", Order = 3)]
    public Impact Impact { get; set; } = Impact.Medium;

    [Display(Name = "Urgency", Description = "How fast must it be resolved?", Order = 4)]
    public Urgency Urgency { get; set; } = Urgency.Medium;

    [Required]
    [StringLength(60)]
    [Display(Name = "Requester", Order = 5, Prompt = "Your name")]
    public string Requester { get; set; } = string.Empty;

    [StringLength(60)]
    [Display(Name = "Configuration item (CI)", Description = "Affected service or asset.", Order = 6, Prompt = "e.g. Email Service")]
    public string ConfigItem { get; set; } = string.Empty;
}
