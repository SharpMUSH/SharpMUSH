using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.Messaging.NATS.Strategy;

/// <summary>
/// The NATS container a process starts for itself when <c>NATS_URL</c> is unset. The server starts it through
/// Testcontainers (<see cref="NatsTestContainerStrategy"/>), the socket server through Docker's API
/// (<see cref="NatsDockerStrategy"/>). Both give it the one name <see cref="Name"/>, so whichever starts first, the
/// other joins it, and both label it with <see cref="Revision"/>, so a container from an older image or config is
/// never joined.
/// </summary>
internal static class NatsLocalContainer
{
	/// <summary>The NATS image. Pinned to a specific 2.x Alpine release.</summary>
	public const string Image = "nats";

	public const string Tag = "2.14-alpine";

	public const string Name = "sharpmush-local-nats";

	public const string ReuseLabelKey = "reuse-id";

	public const string ReuseLabelValue = "SharpMUSH-NATS";

	public const string RevisionLabelKey = "sharpmush.nats-revision";

	/// <summary>Marks a container <see cref="NatsDockerStrategy"/> created; Testcontainers finds its own by its reuse hash.</summary>
	public const string CreatorLabelKey = "sharpmush.nats-creator";

	public const string CreatorLabelValue = "engine-api";

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

	/// <summary>A hash of the image, config and command: a container carrying another one is not this revision's.</summary>
	public static readonly string Revision = Convert.ToHexStringLower(SHA256.HashData(
		Encoding.UTF8.GetBytes($"{Image}:{Tag}\n{Encoding.UTF8.GetString(Config)}\n{string.Join(' ', Command)}")))[..12];
}
