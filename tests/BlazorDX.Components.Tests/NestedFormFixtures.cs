using BlazorDX.Primitives.Forms;

namespace BlazorDX.Components.Tests;

[DxFormModel(Name = "office_location")]
public sealed class Address
{
    [DxField("Street", Required = true)]
    public string Street { get; set; } = string.Empty;

    [DxField("City", Required = true)]
    public string City { get; set; } = string.Empty;
}

[DxFormModel(Name = "attendee_info")]
public sealed class Attendee
{
    [DxField("Name", Required = true)]
    public string Name { get; set; } = string.Empty;

    [DxField("Email", Required = true, Pattern = @"^[^@\s]+@[^@\s]+$")]
    public string Email { get; set; } = string.Empty;
}

/// <summary>Nested object (Location), array-of-nested (Attendees), and array-of-scalar (Tags) in one model.</summary>
[DxFormModel(Name = "schedule_meeting_with_attendees")]
public sealed class MeetingWithAttendees
{
    [DxField("Title", Required = true)]
    public string Title { get; set; } = string.Empty;

    [DxField("Location")]
    public Address Location { get; set; } = new();

    [DxField("Attendees", Required = true)]
    public List<Attendee> Attendees { get; set; } = new();

    [DxField("Tags")]
    public List<string> Tags { get; set; } = new();
}

/// <summary>A minimal fixture for the Object-field "required and currently null" case — needs a nullable-typed nested property, unlike the always-materialized <see cref="MeetingWithAttendees.Location"/>.</summary>
[DxFormModel(Name = "required_location_holder")]
public sealed class RequiredLocationHolder
{
    [DxField("Location", Required = true)]
    public Address? Location { get; set; }
}
