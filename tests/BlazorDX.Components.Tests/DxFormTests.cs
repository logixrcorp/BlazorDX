using System.Linq;
using BlazorDX.Components;
using BlazorDX.Primitives.Forms;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>DxForm auto-render, two-way binding, validation/submit, templates, and containers.</summary>
public sealed class DxFormTests : TestContext
{
    private static MeetingRequestFormModel Descriptor() => new();

    private IRenderedComponent<DxForm<MeetingRequest>> RenderForm(
        MeetingRequest model, Action<ComponentParameterCollectionBuilder<DxForm<MeetingRequest>>>? extra = null) =>
        RenderComponent<DxForm<MeetingRequest>>(p =>
        {
            p.Add(f => f.Model, model);
            p.Add(f => f.Descriptor, Descriptor());
            extra?.Invoke(p);
        });

    [Fact]
    public void Auto_renders_an_input_per_field_by_kind()
    {
        // Priority defaults to Low, so Notes (DependsOn = Priority == High) starts inactive
        // and is skipped — 5 of the 6 declared fields render, not 6. See the
        // Conditional_field_* tests below for the active case.
        IRenderedComponent<DxForm<MeetingRequest>> form = RenderForm(new MeetingRequest());

        Assert.Equal(5, form.FindAll(".dx-field").Count);
        Assert.Single(form.FindAll("input[type=number]"));    // Attendees
        Assert.Single(form.FindAll("input[type=checkbox]"));  // Remote
        Assert.Empty(form.FindAll("textarea"));                // Notes (multiline) is inactive
        Assert.Equal(3, form.FindAll("select option").Count); // Priority enum choices
    }

    [Fact]
    public void Conditional_field_is_hidden_until_its_dependency_matches()
    {
        MeetingRequest model = new();
        IRenderedComponent<DxForm<MeetingRequest>> form = RenderForm(model, p => p.Add(f => f.ValidateOnChange, true));

        Assert.Empty(form.FindAll("textarea"));   // Priority starts Low -> Notes inactive

        form.Find("select").Change("High");
        Assert.Single(form.FindAll("textarea"));   // Priority -> High -> Notes appears

        form.Find("select").Change("Low");
        Assert.Empty(form.FindAll("textarea"));   // back to Low -> Notes disappears again
    }

    [Fact]
    public void Conditional_field_via_DxFormField_renders_only_while_active()
    {
        MeetingRequest model = new();
        IRenderedComponent<DxForm<MeetingRequest>> form = RenderForm(model, p =>
            p.Add(f => f.ChildContent, (RenderFragment)(b =>
            {
                b.OpenComponent<DxFormField>(0);
                b.AddComponentParameter(1, nameof(DxFormField.Name), "Notes");
                b.CloseComponent();
            })));

        Assert.Empty(form.FindAll(".dx-field"));   // Priority is Low -> Notes inactive

        model.Priority = Priority.High;
        form.InvokeAsync(() => form.Instance.Refresh());   // model changed from outside the form
        Assert.Single(form.FindAll(".dx-field"));
    }

    [Fact]
    public void Conditional_field_is_validated_only_while_active()
    {
        MeetingRequest lowPriority = new() { Title = "Sync", Email = "a@b.co", Attendees = 3 };
        IRenderedComponent<DxForm<MeetingRequest>> lowForm = RenderForm(lowPriority);
        lowForm.Find("form").Submit();
        Assert.Empty(lowForm.FindAll(".dx-field-error"));   // Notes inactive, empty is fine

        MeetingRequest highPriority = new() { Title = "Sync", Email = "a@b.co", Attendees = 3, Priority = Priority.High };
        IRenderedComponent<DxForm<MeetingRequest>> highForm = RenderForm(highPriority);
        highForm.Find("form").Submit();
        Assert.NotEmpty(highForm.FindAll(".dx-field-error"));   // Notes active and empty -> required error
    }

    [Fact]
    public void Typing_updates_the_model_through_the_generated_setter()
    {
        MeetingRequest model = new();
        IRenderedComponent<DxForm<MeetingRequest>> form = RenderForm(model);

        form.FindAll("input[type=text]")[0].Input("Sprint sync");   // Title is the first text field
        form.Find("input[type=number]").Input("7");

        Assert.Equal("Sprint sync", model.Title);
        Assert.Equal(7, model.Attendees);
    }

    [Fact]
    public void Submitting_invalid_shows_errors_and_skips_valid_callback()
    {
        bool valid = false;
        var invalid = new List<FormValidationError>();
        IRenderedComponent<DxForm<MeetingRequest>> form = RenderForm(new MeetingRequest(), p =>
        {
            p.Add(f => f.OnValidSubmit, EventCallback.Factory.Create<MeetingRequest>(this, _ => valid = true));
            p.Add(f => f.OnInvalidSubmit, EventCallback.Factory.Create<IReadOnlyList<FormValidationError>>(this, e => invalid = e.ToList()));
        });

        form.Find("form").Submit();

        Assert.False(valid);
        Assert.NotEmpty(invalid);
        Assert.NotEmpty(form.FindAll(".dx-field-error"));
    }

