using System.Text;
using BlazorDX.Conduit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace BlazorDX.Conduit.Tests;

public sealed class McpProxyEndpointTests
{
    [Fact]
    public async Task HandlePostAsync_WhenAuthorizerDenies_Returns403_AndNeverRoutesAnything()
    {
        var registry = new EphemeralSessionRegistry();
        var writer = new Mock<IEphemeralEventWriter>();
        registry.Register("session-y", writer.Object);

        var authorizer = new Mock<IEphemeralSessionAuthorizer>();
        authorizer
            .Setup(a => a.IsAllowedAsync("session-y", It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        HttpContext context = BuildContext(messageId: "m-1", body: "cipher");

        IResult result = await McpProxyEndpoint.HandlePostAsync(
            context, "session-y", registry, authorizer.Object, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        writer.Verify(w => w.WriteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandlePostAsync_WhenMessageIdHeaderIsMissing_Returns400()
    {
        var registry = new EphemeralSessionRegistry();
        HttpContext context = BuildContext(messageId: null, body: "cipher");

        IResult result = await McpProxyEndpoint.HandlePostAsync(
            context, "session-y", registry, authorizer: null, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandlePostAsync_DeliversTheOpaqueBodyVerbatim_ToEveryConnectionOnTheSession()
    {
        // Proof of Zero-Knowledge Routing: the body is a nonsense ciphertext-looking blob, and
        // this endpoint must forward the exact bytes it received — no parsing, no decoding, no
        // JSON envelope inspection.
        var registry = new EphemeralSessionRegistry();
        var writer = new Mock<IEphemeralEventWriter>();
        writer.Setup(w => w.WriteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        registry.Register("session-y", writer.Object);

        const string opaqueCiphertext = "U29tZS1DaXBoZXJ0ZXh0LUJsb2I=";
        HttpContext context = BuildContext(messageId: "m-42", body: opaqueCiphertext);

        IResult result = await McpProxyEndpoint.HandlePostAsync(
            context, "session-y", registry, authorizer: null, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        writer.Verify(
            w => w.WriteEventAsync("PAYLOAD", opaqueCiphertext, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandlePostAsync_WhenNoConnectionsAreListening_StillAccepts_AndReportsZeroDelivered()
    {
        var registry = new EphemeralSessionRegistry();
        HttpContext context = BuildContext(messageId: "m-1", body: "cipher");

        IResult result = await McpProxyEndpoint.HandlePostAsync(
            context, "session-nobody-home", registry, authorizer: null, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);

        string body = ReadResponseBody(context);
        Assert.Contains("\"delivered\":0", body);
    }

    private static DefaultHttpContext BuildContext(string? messageId, string body)
    {
        var context = new DefaultHttpContext();
        if (messageId is not null)
        {
            context.Request.Headers["X-Conduit-Message-Id"] = messageId;
        }

        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();

        // IResult.ExecuteAsync (JsonHttpResult/StatusCodeHttpResult) resolves an ILoggerFactory
        // off HttpContext.RequestServices internally; DefaultHttpContext leaves RequestServices
        // null, so give it a minimal real provider — HttpContext.RequestServices isn't otherwise
        // exercised by anything under test.
        var services = new ServiceCollection();
        services.AddLogging();
        context.RequestServices = services.BuildServiceProvider();
        return context;
    }

    private static string ReadResponseBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }
}
