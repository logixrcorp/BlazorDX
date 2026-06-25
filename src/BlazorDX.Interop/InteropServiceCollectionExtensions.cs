using Microsoft.Extensions.DependencyInjection;

namespace BlazorDX.Interop;

/// <summary>
/// Registers the browser bridges with platform-appropriate implementations:
/// the real Rust/DOM bridges under WebAssembly, no-op/absent ones elsewhere.
/// All registrations are scoped, never Singleton.
/// </summary>
public static class InteropServiceCollectionExtensions
{
    public static IServiceCollection AddBlazorDXInterop(this IServiceCollection services)
    {
        if (OperatingSystem.IsBrowser())
        {
            services.AddScoped<IGridWasmInterop, GridWasmInterop>();
            services.AddScoped<IGridDomInterop, GridDomInterop>();
            services.AddScoped<IOverlayInterop, OverlayInterop>();
            services.AddScoped<IAnchorInterop, AnchorInterop>();
            services.AddScoped<IRichTextInterop, RichTextInterop>();
            services.AddScoped<IHotkeyInterop, HotkeyInterop>();
            services.AddScoped<IImageEditorInterop, ImageEditorInterop>();
            services.AddScoped<IDocumentViewerInterop, DocumentViewerInterop>();
        }
        else
        {
            // No DOM and no wasm runtime off-browser; the DOM/overlay/anchor/richtext
            // bridges are no-ops and the wasm bridge is simply not registered (the
            // managed compute fallback is used instead).
            services.AddScoped<IGridDomInterop, NullGridDomInterop>();
            services.AddScoped<IOverlayInterop, NullOverlayInterop>();
            services.AddScoped<IAnchorInterop, NullAnchorInterop>();
            services.AddScoped<IRichTextInterop, NullRichTextInterop>();
            services.AddScoped<IHotkeyInterop, NullHotkeyInterop>();
            services.AddScoped<IImageEditorInterop, NullImageEditorInterop>();
            services.AddScoped<IDocumentViewerInterop, NullDocumentViewerInterop>();
        }

        return services;
    }
}
