using BlazorDX.Primitives.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Contains exceptions thrown while rendering its content so one broken component
/// doesn't take down the page. On an error it reports to <see cref="IDxDiagnostics"/>
/// (resolved optionally — no-op if none is registered), raises <see cref="OnError"/>,
/// and shows a fallback: your <c>ErrorContent</c> template, or a styled default with a
/// Retry button. Builds on the framework's <see cref="ErrorBoundaryBase"/>.
/// </summary>
public sealed class DxErrorBoundary : ErrorBoundaryBase
{
    [Inject] private IServiceProvider Services { get; set; } = default!;

    /// <summary>Raised with the caught exception (after it is reported to diagnostics).</summary>
    [Parameter] public EventCallback<Exception> OnError { get; set; }

    /// <summary>Heading shown by the default fallback.</summary>
    [Parameter] public string FallbackTitle { get; set; } = "Something went wrong.";

    /// <summary>Show the exception message in the default fallback (use only in development).</summary>
    [Parameter] public bool ShowDetail { get; set; }

    /// <summary>Label for the default fallback's recover button.</summary>
    [Parameter] public string RetryText { get; set; } = "Retry";

    /// <summary>Extra CSS classes appended to the default fallback.</summary>
    [Parameter] public string? Class { get; set; }

    protected override async Task OnErrorAsync(Exception exception)
    {
        (Services.GetService(typeof(IDxDiagnostics)) as IDxDiagnostics)
            .TryReportError("DxErrorBoundary", exception.Message, exception);

        if (OnError.HasDelegate)
        {
            await OnError.InvokeAsync(exception);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (CurrentException is null)
        {
            builder.AddContent(0, ChildContent);
            return;
        }

        if (ErrorContent is not null)
        {
            builder.AddContent(1, ErrorContent(CurrentException));
            return;
        }

        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", $"dx-errorboundary {Class}".TrimEnd());
        builder.AddAttribute(4, "role", "alert");

        builder.OpenElement(5, "span");
        builder.AddAttribute(6, "class", "dx-errorboundary-title");
        builder.AddContent(7, FallbackTitle);
        builder.CloseElement();

        if (ShowDetail)
        {
            builder.OpenElement(8, "pre");
            builder.AddAttribute(9, "class", "dx-errorboundary-detail");
            builder.AddContent(10, CurrentException.Message);
            builder.CloseElement();
        }

        builder.OpenElement(11, "button");
        builder.AddAttribute(12, "type", "button");
        builder.AddAttribute(13, "class", "dx-errorboundary-retry");
        builder.AddAttribute(14, "onclick", EventCallback.Factory.Create(this, Recover));
        builder.AddContent(15, RetryText);
        builder.CloseElement();

        builder.CloseElement();
    }
}
