using Microsoft.Extensions.Hosting;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Whether this server can play the game, as opposed to merely answering HTTP.
/// </summary>
/// <remarks>
/// <para>Kestrel starts listening once every hosted service's <c>StartAsync</c> has returned —
/// migrations, the publishing and connection-state NATS connections, bootstrap, plugins, connection
/// reconciliation. Two things still come up after that, each with its own retry loop: the JetStream
/// consumers that carry player input to the engine (<see cref="NatsConsumerRegistry"/>), and the
/// NATS-to-SignalR bridge that carries output back to the portal (<see cref="NatsBridgeService"/>).
/// Until both are up, a portal that loaded would send commands nobody reads and miss the output.</para>
/// <para><see cref="IsReady"/> is the live answer (<c>/api/health</c>, <c>/ready</c>). <see cref="HasBeenReady"/>
/// latches the first time it is true, and is what decides whether a browser is handed the portal or the
/// startup page: a later broker blip reconnects on its own, and throwing players back to a startup page
/// mid-session would help nobody.</para>
/// </remarks>
public sealed class ServerReadiness(NatsConsumerRegistry consumers, IHostApplicationLifetime lifetime)
{
	private volatile Func<bool>? _bridgeOpen;
	private volatile bool _hasBeenReady;

	/// <summary>Called by the bridge with its connection's live state once it has connected, and with
	/// <c>null</c> when it tears that connection down. Read on every check: the NATS client reconnects on
	/// its own, so the bridge never sees a broker outage as a failure.</summary>
	public void SetBridgeConnection(Func<bool>? isOpen) => _bridgeOpen = isOpen;

	private bool BridgeOpen => _bridgeOpen?.Invoke() == true;

	/// <summary>The host has started, every input consumer is consuming and the output bridge is connected.</summary>
	public bool IsReady
	{
		get
		{
			var ready = lifetime.ApplicationStarted.IsCancellationRequested && consumers.AllActive && BridgeOpen;
			if (ready) _hasBeenReady = true;
			return ready;
		}
	}

	/// <summary>Whether <see cref="IsReady"/> has ever been true in this process.</summary>
	public bool HasBeenReady => _hasBeenReady || IsReady;

	/// <summary>What is still missing, for the health endpoint; empty when ready.</summary>
	public IReadOnlyList<string> Pending()
	{
		var pending = new List<string>(3);
		if (!lifetime.ApplicationStarted.IsCancellationRequested) pending.Add("host");
		if (!consumers.AllActive) pending.Add("input-consumers");
		if (!BridgeOpen) pending.Add("output-bridge");
		return pending;
	}
}
