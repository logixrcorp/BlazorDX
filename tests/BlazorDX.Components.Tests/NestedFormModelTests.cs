using System.Linq;
using System.Text.Json;
using BlazorDX.Primitives.Forms;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Array/nested-object fields: generated metadata, IFormModelUntyped accessors,
/// recursive validation, AI schema, and recursive ApplyArguments. The existing
/// scalar-only <see cref="MeetingRequest"/>/<see cref="FormModelTests"/> are left
/// completely untouched — their absence of any diff is the regression proof.
/// </summary>
public sealed class NestedFormModelTests
{
    private static readonly MeetingWithAttendeesFormModel Model = new();

    private static FormFieldInfo Field(string name) => Model.Fields.First(f => f.Name == name);

    [Fact]
    public void Generates_object_and_array_field_metadata()
    {
        Assert.Equal(4, Model.Fields.Count);

        FormFieldInfo location = Field("Location");
        Assert.Equal(FormFieldKind.Object, location.Kind);
        Assert.Equal(typeof(Address), location.NestedType);
        Assert.Null(location.ArrayElementKind);

        FormFieldInfo attendees = Field("Attendees");
        Assert.Equal(FormFieldKind.Array, attendees.Kind);
        Assert.Equal(typeof(Attendee), attendees.NestedType);
        Assert.Null(attendees.ArrayElementKind);
        Assert.True(attendees.Required);

        FormFieldInfo tags = Field("Tags");
        Assert.Equal(FormFieldKind.Array, tags.Kind);
        Assert.Null(tags.NestedType);
        Assert.Equal(FormFieldKind.Text, tags.ArrayElementKind);
    }

    [Fact]
    public void Nested_instance_round_trips()
    {
        MeetingWithAttendees model = new();
        Assert.NotNull(Model.GetNestedInstance(model, "Location"));   // default = new()

        Address address = new() { Street = "1 Main St", City = "Springfield" };
        Model.SetNestedInstance(model, "Location", address);

        Assert.Same(address, model.Location);
        Assert.Same(address, Model.GetNestedInstance(model, "Location"));
    }

    [Fact]
    public void Nested_object_required_and_null_reports_a_single_top_level_error()
    {
        RequiredLocationHolderFormModel model = new();
        var errors = model.Validate(new RequiredLocationHolder());   // Location left null

        Assert.Contains(errors, e => e.Field == "Location");
        Assert.DoesNotContain(errors, e => e.Field == "Location.Street");   // never recurses into a null instance
    }

    [Fact]
    public void Nested_validation_prefixes_the_field_path()
    {
        MeetingWithAttendees model = new()
        {
            Title = "Sync",
            Location = new Address(),   // Street/City both empty
            Attendees = { new Attendee { Name = "Ada", Email = "ada@x.co" } },
        };

        var errors = Model.Validate(model);

        Assert.Contains(errors, e => e.Field == "Location.Street");
        Assert.Contains(errors, e => e.Field == "Location.City");
    }

    [Fact]
    public void Array_of_nested_validation_uses_indexed_paths()
    {
        MeetingWithAttendees model = new()
        {
            Title = "Sync",
            Location = new Address { Street = "1 Main St", City = "Springfield" },
            Attendees = { new Attendee { Name = "Ada" } },   // Email missing
        };

        var errors = Model.Validate(model);

        Assert.Contains(errors, e => e.Field == "Attendees[0].Email");
    }

    [Fact]
    public void Required_array_reports_a_list_level_error_when_empty()
    {
        MeetingWithAttendees model = new()
        {
            Title = "Sync",
            Location = new Address { Street = "1 Main St", City = "Springfield" },
        };   // Attendees left empty -- Required = true

        var errors = Model.Validate(model);

        Assert.Contains(errors, e => e.Field == "Attendees");
    }

    [Fact]
    public void Array_strings_round_trip()
    {
        MeetingWithAttendees model = new();
        Model.SetArrayStrings(model, "Tags", new[] { "eng", "design" });

        Assert.Equal(new[] { "eng", "design" }, model.Tags);
        Assert.Equal(new[] { "eng", "design" }, Model.GetArrayStrings(model, "Tags"));
    }

