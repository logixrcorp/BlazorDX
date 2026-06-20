using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorDX.Primitives.Diagnostics;

/// <summary>Registers a BlazorDX diagnostics sink. Scoped, never Singleton.</summary>
public static class DiagnosticsServiceCollectionExtensions
{
    /// <summary>Routes every BlazorDX diagnostic to the app's <c>ILogger</c> (the zero-config default).</summary>
    public static IServiceCollection AddBlazorDXDiagnostics(this IServiceCollection services)
    {
        services.AddScoped<IDxDiagnostics, LoggerDxDiagnostics>();
        return services;
    }

    /// <summary>Reports every diagnostic to <paramref name="sink"/> (e.g. an <c>ILogger</c> call).</summary>
    public static IServiceCollection AddBlazorDXDiagnostics(this IServiceCollection services, Action<DiagnosticEvent> sink)
    {
        services.AddScoped<IDxDiagnostics>(_ => new DelegateDxDiagnostics(sink));
        return services;
    }

    /// <summary>Registers a custom <see cref="IDxDiagnostics"/> implementation.</summary>
    public static IServiceCollection AddBlazorDXDiagnostics<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(this IServiceCollection services)
        where TImplementation : class, IDxDiagnostics
    {
        services.AddScoped<IDxDiagnostics, TImplementation>();
        return services;
    }
}
