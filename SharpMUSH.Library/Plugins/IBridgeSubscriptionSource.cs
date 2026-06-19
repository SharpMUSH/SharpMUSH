namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Phase 2a contribution surface for NATS-to-SignalR bridge subscriptions. A plugin entry type may
/// implement this to subscribe to its own NATS subjects and forward messages to SignalR groups,
/// mirroring the engine's built-in output/room/scene subscriptions. The host's <c>NatsBridgeService</c>
/// runs every collected source alongside the built-ins inside its connect loop, each isolated in
/// try/catch so a failing subscription cannot tear down the others.
///
/// The parameters are intentionally loose (<see cref="object"/>) so this contract can live in
/// <c>SharpMUSH.Library</c> without taking a SignalR dependency: the host passes a
/// <c>NATS.Client.Core.NatsConnection</c> and an <c>IHubContext&lt;GameHub, IGameHubClient&gt;</c>,
/// which the plugin casts to the concrete types it references.
/// </summary>
public interface IBridgeSubscriptionSource
{
	/// <summary>
	/// Run the plugin's bridge subscription loop until <paramref name="ct"/> is cancelled.
	/// </summary>
	/// <param name="natsConnection">The live <c>NATS.Client.Core.NatsConnection</c>.</param>
	/// <param name="hubContext">The host's <c>IHubContext&lt;GameHub, IGameHubClient&gt;</c>.</param>
	/// <param name="ct">Cancellation token tied to the bridge service lifetime.</param>
	Task RunAsync(object natsConnection, object hubContext, CancellationToken ct);
}
