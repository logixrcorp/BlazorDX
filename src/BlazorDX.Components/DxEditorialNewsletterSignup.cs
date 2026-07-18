using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// An inline email-capture form for a <see cref="DxEditorialLayout"/> piece — composes the
/// library's own <see cref="DxTextBox"/> and <see cref="DxButton"/>. Ships no backend of its
/// own (there's no newsletter service to wire it to): <see cref="OnSubscribe"/> hands the host
/// application a raw email string to do something real with. Not wired into the flagship
/// article for that reason — a signup form with nowhere real for the email to go would mislead
/// a reader who submits it, which is worse than the honest empty states this project already
/// uses for content it doesn't have yet.
/// </summary>
public sealed class DxEditorialNewsletterSignup : ComponentBase
{
    [Parameter] public string Heading { get; set; } = "Get more like this";

    [Parameter] public string? Description { get; set; }

    [Parameter] public string ButtonText { get; set; } = "Subscribe";

    [Parameter, EditorRequired] public EventCallback<string> OnSubscribe { get; set; }

    private string? email;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-editorial-newsletter");

        builder.OpenElement(2, "p");
        builder.AddAttribute(3, "class", "dx-editorial-newsletter-heading");
        builder.AddContent(4, Heading);
        builder.CloseElement();

        if (!string.IsNullOrEmpty(Description))
        {
            builder.OpenElement(5, "p");
            builder.AddAttribute(6, "class", "dx-editorial-newsletter-description");
            builder.AddContent(7, Description);
            builder.CloseElement();
        }

        builder.OpenElement(8, "form");
        builder.AddAttribute(9, "class", "dx-editorial-newsletter-form");
        builder.AddAttribute(10, "onsubmit", EventCallback.Factory.Create(this, SubmitAsync));
        builder.AddEventPreventDefaultAttribute(11, "onsubmit", true);

        builder.OpenComponent<DxTextBox>(12);
        builder.AddComponentParameter(13, nameof(DxTextBox.Type), "email");
        builder.AddComponentParameter(14, nameof(DxTextBox.Value), email);
        builder.AddComponentParameter(15, nameof(DxTextBox.ValueChanged),
            EventCallback.Factory.Create<string?>(this, v => email = v));
        builder.AddComponentParameter(16, nameof(DxTextBox.Placeholder), "you@example.com");
        builder.AddComponentParameter(17, nameof(DxTextBox.AriaLabel), "Email address");
        builder.AddComponentParameter(18, nameof(DxTextBox.Class), "dx-editorial-newsletter-input");
        builder.CloseComponent();

        builder.OpenComponent<DxButton>(19);
        builder.AddComponentParameter(20, nameof(DxButton.Text), ButtonText);
        builder.AddComponentParameter(21, nameof(DxButton.Type), "submit");
        builder.CloseComponent();

        builder.CloseElement(); // form
        builder.CloseElement(); // .dx-editorial-newsletter
    }

    private Task SubmitAsync() =>
        string.IsNullOrWhiteSpace(email) ? Task.CompletedTask : OnSubscribe.InvokeAsync(email);
}
