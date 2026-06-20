using System.ComponentModel.DataAnnotations;
using BlazorDX.Primitives.Forms;

namespace BlazorDX.Demo.Client;

/// <summary>
/// An ordinary <c>System.ComponentModel.DataAnnotations</c> model — the kind a team already
/// has thousands of. The only BlazorDX-specific addition is the class-level
/// <c>[DxFormModel]</c>; every field constraint comes from standard attributes, and the
/// cross-field rule from <see cref="IValidatableObject"/>. The generator turns it into a
/// rendered form, validation, and an AI tool — reflection-free.
/// </summary>
[DxFormModel(Name = "reserve_room", Description = "Reserve a meeting room.")]
public sealed class RoomReservation : IValidatableObject
{
    [Required]
    [StringLength(40)]
    [Display(Name = "Room name", Description = "Which room to reserve.", Order = 0, Prompt = "e.g. Aspen")]
    public string Room { get; set; } = string.Empty;

    [Range(1, 20)]
    [Display(Name = "Seats needed", Order = 1)]
    public int Seats { get; set; } = 1;

    [Required]
    [EmailAddress]
    [Display(Name = "Organizer email", Order = 2, Prompt = "you@team.io")]
    public string Email { get; set; } = string.Empty;

    [Range(0, 23)]
    [Display(Name = "Start hour (0–23)", Order = 3)]
    public int StartHour { get; set; } = 9;

    [Range(0, 23)]
    [Display(Name = "End hour (0–23)", Order = 4)]
    public int EndHour { get; set; } = 10;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndHour <= StartHour)
        {
            yield return new ValidationResult("End hour must be after start hour.", new[] { nameof(EndHour) });
        }
    }
}
