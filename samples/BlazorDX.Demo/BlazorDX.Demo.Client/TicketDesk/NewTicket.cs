using System.ComponentModel.DataAnnotations;
using BlazorDX.Primitives.Forms;

namespace BlazorDX.Demo.Client.TicketDesk;

/// <summary>
/// The "open a ticket" form. One class-level <c>[DxFormModel]</c> over ordinary
/// DataAnnotations makes it a rendered <c>DxForm</c>, a validation contract, AND an
/// MCP/AI tool (<c>create_ticket</c>) — the same model a person fills or an assistant calls.
/// Enum fields render as dropdowns automatically.
/// </summary>
[DxFormModel(Name = "create_ticket", Description = "Open a new support ticket.")]
public sealed class NewTicket
{
    [Required]
    [StringLength(80, MinimumLength = 4)]
    [Display(Name = "Title", Description = "A short, specific summary.", Order = 0, Prompt = "e.g. Login button does nothing on Safari")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(2000, MinimumLength = 10)]
    [Display(Name = "Description", Description = "What happened, and how to reproduce it.", Order = 1, Prompt = "Steps, expected vs actual…")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Priority", Order = 2)]
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;

    [Display(Name = "Category", Order = 3)]
    public TicketCategory Category { get; set; } = TicketCategory.Bug;

    [Required]
    [StringLength(60)]
    [Display(Name = "Requester", Description = "Who is reporting this.", Order = 4, Prompt = "Your name")]
    public string Requester { get; set; } = string.Empty;

    [StringLength(60)]
    [Display(Name = "Assignee", Description = "Leave as Unassigned to triage later.", Order = 5)]
    public string Assignee { get; set; } = "Unassigned";
}
