using System.Linq;
using System.Text.Json;
using BlazorDX.Primitives.Forms;
using Xunit;

namespace BlazorDX.Components.Tests;

public enum Priority { Low, Normal, High }

/// <summary>A form model that doubles as an AI tool — the source generator emits its descriptor.</summary>
[DxFormModel(Name = "schedule_meeting", Description = "Schedule a meeting with a teammate.")]
public sealed class MeetingRequest
{
    [DxField("Title", Required = true, MaxLength = 80, Description = "Meeting title.")]
    public string Title { get; set; } = string.Empty;

    [DxField("Attendees", Min = 1, Max = 50, Description = "Number of attendees.")]
    public int Attendees { get; set; }

    [DxField("Email", Required = true, Pattern = @"^[^@\s]+@[^@\s]+$", Description = "Organizer email.")]
    public string Email { get; set; } = string.Empty;

    [DxField("Remote", Description = "Whether the meeting is remote.")]
    public bool Remote { get; set; }

    [DxField("Priority", Description = "Priority level.")]
    public Priority Priority { get; set; }

    [DxField("Notes", Multiline = true, Required = true, Description = "Extra notes.",
        DependsOn = nameof(Priority), DependsOnValue = "High")]
    public string? Notes { get; set; }
}

/// <summary>The generated <see cref="IFormModel{TModel}"/> descriptor + the AI-tool projection.</summary>
public sealed class FormModelTests
{
    private static readonly MeetingRequestFormModel Model = new();

    private static FormFieldInfo Field(string name) => Model.Fields.First(f => f.Name == name);

    [Fact]
    public void Generates_field_metadata_with_kinds_and_constraints()
    {
        Assert.Equal(6, Model.Fields.Count);

        Assert.Equal(FormFieldKind.Integer, Field("Attendees").Kind);
        Assert.Equal(1, Field("Attendees").Min);
        Assert.Equal(50, Field("Attendees").Max);

        Assert.Equal(FormFieldKind.Multiline, Field("Notes").Kind);
        Assert.Equal(FormFieldKind.Bool, Field("Remote").Kind);

        FormFieldInfo priority = Field("Priority");
        Assert.Equal(FormFieldKind.Enum, priority.Kind);
        Assert.Equal(new[] { "Low", "Normal", "High" }, priority.Choices);

        Assert.True(Field("Title").Required);
        Assert.True(Field("Notes").Required);   // conditionally required — see DependsOn below

        Assert.Null(Field("Title").DependsOn);   // unconditional fields carry no DependsOn
        FormFieldInfo notes = Field("Notes");
        Assert.Equal("Priority", notes.DependsOn);
        Assert.Equal("High", notes.DependsOnValue);
        Assert.Equal(FormFieldDependsOnOperator.Equals, notes.DependsOnOperator);
    }

    [Fact]
    public void Validate_flags_required_range_and_pattern()
    {
        MeetingRequest empty = new();   // Title/Email empty, Attendees 0
        var errors = Model.Validate(empty);

        Assert.Contains(errors, e => e.Field == "Title");      // required
        Assert.Contains(errors, e => e.Field == "Email");      // required
        Assert.Contains(errors, e => e.Field == "Attendees");  // below min (0 < 1)

        var bad = new MeetingRequest { Title = "Sync", Email = "not-an-email", Attendees = 3 };
        Assert.Contains(Model.Validate(bad), e => e.Field == "Email");   // pattern

        var ok = new MeetingRequest { Title = "Sync", Email = "a@b.co", Attendees = 3 };
        Assert.Empty(Model.Validate(ok));
    }

    [Fact]
    public void Conditional_fields_constraint_only_fires_while_active()
    {
        var lowPriority = new MeetingRequest { Title = "Sync", Email = "a@b.co", Attendees = 3, Priority = Priority.Low };
        Assert.DoesNotContain(Model.Validate(lowPriority), e => e.Field == "Notes");   // inactive: not required

        var highPriorityNoNotes = new MeetingRequest
        {
            Title = "Sync", Email = "a@b.co", Attendees = 3, Priority = Priority.High,
        };
        Assert.Contains(Model.Validate(highPriorityNoNotes), e => e.Field == "Notes");   // active: required

        var highPriorityWithNotes = new MeetingRequest
        {
            Title = "Sync", Email = "a@b.co", Attendees = 3, Priority = Priority.High, Notes = "Escalation context",
        };
        Assert.Empty(Model.Validate(highPriorityWithNotes));
    }

