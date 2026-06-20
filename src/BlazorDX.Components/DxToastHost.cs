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

        foreach (Toast toast in Toasts.Toasts)
        {
            builder.OpenElement(2, "div");
            builder.SetKey(toast.Id);
            builder.AddAttribute(3, "class", $"dx-toast dx-toast-{toast.Severity}");
            builder.AddAttribute(4, "role", "status");

            builder.OpenElement(5, "span");
            builder.AddAttribute(6, "class", "dx-toast-message");
            builder.AddContent(7, toast.Message);
            builder.CloseElement();

            string capturedId = toast.Id;
            builder.OpenElement(8, "button");
            builder.AddAttribute(9, "type", "button");
            builder.AddAttribute(10, "class", "dx-toast-close");
            builder.AddAttribute(11, "aria-label", "Dismiss");
            builder.AddAttribute(12, "onclick", EventCallback.Factory.Create(this, () => Toasts.Remove(capturedId)));
            builder.AddContent(13, "✕");
            builder.CloseElement();

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    public void Dispose() => Toasts.OnChange -= OnToastsChanged;
}
