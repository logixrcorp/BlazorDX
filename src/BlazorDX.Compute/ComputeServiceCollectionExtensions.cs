using BlazorDX.Interop;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorDX.Compute;

/// <summary>
/// Registration for the grid compute services. The backend is chosen by the
/// runtime: Rust/wasm in the browser, managed C# elsewhere. Services are scoped,
/// never Singleton, in keeping with the BlazorDX state-isolation rule.
/// </summary>
public static class ComputeServiceCollectionExtensions
{
    public static IServiceCollection AddBlazorDXCompute(this IServiceCollection services)
    {
        services.AddBlazorDXInterop();

        if (OperatingSystem.IsBrowser())
        {
            services.AddScoped<IGridCompute, RustGridCompute>();
        }
        else
        {
            services.AddScoped<IGridCompute, ManagedGridCompute>();
        }

        return services;
    }
}