    [Fact]
    public void Typed_get_set_roundtrips_invariantly()
    {
        MeetingRequest m = new();
        Model.SetString(m, "Attendees", "42");
        Model.SetString(m, "Remote", "true");
        Model.SetString(m, "Priority", "high");   // case-insensitive enum parse

        Assert.Equal(42, m.Attendees);
        Assert.True(m.Remote);
        Assert.Equal(Priority.High, m.Priority);
        Assert.Equal("42", Model.GetString(m, "Attendees"));
        Assert.Equal("High", Model.GetString(m, "Priority"));
    }

    [Fact]
    public void Builds_a_valid_json_schema_tool_definition()
    {
        using JsonDocument doc = JsonDocument.Parse(FormTool.BuildToolDefinition(Model));
        JsonElement root = doc.RootElement;

        Assert.Equal("schedule_meeting", root.GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("description").GetString()));

        JsonElement schema = root.GetProperty("input_schema");
        Assert.Equal("object", schema.GetProperty("type").GetString());

        JsonElement props = schema.GetProperty("properties");
        Assert.Equal("integer", props.GetProperty("Attendees").GetProperty("type").GetString());
        Assert.Equal(1, props.GetProperty("Attendees").GetProperty("minimum").GetInt32());
        Assert.Equal(50, props.GetProperty("Attendees").GetProperty("maximum").GetInt32());
        Assert.Equal("string", props.GetProperty("Title").GetProperty("type").GetString());
        Assert.Equal(80, props.GetProperty("Title").GetProperty("maxLength").GetInt32());

        // Enum surfaces its choices.
        var roles = props.GetProperty("Priority").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("High", roles);

        // Required set carries the unconditionally required fields only -- Notes is
        // conditionally required, expressed via allOf/if/then below instead.
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Title", required);
        Assert.Contains("Email", required);
        Assert.DoesNotContain("Notes", required);

        // The conditional-required clause: Notes is required only when Priority == High.
        JsonElement allOf = schema.GetProperty("allOf");
        Assert.Equal(1, allOf.GetArrayLength());
        JsonElement clause = allOf[0];
        Assert.Equal("High", clause.GetProperty("if").GetProperty("properties").GetProperty("Priority").GetProperty("const").GetString());
        var thenRequired = clause.GetProperty("then").GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Notes", thenRequired);

        // Every function-calling host reads "description", even ones that don't
        // evaluate allOf/if/then -- the conditional clause is also stated there.
        Assert.Contains("Only applicable when Priority is High", props.GetProperty("Notes").GetProperty("description").GetString());
    }

    [Fact]
    public void Ai_tool_call_arguments_fill_and_validate_the_model()
    {
        // What an AI host would pass when it decides to call the tool. Priority is
        // High, so Notes is both active and required -- the AI must supply it too.
        const string toolCall = """
            { "Title": "Sprint sync", "Attendees": 5, "Email": "lead@team.io", "Remote": true,
              "Priority": "High", "Notes": "Escalation context" }
            """;

        MeetingRequest target = new();
        var errors = FormTool.ApplyArguments(Model, target, toolCall);

        Assert.Empty(errors);
        Assert.Equal("Sprint sync", target.Title);
        Assert.Equal(5, target.Attendees);
        Assert.True(target.Remote);
        Assert.Equal(Priority.High, target.Priority);
        Assert.Equal("Escalation context", target.Notes);
    }

    [Fact]
    public void Ai_tool_call_cannot_set_a_conditionally_inactive_field()
    {
        // Priority is Low (the default), so Notes is inactive -- even though the AI
        // supplies it, ApplyArguments must silently skip it (pass 2's activity check),
        // same posture as a Sensitive field. The schema's allOf/if/then is advisory;
        // this is the real enforcement boundary.
        const string toolCall = """{ "Title": "Sync", "Email": "a@b.co", "Notes": "should not be set" }""";

        MeetingRequest target = new();
        FormTool.ApplyArguments(Model, target, toolCall);

        Assert.Null(target.Notes);
    }

    [Fact]
    public void Ai_tool_call_with_bad_arguments_reports_validation_errors()
    {
        MeetingRequest target = new();
        var errors = FormTool.ApplyArguments(Model, target, """{ "Attendees": 0 }""");

        Assert.Contains(errors, e => e.Field == "Title");       // missing required
        Assert.Contains(errors, e => e.Field == "Attendees");   // below minimum
    }

    [Fact]
    public void Malformed_tool_arguments_yield_an_error_not_an_exception()
    {
        var errors = FormTool.ApplyArguments(Model, new MeetingRequest(), "not json");
        Assert.NotEmpty(errors);
    }
}
