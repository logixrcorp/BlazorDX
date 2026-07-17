using Microsoft.AspNetCore.Http;

namespace BlazorDX.Conduit;

/// <summary>
/// Decides whether the current caller may open (or deliver a payload into) the ephemeral SSE
/// stream for a given session id. A host implements this over its own auth (e.g. an ASP.NET
/// Core <c>ClaimsPrincipal</c> plus a session-ownership check, or a shared-secret check for the
/// external MCP resource-provider itself) — this mirrors <c>IAiToolAuthorizer</c>
/// (BlazorDX.Primitives.Forms): BlazorDX defines the seam, the host wires the real auth.
///
/// <see cref="EphemeralEventsEndpoint"/> consults it before registering a connection, and
/// <see cref="McpProxyEndpoint"/> consults it before accepting a payload delivery, so a caller
/// who does not own <c>sessionId</c> never sees, and never injects, that session's events. When
/// no authorizer is registered in DI, every session id is allowed — wire a real one before
/// deploying past a local demo.
/// </summary>
public interface IEphemeralSessionAuthorizer
{
    /// <summary>
    /// Returns <c>true</c> if the caller behind <paramref name="context"/> may connect to (or
    /// deliver into) <paramref name="sessionId"/>'s ephemeral event stream.
    /// </summary>
    ValueTask<bool> IsAllowedAsync(string sessionId, HttpContext context, CancellationToken cancellationToken);
}
