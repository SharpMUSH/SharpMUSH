using System.Text;

namespace SharpMUSH.Messaging.NATS.Strategy;

/// <summary>
/// The NATS container a process starts for itself when <c>NATS_URL</c> is unset. A build starts it through
/// Testcontainers (<see cref="NatsTestContainerStrategy"/>), a native binary through Docker's API
/// (<see cref="NatsDockerStrategy"/>); both label it <see cref="ReuseLabel"/>, so the server and the
/// connection server share one whichever way each was built.
/// </summary>
internal static class NatsLocalContainer
{
	/// <summary>The NATS image. Pinned to a specific 2.x Alpine release.</summary>
	public const string Image = "nats";

	public const string Tag = "2.14-alpine";

	public const string ReuseLabelKey = "reuse-id";

	public const string ReuseLabelValue = "SharpMUSH-NATS";

	public const string ReuseLabel = ReuseLabelKey + "=" + ReuseLabelValue;

	public const int ClientPort = 4222;

	/// <summary>Maximum message payload size, set in the NATS config file.</summary>
	private const int MaxPayloadBytes = 6 * 1024 * 1024; // 6 MB

	public const string ConfigPath = "/etc/nats/nats.conf";

	// Replaces NatsBuilder's default command, whose --debug --trace logs every message the bus
	// carries: one Integration run wrote a 2.7 GB container log.
	public static readonly string[] Command = ["-c", ConfigPath, "--http_port", "8222", "--jetstream"];

	// JetStream reserves each stream's MaxBytes against max_file_store when the stream is created, and
	// left unset that limit is 75% of the disk that happens to be free. Short-lived streams with budgets
	// (docs/design/messaging-retention.md) would then fail with "insufficient storage resources" on a
	// nearly full disk; the explicit ceiling makes creation independent of it. Little is actually written.
	public static readonly byte[] Config = Encoding.UTF8.GetBytes(
		$"max_payload: {MaxPayloadBytes}\njetstream {{ max_file_store: 256GB }}\n");
}
