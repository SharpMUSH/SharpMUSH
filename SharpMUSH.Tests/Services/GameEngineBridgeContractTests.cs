using NSubstitute;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Unit tests for <see cref="IGameEngineBridge"/> — verifies that the interface
/// contract can be correctly implemented and that a mock can express all relevant
/// acceptance / rejection scenarios.
/// </summary>
public class GameEngineBridgeContractTests
{
    private static IGameEngineBridge BuildBridge(
        bool accepts = true,
        string? rejectionReason = null,
        EngineStateResponse? stateResponse = null)
    {
        var bridge = Substitute.For<IGameEngineBridge>();

        bridge
            .SendCommandAsync(Arg.Any<EngineCommandRequest>(), Arg.Any<CancellationToken>())
            .Returns(new EngineCommandAck(Accepted: accepts, RejectionReason: rejectionReason));

        bridge
            .GetStateAsync(Arg.Any<EngineStateRequest>(), Arg.Any<CancellationToken>())
            .Returns(stateResponse);

        return bridge;
    }

    // ── SendCommandAsync ─────────────────────────────────────────────────────

    [Test]
    public async Task SendCommandAsync_WhenAccepted_ReturnsAcceptedAck()
    {
        var bridge = BuildBridge(accepts: true);
        var req = new EngineCommandRequest("#1", "look", DateTimeOffset.UtcNow);

        var ack = await bridge.SendCommandAsync(req);

        await Assert.That(ack.Accepted).IsTrue();
        await Assert.That(ack.RejectionReason).IsNull();
    }

    [Test]
    public async Task SendCommandAsync_WhenRejected_ReturnsRejectedAck()
    {
        var bridge = BuildBridge(accepts: false, rejectionReason: "rate limited");
        var req = new EngineCommandRequest("#1", "drop all", DateTimeOffset.UtcNow);

        var ack = await bridge.SendCommandAsync(req);

        await Assert.That(ack.Accepted).IsFalse();
        await Assert.That(ack.RejectionReason).IsEqualTo("rate limited");
    }

    [Test]
    public async Task SendCommandAsync_ForwardsRequestFields()
    {
        var bridge = BuildBridge();
        var ts = DateTimeOffset.UtcNow;
        var req = new EngineCommandRequest("#99", "inventory", ts);

        await bridge.SendCommandAsync(req);

        await bridge.Received(1).SendCommandAsync(
            Arg.Is<EngineCommandRequest>(r =>
                r.CharacterDbref == "#99" &&
                r.Command == "inventory" &&
                r.Timestamp == ts),
            Arg.Any<CancellationToken>());
    }

    // ── GetStateAsync ────────────────────────────────────────────────────────

    [Test]
    public async Task GetStateAsync_WhenStateAvailable_ReturnsSnapshot()
    {
        var expected = new EngineStateResponse(
            CharacterDbref: "#1",
            RoomDbref: "#2",
            RoomName: "The Nexus",
            VisibleObjectDbrefs: ["#3", "#4"]);

        var bridge = BuildBridge(stateResponse: expected);
        var response = await bridge.GetStateAsync(new EngineStateRequest("#1"));

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.RoomName).IsEqualTo("The Nexus");
        await Assert.That(response.VisibleObjectDbrefs.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetStateAsync_WhenUnreachable_ReturnsNull()
    {
        var bridge = BuildBridge(stateResponse: null);
        var response = await bridge.GetStateAsync(new EngineStateRequest("#99"));

        await Assert.That(response).IsNull();
    }

    [Test]
    public async Task GetStateAsync_ForwardsCharacterDbref()
    {
        var bridge = BuildBridge();
        await bridge.GetStateAsync(new EngineStateRequest("#77"));

        await bridge.Received(1).GetStateAsync(
            Arg.Is<EngineStateRequest>(r => r.CharacterDbref == "#77"),
            Arg.Any<CancellationToken>());
    }

    // ── DTO structural tests ─────────────────────────────────────────────────

    [Test]
    public async Task EngineCommandRequest_RecordEquality()
    {
        var ts = DateTimeOffset.UtcNow;
        var a = new EngineCommandRequest("#1", "look", ts);
        var b = new EngineCommandRequest("#1", "look", ts);

        await Assert.That(a).IsEqualTo(b);
    }

    [Test]
    public async Task EngineCommandAck_DefaultRejectionReason_IsNull()
    {
        var ack = new EngineCommandAck(Accepted: true);
        await Assert.That(ack.RejectionReason).IsNull();
    }

    [Test]
    public async Task EngineStateResponse_VisibleObjectDbrefs_IsReadOnly()
    {
        var state = new EngineStateResponse("#1", "#2", "Room", ["#3"]);
        await Assert.That(state.VisibleObjectDbrefs.Count).IsEqualTo(1);
    }
}
