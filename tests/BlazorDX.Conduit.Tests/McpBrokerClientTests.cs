using System.Runtime.CompilerServices;
using BlazorDX.Conduit;
using Moq;
using Xunit;

namespace BlazorDX.Conduit.Tests;

public sealed class McpBrokerClientTests
{
    [Fact]
    public async Task RunAsync_RoutesEachNotificationToItsOwnSession_MappingActionToTheDocumentedEventName()
    {
        var registry = new EphemeralSessionRegistry();
        var writerA = new Mock<IEphemeralEventWriter>();
        var writerB = new Mock<IEphemeralEventWriter>();
        writerA.Setup(w => w.WriteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        writerB.Setup(w => w.WriteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        registry.Register("session-a", writerA.Object);
        registry.Register("session-b", writerB.Object);

        var notifications = new[]
        {
            new ConduitNotification("m1", "session-a", ConduitAction.Withdraw),
            new ConduitNotification("m2", "session-b", ConduitAction.Refresh),
        };
        var source = new Mock<IConduitNotificationSource>();
        source.Setup(s => s.ReceiveAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct => ToAsyncEnumerable(notifications, ct));

        var client = new McpBrokerClient(source.Object, registry);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.RunAsync(cts.Token);

        writerA.Verify(w => w.WriteEventAsync("WITHDRAW", "m1", It.IsAny<CancellationToken>()), Times.Once);
        writerB.Verify(w => w.WriteEventAsync("REFRESH", "m2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_StopsGracefullyWhenCancelled_WithoutThrowing()
    {
        var registry = new EphemeralSessionRegistry();
        var source = new Mock<IConduitNotificationSource>();
        source.Setup(s => s.ReceiveAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(NeverEndingSource);

        var client = new McpBrokerClient(source.Object, registry);
        using var cts = new CancellationTokenSource();

        Task running = client.RunAsync(cts.Token);
        await Task.Delay(30);
        Assert.False(running.IsCompleted);

        cts.Cancel();

        // Must complete cleanly (not fault/throw) — the host's stopping token is expected
        // cancellation, not an error.
        await running;
        Assert.True(running.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RouteToSessionAsync_ForwardsTheDataVerbatim_WithoutInterpretingIt()
    {
        // Zero-Knowledge Routing: this is the Conduit's single routing primitive, and it must
        // never transform, parse, or otherwise "look at" the payload — WITHDRAW/REFRESH
        // message-ids and PAYLOAD ciphertext both flow through this exact same path unchanged.
        var registry = new EphemeralSessionRegistry();
        var writer = new Mock<IEphemeralEventWriter>();
        writer.Setup(w => w.WriteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        registry.Register("session-z", writer.Object);

        const string opaqueCiphertext = "U29tZS1PcGFxdWUtQ2lwaGVydGV4dA==";
        await McpBrokerClient.RouteToSessionAsync(registry, "session-z", "PAYLOAD", opaqueCiphertext, CancellationToken.None);

        writer.Verify(w => w.WriteEventAsync("PAYLOAD", opaqueCiphertext, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async IAsyncEnumerable<ConduitNotification> ToAsyncEnumerable(
        IEnumerable<ConduitNotification> notifications,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (ConduitNotification notification in notifications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return notification;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<ConduitNotification> NeverEndingSource(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break; // Unreachable: Task.Delay only returns by throwing OperationCanceledException.
    }
}
