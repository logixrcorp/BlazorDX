using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorDX.Security;

/// <summary>
/// The sanctioned way to register UI state. State is always scoped, never a
/// Singleton: on Blazor Server a Singleton is shared across every connected
/// circuit, leaking one user's data into another's session. The DX1002 analyzer
/// flags Singleton state registrations and points here.
/// </summary>
public static class ScopedStateServiceCollectionExtensions
{
    /// <summary>Registers <typeparamref name="TState"/> with a scoped lifetime.</summary>
    public static IServiceCollection AddScopedState<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TState>(
        this IServiceCollection services)
        where TState : class
    {
        return services.AddScoped<TState>();
    }
}
