using BlazorDX.Components;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + navigation behavior for the stepper.</summary>
public sealed class DxStepperTests : TestContext
{
    private static IReadOnlyList<StepItem> BuildSteps() =>
    [
        new StepItem("One", b => b.AddContent(0, "First content")),
        new StepItem("Two", b => b.AddContent(0, "Second content")),
        new StepItem("Three", b => b.AddContent(0, "Third content")),
    ];

    [Fact]
    public void Shows_the_current_step_content_and_marks_the_indicator()
    {
        IRenderedComponent<DxStepper> stepper = RenderComponent<DxStepper>(parameters => parameters
            .Add(s => s.Steps, BuildSteps())
            .Add(s => s.Current, 0));

        Assert.Contains("First content", stepper.Markup);
        Assert.DoesNotContain("Second content", stepper.Markup);
        Assert.Equal("step", stepper.Find(".dx-stepper-current .dx-stepper-trigger").GetAttribute("aria-current"));
    }

    [Fact]
    public void Next_advances_the_current_step()
    {
        int current = 0;
        IRenderedComponent<DxStepper> stepper = RenderComponent<DxStepper>(parameters => parameters
            .Add(s => s.Steps, BuildSteps())
            .Add(s => s.Current, current)
            .Add(s => s.CurrentChanged, value => current = value));

        stepper.Find(".dx-stepper-next").Click();
        Assert.Equal(1, current);
    }

    [Fact]
    public void Back_is_disabled_on_the_first_step()
    {
        IRenderedComponent<DxStepper> stepper = RenderComponent<DxStepper>(parameters => parameters
            .Add(s => s.Steps, BuildSteps())
            .Add(s => s.Current, 0));

        Assert.True(stepper.Find(".dx-stepper-back").HasAttribute("disabled"));
        Assert.False(stepper.Find(".dx-stepper-next").HasAttribute("disabled"));
    }

    [Fact]
    public void Next_is_disabled_on_the_last_step()
    {
        IRenderedComponent<DxStepper> stepper = RenderComponent<DxStepper>(parameters => parameters
            .Add(s => s.Steps, BuildSteps())
            .Add(s => s.Current, 2));

        Assert.True(stepper.Find(".dx-stepper-next").HasAttribute("disabled"));
    }

    [Fact]
    public void Clicking_a_step_indicator_navigates_to_it()
    {
        int current = 0;
        IRenderedComponent<DxStepper> stepper = RenderComponent<DxStepper>(parameters => parameters
            .Add(s => s.Steps, BuildSteps())
            .Add(s => s.Current, current)
            .Add(s => s.CurrentChanged, value => current = value));

        stepper.FindAll(".dx-stepper-trigger")[2].Click();
        Assert.Equal(2, current);
    }
}
