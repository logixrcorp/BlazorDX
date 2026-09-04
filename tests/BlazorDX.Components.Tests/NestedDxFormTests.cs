using BlazorDX.Components;
using BlazorDX.Primitives.Forms;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// DxForm rendering for Object/Array-kind fields: a nested object renders inside a
/// DxFormSection wrapping a nested DxForm; an array renders a DxFieldList with
/// add/remove/reorder; array-of-nested rows are the nested-object path repeated per
/// element — including the "hardest case" combination, exercised end-to-end below.
/// </summary>
public sealed class NestedDxFormTests : TestContext
{
    private static MeetingWithAttendeesFormModel Descriptor() => new();

    private IRenderedComponent<DxForm<MeetingWithAttendees>> RenderForm(MeetingWithAttendees model) =>
        RenderComponent<DxForm<MeetingWithAttendees>>(p =>
        {
            p.Add(f => f.Model, model);
            p.Add(f => f.Descriptor, Descriptor());
        });

    [Fact]
    public void Nested_object_field_renders_inside_a_form_section_without_a_nested_form_element()
    {
        MeetingWithAttendees model = new() { Title = "Sync" };
        IRenderedComponent<DxForm<MeetingWithAttendees>> form = RenderForm(model);

        Assert.Single(form.FindAll("fieldset.dx-form-section"));

        // A <form> cannot legally nest inside another <form> -- the nested sub-form
        // must render as a <div>, not a second <form> element.
        Assert.Single(form.FindAll("form"));
        Assert.Single(form.FindAll("div.dx-form-nested"));

        // Title (outer) + Street + City (nested) = 3 text inputs total.
        Assert.Equal(3, form.FindAll("input[type=text]").Count);
    }

    [Fact]
    public void Array_fields_each_render_their_own_field_list_with_an_add_button()
    {
        MeetingWithAttendees model = new()
        {
            Title = "Sync",
            Attendees = { new Attendee { Name = "Ada", Email = "ada@x.co" } },
        };
        IRenderedComponent<DxForm<MeetingWithAttendees>> form = RenderForm(model);

        // Declared order: Attendees before Tags, so index 0 is Attendees' list.
        Assert.Equal(2, form.FindAll(".dx-fieldlist").Count);
        Assert.Equal(2, form.FindAll(".dx-fieldlist-add").Count);
        Assert.Single(form.FindAll(".dx-fieldlist")[0].FindAll("[role=listitem]"));
    }

    [Fact]
    public void Adding_a_row_appends_a_new_nested_instance()
    {
        MeetingWithAttendees model = new() { Title = "Sync" };
        IRenderedComponent<DxForm<MeetingWithAttendees>> form = RenderForm(model);

        form.FindAll(".dx-fieldlist-add")[0].Click();   // Attendees' add button

        Assert.Single(model.Attendees);
        Assert.IsType<Attendee>(model.Attendees[0]);
    }

    [Fact]
    public void Removing_a_row_deletes_it()
    {
        MeetingWithAttendees model = new()
        {
            Title = "Sync",
            Attendees =
            {
                new Attendee { Name = "Ada", Email = "ada@x.co" },
                new Attendee { Name = "Lin", Email = "lin@x.co" },
            },
        };
        IRenderedComponent<DxForm<MeetingWithAttendees>> form = RenderForm(model);

        form.FindAll(".dx-fieldlist")[0].Find(".dx-fieldlist-remove").Click();

        Assert.Single(model.Attendees);
    }

    [Fact]
    public void Reordering_an_array_of_nested_row_with_alt_arrow_moves_it_and_keeps_reference_identity()
    {
        Attendee ada = new() { Name = "Ada", Email = "ada@x.co" };
        Attendee lin = new() { Name = "Lin", Email = "lin@x.co" };
        MeetingWithAttendees model = new() { Title = "Sync", Attendees = { ada, lin } };
        IRenderedComponent<DxForm<MeetingWithAttendees>> form = RenderForm(model);

        form.FindAll(".dx-fieldlist")[0].FindAll("[role=listitem]")[0]
            .KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        Assert.Same(lin, model.Attendees[0]);
        Assert.Same(ada, model.Attendees[1]);
    }

    [Fact]
    public void Scalar_array_renders_one_row_per_item()
    {
        MeetingWithAttendees model = new() { Title = "Sync", Tags = { "eng", "design" } };
        IRenderedComponent<DxForm<MeetingWithAttendees>> form = RenderForm(model);

        Assert.Equal(2, form.FindAll(".dx-fieldlist")[1].FindAll("[role=listitem]").Count);
    }

    [Fact]
    public void Editing_a_scalar_array_row_updates_the_model()
    {
        MeetingWithAttendees model = new() { Title = "Sync", Tags = { "eng" } };
        IRenderedComponent<DxForm<MeetingWithAttendees>> form = RenderForm(model);

        form.FindAll(".dx-fieldlist")[1].Find("input[type=text]").Input("design");

        Assert.Equal("design", model.Tags[0]);
    }

    [Fact]
    public void Filling_a_new_nested_row_through_its_rendered_sub_form_reflects_by_reference_identity_and_submits_clean()
    {
        // The hardest combination case: add an Attendee row, fill its nested fields
        // through the actually-rendered nested DxForm<Attendee>, then submit the
        // outer form and confirm the edit landed on the model by reference identity
        // (no write-back step) and validation passes end-to-end.
        MeetingWithAttendees model = new()
        {
            Title = "Sync",
            Location = new Address { Street = "1 Main St", City = "Springfield" },
        };
        IRenderedComponent<DxForm<MeetingWithAttendees>> form = RenderForm(model);

        form.FindAll(".dx-fieldlist-add")[0].Click();   // add an Attendee row
        Assert.Single(model.Attendees);

        var attendeesList = form.FindAll(".dx-fieldlist")[0];
        var rowInputs = attendeesList.FindAll("input[type=text]");
        Assert.Equal(2, rowInputs.Count);   // Name, Email

        rowInputs[0].Input("Ada");
        rowInputs[1].Input("ada@x.co");

        Assert.Equal("Ada", model.Attendees[0].Name);
        Assert.Equal("ada@x.co", model.Attendees[0].Email);

        form.Find("form").Submit();
        Assert.Empty(form.FindAll(".dx-field-error"));
    }
}