    [Fact]
    public void Invalid_field_is_marked_and_linked_to_its_error_message()
    {
        // WCAG 3.3.1: the input is aria-invalid and points, via aria-describedby, at the
        // text error region — so a screen reader announces it when focus returns.
        IRenderedComponent<DxForm<MeetingRequest>> form = RenderForm(new MeetingRequest());
        form.Find("form").Submit();   // empty model -> required fields fail

        var title = form.FindAll("input[type=text]")[0];   // Title is required
        Assert.Equal("true", title.GetAttribute("aria-invalid"));

        string? describedBy = title.GetAttribute("aria-describedby");
        Assert.False(string.IsNullOrEmpty(describedBy));

        var errorRegion = form.Find($"#{describedBy}");
        Assert.Equal("alert", errorRegion.GetAttribute("role"));
        Assert.NotEmpty(errorRegion.TextContent.Trim());
    }

    [Fact]
    public void Valid_field_carries_no_invalid_or_describedby_attributes()
    {
        MeetingRequest model = new() { Title = "Sync", Email = "a@b.co", Attendees = 3 };
        IRenderedComponent<DxForm<MeetingRequest>> form = RenderForm(model);
        form.Find("form").Submit();

        var title = form.FindAll("input[type=text]")[0];
        Assert.False(title.HasAttribute("aria-invalid"));
        Assert.False(title.HasAttribute("aria-describedby"));
    }

    [Fact]
    public void Submitting_valid_calls_the_valid_callback_with_the_model()
    {
        MeetingRequest model = new() { Title = "Sync", Email = "a@b.co", Attendees = 3 };
        MeetingRequest? submitted = null;
        IRenderedComponent<DxForm<MeetingRequest>> form = RenderForm(model, p =>
            p.Add(f => f.OnValidSubmit, EventCallback.Factory.Create<MeetingRequest>(this, m => submitted = m)));

        form.Find("form").Submit();

        Assert.Same(model, submitted);
        Assert.Empty(form.FindAll(".dx-field-error"));
    }

    [Fact]
    public void Input_template_overrides_the_default_control()
    {
        IRenderedComponent<DxForm<MeetingRequest>> form = RenderForm(new MeetingRequest(), p =>
            p.Add(f => f.InputTemplate, (RenderFragment<FormFieldRenderContext>)(ctx => b =>
            {
                b.OpenElement(0, "input");
                b.AddAttribute(1, "class", "my-custom-input");
                b.AddAttribute(2, "data-field", ctx.Field.Name);
                b.CloseElement();
            })));

        // 5, not 6: Priority defaults to Low, so Notes (conditional on Priority == High)
        // is inactive and skipped, same as the auto-render case.
        Assert.Equal(5, form.FindAll("input.my-custom-input").Count);
        Assert.Empty(form.FindAll("textarea"));   // default multiline control was replaced
    }

    [Fact]
    public void Child_content_with_DxFormField_renders_only_chosen_fields()
    {
        MeetingRequest model = new();
        IRenderedComponent<DxForm<MeetingRequest>> form = RenderForm(model, p =>
            p.Add(f => f.ChildContent, (RenderFragment)(b =>
            {
                b.OpenComponent<DxFormField>(0);
                b.AddComponentParameter(1, nameof(DxFormField.Name), "Title");
                b.CloseComponent();
            })));

        Assert.Single(form.FindAll(".dx-field"));   // only Title, not all six
        form.Find("input[type=text]").Input("Hello");
        Assert.Equal("Hello", model.Title);
    }

    [Fact]
    public void DxFormSection_renders_a_fieldset_and_collapses()
    {
        IRenderedComponent<DxFormSection> section = RenderComponent<DxFormSection>(p =>
        {
            p.Add(s => s.Title, "Details");
            p.Add(s => s.Collapsible, true);
            p.AddChildContent("<p class=\"inside\">body</p>");
        });

        Assert.Single(section.FindAll("fieldset legend"));
        Assert.Single(section.FindAll(".inside"));

        section.Find(".dx-form-section-toggle").Click();   // collapse
        Assert.Empty(section.FindAll(".inside"));
    }

    [Fact]
    public void DxFormGrid_sets_the_column_count()
    {
        IRenderedComponent<DxFormGrid> grid = RenderComponent<DxFormGrid>(p =>
        {
            p.Add(g => g.Columns, 3);
            p.AddChildContent("<span>x</span>");
        });

        Assert.Contains("--dx-form-cols:3", grid.Find(".dx-form-grid").GetAttribute("style"));
    }
}
