using System.Text;
using BlazorDX.Conduit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace BlazorDX.Conduit.Tests;

public sealed class EphemeralEventsEndpointTests
{
    [Fact]
    public async Task HttpResponseEventWriter_FlushesImmediatelyAfterEachWrite_NotJustAtTheEnd()
    {
        // A write that is not followed by its own flush would sit in a buffer indefinitely on a
        // stream that never naturally completes (SSE). Assert the exact interleaving — write,
        // flush, write, flush — not merely that both calls eventually happened.
        var order = new List<string>();
        var stream = new Mock<Stream>();
        stream.Setup(s => s.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("write"))
            .Returns(ValueTask.CompletedTask);
        stream.Setup(s => s.FlushAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("flush"))
            .Returns(Task.CompletedTask);

        var context = new DefaultHttpContext();
        context.Response.Body = stream.Object;
        var writer = new HttpResponseEventWriter(context.Response);

        await writer.WriteEventAsync("WITHDRAW", "msg-1", CancellationToken.None);
        await writer.WriteEventAsync("REFRESH", "msg-2", CancellationToken.None);

        Assert.Equal(new[] { "write", "flush", "write", "flush" }, order);
    }

    [Fact]
    public async Task HttpResponseEventWriter_WritesTheDocumentedSseWireFormat()
    {
        using var buffer = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = buffer;
        var writer = new HttpResponseEventWriter(context.Response);

        await writer.WriteEventAsync("REFRESH", "abc123", CancellationToken.None);

        string text = Encoding.UTF8.GetString(buffer.ToArray());
        Assert.Equal("event: REFRESH\ndata: abc123\n\n", text);
    }

    [Fact]
    public async Task HandleGetAsync_WhenAuthorizerDenies_Returns403AndNeverRegistersAConnection()
    {
        var registry = new EphemeralSessionRegistry();
        var authorizer = new Mock<IEphemeralSessionAuthorizer>();
        authorizer
            .Setup(a => a.IsAllowedAsync("blocked-session", It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await EphemeralEventsEndpoint.HandleGetAsync(
            context, "blocked-session", registry, authorizer.Object, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal(0, registry.ConnectionCount("blocked-session"));
    }

    [Fact]
    public async Task HandleGetAsync_WhenNoAuthorizerIsRegistered_AllowsTheConnection()
    {
        var registry = new EphemeralSessionRegistry();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        using var cts = new CancellationTokenSource();

        Task handling = EphemeralEventsEndpoint.HandleGetAsync(context, "session-open", registry, authorizer: null, cts.Token);
        await Task.Delay(50);

        Assert.Equal(1, registry.ConnectionCount("session-open"));

        cts.Cancel();
        await handling;
    }

    [Fact]
    public async Task HandleGetAsync_WhenAuthorized_SetsSseHeaders_RegistersAndUnregistersOnDisconnect()
    {
        var registry = new EphemeralSessionRegistry();
        var authorizer = new Mock<IEphemeralSessionAuthorizer>();
        authorizer
            .Setup(a => a.IsAllowedAsync(It.IsAny<string>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        using var cts = new CancellationTokenSource();

        Task handling = EphemeralEventsEndpoint.HandleGetAsync(context, "session-x", registry, authorizer.Object, cts.Token);

        // Give the handler a moment to run past authorization, set headers, and register the
        // connection before we simulate a client disconnect.
        await Task.Delay(50);

        Assert.Equal("text/event-stream", context.Response.ContentType);
        Assert.Equal("no-cache", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no", context.Response.Headers["X-Accel-Buffering"].ToString());
        Assert.Equal(1, registry.ConnectionCount("session-x"));

        cts.Cancel();
        await handling;

        Assert.Equal(0, registry.ConnectionCount("session-x"));
    }

    [Fact]
    public void MapEphemeralEvents_RegistersAGetRouteAtTheExactContractPath()
    {
        // The path contract is shared with a separately-built frontend calling `new
        // EventSource("/ephemeral-events/{sessionId}")` — this pins it so a refactor cannot
        // silently drift the route.
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<EphemeralSessionRegistry>();
        WebApplication app = builder.Build();

        app.MapEphemeralEvents();

        RouteEndpoint endpoint = Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(ds => ds.Endpoints)
                .OfType<RouteEndpoint>(),
            e => e.RoutePattern.RawText == "/ephemeral-events/{sessionId}");

        HttpMethodMetadata? methodMetadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        Assert.NotNull(methodMetadata);
        Assert.Contains("GET", methodMetadata!.HttpMethods);
        Assert.Equal("/ephemeral-events/{sessionId}", EphemeralEventsEndpoint.RoutePattern);
    }
}
