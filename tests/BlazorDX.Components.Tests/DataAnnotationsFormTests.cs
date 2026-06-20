using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using BlazorDX.Primitives.Forms;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// A model annotated with standard <c>System.ComponentModel.DataAnnotations</c> attributes —
/// no <c>[DxField]</c> anywhere — plus <see cref="IValidatableObject"/> for a cross-field rule.
/// One class-level <c>[DxFormModel]</c> turns it into a BlazorDX form + AI tool; the generator
/// reads the DataAnnotations at compile time, so the path stays reflection-free.
/// </summary>
[DxFormModel(Name = "book_room", Description = "Reserve a meeting room.")]
public sealed class RoomBooking : IValidatableObject
{
    [Required]
    [StringLength(40)]
    [Display(Name = "Room name", Description = "Which room to reserve.", Order = 0, Prompt = "e.g. Aspen")]
    public string Room { get; set; } = string.Empty;

    [Range(1, 20)]
    [Display(Name = "Seats", Order = 1)]
    public int Seats { get; set; }

    [Required]
    [EmailAddress]
    [Display(Name = "Organizer email", Order = 2)]
    public string Email { get; set; } = string.Empty;

    [Range(0, 23)]
    [Display(Name = "Start hour", Order = 3)]
    public int StartHour { get; set; }

    [Range(0, 23)]
    [Display(Name = "End hour", Order = 4)]
    public int EndHour { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndHour <= StartHour)
        {
            yield return new ValidationResult("End hour must be after start hour.", new[] { nameof(EndHour) });
        }
    }
}

/// <summary>The generated descriptor for a pure-DataAnnotations model.</summary>
public sealed class DataAnnotationsFormTests
{
    private static readonly RoomBookingFormModel Model = new();

    private static FormFieldInfo Field(string name) => Model.Fields.First(f => f.Name == name);

    private static RoomBooking Valid() =>
        new() { Room = "Aspen", Seats = 6, Email = "lead@team.io", StartHour = 9, EndHour = 10 };

    [Fact]
    public void Reads_dataannotations_into_field_metadata()
    {
        Assert.Equal(5, Model.Fields.Count);

        // [Display(Name=...)] becomes the label; [Display(Description/Prompt)] flow through.
        Assert.Equal("Room name", Field("Room").Label);
        Assert.Equal("Which room to reserve.", Field("Room").Description);
        Assert.Equal("e.g. Aspen", Field("Room").Placeholder);

        // [Required] + [StringLength(40)].
        Assert.True(Field("Room").Required);
        Assert.Equal(40, Field("Room").MaxLength);

        // [Range(1,20)] on an int.
        Assert.Equal(FormFieldKind.Integer, Field("Seats").Kind);
        Assert.Equal(1, Field("Seats").Min);
        Assert.Equal(20, Field("Seats").Max);

        // [EmailAddress] becomes a format pattern.
        Assert.True(Field("Email").Required);
        Assert.False(string.IsNullOrEmpty(Field("Email").Pattern));

        // [Display(Order=...)] drives declared order.
        Assert.Equal(new[] { "Room", "Seats", "Email", "StartHour", "EndHour" },
            Model.Fields.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void Validates_dataannotations_constraints()
    {
        var errors = Model.Validate(new RoomBooking());   // empty: Room/Email blank, Seats 0
        Assert.Contains(errors, e => e.Field == "Room");      // [Required]
        Assert.Contains(errors, e => e.Field == "Email");     // [Required]
        Assert.Contains(errors, e => e.Field == "Seats");     // below [Range] min

        RoomBooking badEmail = Valid();
        badEmail.Email = "not-an-email";
        Assert.Contains(Model.Validate(badEmail), e => e.Field == "Email");   // [EmailAddress] pattern

        Assert.Empty(Model.Validate(Valid()));
    }

    [Fact]
    public void Runs_ivalidatableobject_cross_field_rule()
    {
        RoomBooking overlapping = Valid();
        overlapping.StartHour = 14;
        overlapping.EndHour = 10;   // end before start

        var errors = Model.Validate(overlapping);
        FormValidationError crossField = Assert.Single(errors);
        Assert.Equal("EndHour", crossField.Field);
        Assert.Equal("End hour must be after start hour.", crossField.Message);

        // Once the times are consistent the cross-field rule no longer fires.
        RoomBooking ok = Valid();
        Assert.Empty(Model.Validate(ok));
    }

    [Fact]
    public void Projects_dataannotations_into_the_ai_tool_schema()
    {
        using JsonDocument doc = JsonDocument.Parse(FormTool.BuildToolDefinition(Model));
        JsonElement root = doc.RootElement;

        Assert.Equal("book_room", root.GetProperty("name").GetString());

        JsonElement props = root.GetProperty("input_schema").GetProperty("properties");
        Assert.Equal("integer", props.GetProperty("Seats").GetProperty("type").GetString());
        Assert.Equal(1, props.GetProperty("Seats").GetProperty("minimum").GetInt32());
        Assert.Equal(20, props.GetProperty("Seats").GetProperty("maximum").GetInt32());
        Assert.Equal(40, props.GetProperty("Room").GetProperty("maxLength").GetInt32());
        Assert.False(string.IsNullOrEmpty(props.GetProperty("Email").GetProperty("pattern").GetString()));

        var required = root.GetProperty("input_schema").GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Room", required);
        Assert.Contains("Email", required);
        Assert.DoesNotContain("Seats", required);
    }

    [Fact]
    public void Ai_tool_call_arguments_fill_then_cross_field_validate()
    {
        const string toolCall = """
            { "Room": "Aspen", "Seats": 6, "Email": "lead@team.io", "StartHour": 15, "EndHour": 9 }
            """;

        RoomBooking target = new();
        var errors = FormTool.ApplyArguments(Model, target, toolCall);

        Assert.Equal("Aspen", target.Room);
        Assert.Equal(6, target.Seats);
        // The AI supplied an inconsistent range; the cross-field rule rejects it so the model can self-correct.
        Assert.Contains(errors, e => e.Field == "EndHour");
    }
}
