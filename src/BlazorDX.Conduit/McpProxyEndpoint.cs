using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorDX.Conduit;

/// <summary>
/// The Conduit Router's inbound half: a secure endpoint the external MCP resource-provider hits
/// to deliver the initial encrypted response payload for a session. The request body is read as
/// opaque bytes and handed straight to <see cref="McpBrokerClient.RouteToSessionAsync"/> for SSE
/// delivery — this server never parses, inspects, or decrypts it (Zero-Knowledge Routing): the
/// ciphertext is meaningful only to whoever holds the matching private key, and that is never
/// this process.
/// </summary>
public static class McpProxyEndpoint
{
    /// <summary>The route the external MCP provider POSTs a delivery to.</summary>
    public const string RoutePattern = "/mcp-proxy/{sessionId}";

    private const string MessageIdHeader = "X-Conduit-Message-Id";
    private const string PayloadEventType = "PAYLOAD";

    /// <summary>
    /// Maps the provider-delivery endpoint. Antiforgery is disabled: the caller is a
    /// server-to-server MCP resource-provider, not a browser form — the same reasoning already
    /// applied to <c>/mcp</c> and <c>/htmx/echo</c> in samples/BlazorDX.Demo/Program.cs.
    /// </summary>
    /// <remarks>
    /// Like ASP.NET Core's own <c>MapPost</c>, this performs reflection-based delegate parameter
    /// binding (<c>RequestDelegateFactory</c>) and so cannot be trimmed/AOT-compiled — the
    /// requirement is propagated honestly rather than suppressed.
    /// </remarks>
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost, which binds the handler delegate's parameters via reflection.")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost, which may generate code to bind the handler delegate's parameters.")]
    public static IEndpointRouteBuilder MapMcpProxy(this WebApplication app)
    {
        app.MapPost(RoutePattern, static (
            HttpContext context,
            string sessionId,
            EphemeralSessionRegistry registry,
            CancellationToken cancellationToken) =>
        {
            IEphemeralSessionAuthorizer? authorizer =
                context.RequestServices.GetService<IEphemeralSessionAuthorizer>();
            return HandlePostAsync(context, sessionId, registry, authorizer, cancellationToken);
        }).DisableAntiforgery();

        return app;
    }

    /// <summary>
    /// The handler behind <see cref="MapMcpProxy"/>, exposed internally so tests can drive it
    /// directly with a hand-built <see cref="DefaultHttpContext"/> and assert the exact bytes
    /// delivered are the exact bytes received — proof this router never touches the plaintext.
    /// </summary>
    internal static async Task<IResult> HandlePostAsync(
        HttpContext context,
        string sessionId,
        EphemeralSessionRegistry registry,
        IEphemeralSessionAuthorizer? authorizer,
        CancellationToken cancellationToken)
    {
        if (authorizer is not null &&
            !await authorizer.IsAllowedAsync(sessionId, context, cancellationToken).ConfigureAwait(false))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        string messageId = context.Request.Headers[MessageIdHeader].ToString();
        if (string.IsNullOrEmpty(messageId))
        {
            return Results.Json(
                new McpProxyErrorResponse { Error = $"The '{MessageIdHeader}' header is required." },
                ConduitJsonContext.Default.McpProxyErrorResponse,
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The body is an opaque, already-encrypted payload — ciphertext meant for the client's
        // own private key. Read it as raw text and forward it verbatim: never parsed, never
        // decrypted, no key ever touches this process. (Same read pattern as the existing
        // /mcp endpoint in Program.cs.)
        using StreamReader reader = new(context.Request.Body);
        string opaquePayload = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        int delivered = registry.GetConnections(sessionId).Count;
        await McpBrokerClient.RouteToSessionAsync(registry, sessionId, PayloadEventType, opaquePayload, cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(
            new McpProxyAcceptedResponse { Delivered = delivered, MessageId = messageId },
            ConduitJsonContext.Default.McpProxyAcceptedResponse,
            statusCode: StatusCodes.Status202Accepted);
    }
}
