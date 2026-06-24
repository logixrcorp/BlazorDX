using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Renders the active toasts from the <see cref="ToastService"/> in a fixed corner.
/// Place one near the app root. Styling is CSS-variable driven (see dx-layout.css).
/// </summary>
public sealed class DxToastHost : ComponentBase, IDisposable
{
    [Inject] private ToastService Toasts { get; set; } = default!;

    protected override void OnInitialized() => Toasts.OnChange += OnToastsChanged;

    private void OnToastsChanged() => _ = InvokeAsync(StateHasChanged);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-toast-host");
        // Pause auto-dismiss while the user hovers or keyboard-focuses the region, so a
        // timed toast cannot vanish mid-read; resume when they leave (WCAG 2.2.1).
        builder.AddAttribute(2, "onmouseenter", EventCallback.Factory.Create(this, Toasts.PauseAll));
        builder.AddAttribute(3, "onmouseleave", EventCallback.Factory.Create(this, Toasts.ResumeAll));
        builder.AddAttribute(4, "onfocusin", EventCallback.Factory.Create(this, Toasts.PauseAll));
        builder.AddAttribute(5, "onfocusout", EventCallback.Factory.Create(this, Toasts.ResumeAll));

        foreach (Toast toast in Toasts.Toasts)
        {
            builder.OpenElement(6, "div");
            builder.SetKey(toast.Id);
            builder.AddAttribute(7, "class", $"dx-toast dx-toast-{toast.Severity}");
            builder.AddAttribute(8, "role", "status");

            builder.OpenElement(9, "span");
            builder.AddAttribute(10, "class", "dx-toast-message");
            builder.AddContent(11, toast.Message);
            builder.CloseElement();

            string capturedId = toast.Id;
            builder.OpenElement(12, "button");
            builder.AddAttribute(13, "type", "button");
            builder.AddAttribute(14, "class", "dx-toast-close");
            builder.AddAttribute(15, "aria-label", "Dismiss");
            builder.AddAttribute(16, "onclick", EventCallback.Factory.Create(this, () => Toasts.Remove(capturedId)));
            builder.AddContent(17, "✕");
            builder.CloseElement();

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    public void Dispose() => Toasts.OnChange -= OnToastsChanged;
}