    [Fact]
    public void Array_instances_round_trip()
    {
        MeetingWithAttendees model = new();
        Attendee ada = new() { Name = "Ada", Email = "ada@x.co" };
        Model.SetArrayInstances(model, "Attendees", new object[] { ada });

        Assert.Same(ada, model.Attendees[0]);
        Assert.Same(ada, Model.GetArrayInstances(model, "Attendees")[0]);
    }

    [Fact]
    public void New_array_element_constructs_a_nested_instance_or_a_blank_scalar()
    {
        Assert.IsType<Attendee>(Model.NewArrayElement("Attendees"));
        Assert.Equal(string.Empty, Model.NewArrayElement("Tags"));
    }

    [Fact]
    public void Builds_nested_object_and_array_json_schema()
    {
        using JsonDocument doc = JsonDocument.Parse(FormTool.BuildInputSchema(Model));
        JsonElement root = doc.RootElement;
        JsonElement props = root.GetProperty("properties");

        JsonElement location = props.GetProperty("Location");
        Assert.Equal("object", location.GetProperty("type").GetString());
        Assert.True(location.GetProperty("properties").TryGetProperty("Street", out _));
        var locationRequired = location.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Street", locationRequired);
        Assert.Contains("City", locationRequired);

        JsonElement attendees = props.GetProperty("Attendees");
        Assert.Equal("array", attendees.GetProperty("type").GetString());
        JsonElement attendeeItems = attendees.GetProperty("items");
        Assert.Equal("object", attendeeItems.GetProperty("type").GetString());
        Assert.True(attendeeItems.GetProperty("properties").TryGetProperty("Email", out _));

        JsonElement tags = props.GetProperty("Tags");
        Assert.Equal("array", tags.GetProperty("type").GetString());
        Assert.Equal("string", tags.GetProperty("items").GetProperty("type").GetString());

        var required = root.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Title", required);
        Assert.Contains("Attendees", required);
    }

    [Fact]
    public void Ai_tool_call_populates_nested_object_and_array_of_nested()
    {
        const string toolCall = """
            { "Title": "Kickoff",
              "Location": { "Street": "1 Main St", "City": "Springfield" },
              "Attendees": [ { "Name": "Ada", "Email": "ada@x.co" }, { "Name": "Lin", "Email": "lin@x.co" } ],
              "Tags": ["eng", "design"] }
            """;

        MeetingWithAttendees target = new();
        var errors = FormTool.ApplyArguments(Model, target, toolCall);

        Assert.Empty(errors);
        Assert.Equal("1 Main St", target.Location.Street);
        Assert.Equal("Springfield", target.Location.City);
        Assert.Equal(2, target.Attendees.Count);
        Assert.Equal("Ada", target.Attendees[0].Name);
        Assert.Equal("ada@x.co", target.Attendees[0].Email);
        Assert.Equal(new[] { "eng", "design" }, target.Tags);
    }

    [Fact]
    public void Ai_tool_call_reports_nested_validation_errors_with_indexed_paths()
    {
        const string toolCall = """
            { "Title": "Kickoff",
              "Location": { "Street": "1 Main St", "City": "Springfield" },
              "Attendees": [ { "Name": "Ada", "Email": "ada@x.co" }, { "Name": "Lin", "Email": "not-an-email" } ] }
            """;

        MeetingWithAttendees target = new();
        var errors = FormTool.ApplyArguments(Model, target, toolCall);

        Assert.Contains(errors, e => e.Field == "Attendees[1].Email");
    }

    [Fact]
    public void Ai_tool_call_replaces_the_whole_array_rather_than_merging()
    {
        MeetingWithAttendees target = new();
        FormTool.ApplyArguments(Model, target, """
            { "Title": "Kickoff", "Attendees": [ { "Name": "Ada", "Email": "ada@x.co" }, { "Name": "Lin", "Email": "lin@x.co" } ] }
            """);
        Assert.Equal(2, target.Attendees.Count);

        FormTool.ApplyArguments(Model, target, """{ "Title": "Kickoff", "Attendees": [ { "Name": "Ada", "Email": "ada@x.co" } ] }""");

        Assert.Single(target.Attendees);   // whole-collection replacement, not a merge
        Assert.Equal("Ada", target.Attendees[0].Name);
    }
}
