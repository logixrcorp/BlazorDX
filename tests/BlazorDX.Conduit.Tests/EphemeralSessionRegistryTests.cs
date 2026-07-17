using BlazorDX.Conduit;
using Moq;
using Xunit;

namespace BlazorDX.Conduit.Tests;

public sealed class EphemeralSessionRegistryTests
{
    [Fact]
    public void Register_GroupsConnectionsBySessionId()
    {
        var registry = new EphemeralSessionRegistry();

        registry.Register("session-a", new Mock<IEphemeralEventWriter>().Object);
        registry.Register("session-a", new Mock<IEphemeralEventWriter>().Object);
        registry.Register("session-b", new Mock<IEphemeralEventWriter>().Object);

        Assert.Equal(2, registry.ConnectionCount("session-a"));
        Assert.Equal(1, registry.ConnectionCount("session-b"));
        Assert.Equal(0, registry.ConnectionCount("session-unknown"));
    }

    [Fact]
    public void Register_ReturnsAConnectionStampedWithTheRequestedSessionId()
    {
        var registry = new EphemeralSessionRegistry();

        EphemeralConnection connection = registry.Register("session-a", new Mock<IEphemeralEventWriter>().Object);

        Assert.Equal("session-a", connection.SessionId);
        Assert.NotEqual(Guid.Empty, connection.Id);
    }

    [Fact]
    public void Unregister_RemovesOnlyThatConnection_AndPrunesTheEmptiedSession()
    {
        var registry = new EphemeralSessionRegistry();
        EphemeralConnection connection1 = registry.Register("session-a", new Mock<IEphemeralEventWriter>().Object);
        EphemeralConnection connection2 = registry.Register("session-a", new Mock<IEphemeralEventWriter>().Object);

        registry.Unregister(connection1);
        Assert.Equal(1, registry.ConnectionCount("session-a"));
        Assert.DoesNotContain(registry.GetConnections("session-a"), c => c.Id == connection1.Id);

        registry.Unregister(connection2);
        Assert.Equal(0, registry.ConnectionCount("session-a"));
        Assert.Empty(registry.GetConnections("session-a"));
    }

    [Fact]
    public void Unregister_OfAnUnknownConnection_IsANoOp()
    {
        var registry = new EphemeralSessionRegistry();
        EphemeralConnection ghost = registry.Register("session-a", new Mock<IEphemeralEventWriter>().Object);
        registry.Unregister(ghost);

        // Unregistering the same (already-removed) connection again must not throw.
        registry.Unregister(ghost);

        Assert.Equal(0, registry.ConnectionCount("session-a"));
    }

    [Fact]
    public async Task PushEphemeralEventAsync_FansOutToEveryConnectionInSession_ButNotOtherSessions()
    {
        var registry = new EphemeralSessionRegistry();
        var writerA1 = new Mock<IEphemeralEventWriter>();
        var writerA2 = new Mock<IEphemeralEventWriter>();
        var writerB = new Mock<IEphemeralEventWriter>();
        writerA1.Setup(w => w.WriteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        writerA2.Setup(w => w.WriteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        writerB.Setup(w => w.WriteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        registry.Register("session-a", writerA1.Object);
        registry.Register("session-a", writerA2.Object);
        registry.Register("session-b", writerB.Object);

        await registry.PushEphemeralEventAsync("session-a", "WITHDRAW", "msg-1", CancellationToken.None);

        writerA1.Verify(w => w.WriteEventAsync("WITHDRAW", "msg-1", It.IsAny<CancellationToken>()), Times.Once);
        writerA2.Verify(w => w.WriteEventAsync("WITHDRAW", "msg-1", It.IsAny<CancellationToken>()), Times.Once);
        writerB.Verify(w => w.WriteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PushEphemeralEventAsync_ToASessionWithNoConnections_IsASilentNoOp()
    {
        var registry = new EphemeralSessionRegistry();

        // Must not throw even though nobody is listening on "ghost-session".
        await registry.PushEphemeralEventAsync("ghost-session", "REFRESH", "msg", CancellationToken.None);
    }
}
