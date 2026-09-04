using BlazorDX.Components;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Components whose user-facing text arrives as a <c>[Parameter]</c> with an English default —
/// <c>DxSpinner.Label</c>, <c>DxSkipLink.Text</c>, <c>DxErrorBoundary.FallbackTitle</c>, and
/// fifteen more.
/// </summary>
/// <remarks>
/// <para>
/// These could not follow the ordinary pattern. A property initializer cannot call an instance
/// member, so <c>= S["Loading", "Loading"]</c> does not compile — the localizer is reached
/// through <c>IServiceProvider</c>, which only exists once the component is activated.
/// </para>
/// <para>
/// So the parameter defaults to <see langword="null"/> and the render site coalesces. That is a
/// visible change to the public surface (<c>string</c> became <c>string?</c>), and it has exactly
/// two behaviours worth pinning: a caller who supplies a value must still win outright, and a
/// caller who supplies nothing must get localized text rather than an empty attribute. The second
/// is the one a regression would silently break — an empty <c>aria-label</c> renders as a control
/// with no accessible name, and the component still looks correct on screen.
/// </para>
/// </remarks>
public sealed class DefaultedParameterLocalizationTests : TestContext
{
    [Fact]
    public void An_unset_parameter_falls_back_to_English_with_no_localizer()
    {
        IRenderedComponent<DxSpinner> spinner = RenderComponent<DxSpinner>();

        Assert.Equal("Loading", spinner.Find("[aria-label]").GetAttribute("aria-label"));
    }

    [Fact]
    public void An_unset_parameter_routes_through_the_localizer_when_one_is_registered()
    {
        // The sentinel is what distinguishes "coalesced to the localizer" from "coalesced to a
        // hardcoded literal that happens to match the English".
        Services.AddSingleton<IStringLocalizer<DxSpinner>>(new FakeStringLocalizer<DxSpinner>());

        IRenderedComponent<DxSpinner> spinner = RenderComponent<DxSpinner>();

        Assert.Equal("§§LOADING§§", spinner.Find("[aria-label]").GetAttribute("aria-label"));
    }

    [Fact]
    public void A_caller_supplied_value_still_wins_over_the_localized_default()
    {
        // The compatibility half. Someone who set Label before this change must see no
        // difference, localizer registered or not.
        Services.AddSingleton<IStringLocalizer<DxSpinner>>(new FakeStringLocalizer<DxSpinner>());

        IRenderedComponent<DxSpinner> spinner = RenderComponent<DxSpinner>(p => p
            .Add(c => c.Label, "Fetching orders"));

        Assert.Equal("Fetching orders", spinner.Find("[aria-label]").GetAttribute("aria-label"));
    }

    [Fact]
    public void Visible_text_defaults_are_localized_the_same_way()
    {
        // DxSkipLink renders its default as content rather than an attribute — the first thing a
        // keyboard user reaches on the page, so an empty default would be a real accessibility
        // failure rather than a cosmetic one.
        Services.AddSingleton<IStringLocalizer<DxSkipLink>>(new FakeStringLocalizer<DxSkipLink>());

        IRenderedComponent<DxSkipLink> link = RenderComponent<DxSkipLink>();

        Assert.Contains("§§SKIPTOMAINCONTENT§§", link.Markup);
    }

    [Fact]
    public void A_component_with_two_defaulted_parameters_resolves_each_independently()
    {
        Services.AddSingleton<IStringLocalizer<DxEditorialNewsletterSignup>>(
            new FakeStringLocalizer<DxEditorialNewsletterSignup>());

        IRenderedComponent<DxEditorialNewsletterSignup> signup =
            RenderComponent<DxEditorialNewsletterSignup>(p => p.Add(c => c.Heading, "Stay in touch"));

        // One supplied, one defaulted: the supplied value must not suppress the other's fallback.
        Assert.Contains("Stay in touch", signup.Markup);
        Assert.Contains("§§SUBSCRIBE§§", signup.Markup);
    }
}
