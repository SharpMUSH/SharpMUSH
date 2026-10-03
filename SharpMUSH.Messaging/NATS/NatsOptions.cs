namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// Configuration options for NATS JetStream messaging.
/// Mirror of <see cref="Configuration.MessageQueueOptions"/> for the Kafka adapter,
/// allowing both transports to be tuned via the same fields.
/// </summary>
public class NatsOptions
{
	/// <summary>
	/// The NATS server URL (e.g. "nats://localhost:4222")
	/// </summary>
	public string Url { get; set; } = "nats://localhost:4222";

	/// <summary>
	/// Name of the JetStream stream that covers all SharpMUSH subjects.
	/// </summary>
	public string StreamName { get; set; } = "SHARPMUSH";

	/// <summary>
	/// Subject prefix used for all published messages (e.g. "sharpmush").
	/// The full subject becomes "{Prefix}.{kebab-case-type-name}".
	/// </summary>
	public string SubjectPrefix { get; set; } = "sharpmush";

	/// <summary>
	/// How long an unconsumed message waits in a bus stream for its consumer. The bus streams use
	/// interest retention, so an acknowledged message is removed at once; this only bounds the backlog a
	/// stopped consumer leaves behind. It is transport retention, configured apart from browser replay
	/// (<c>Replay:RetentionHours</c> on the ConnectionServer). <c>SHARPMUSH_NATS_MAX_AGE</c>.
	/// </summary>
	public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(1);

	/// <summary>
	/// Total bytes a bus stream may hold. When the backlog reaches it, new publications are rejected
	/// (discard-new) rather than removing older, still unprocessed messages. <c>SHARPMUSH_NATS_MAX_BYTES</c>.
	/// </summary>
	public long MaxBytes { get; set; } = DefaultMaxBytes;

	/// <summary>The default per-stream byte budget: 512 MiB.</summary>
	public const long DefaultMaxBytes = 512L * 1024 * 1024;

	/// <summary>Total messages a bus stream may hold; -1 leaves only the byte budget.</summary>
	public long MaxMsgs { get; set; } = -1;

	/// <summary>
	/// How long the broker waits for an acknowledgment before it redelivers a message — the window a
	/// consumer process can disappear in before its in-flight message goes to the next one.
	/// </summary>
	public TimeSpan AckWait { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>
	/// How many times the broker delivers one message before giving up on it. Bounds the redelivery a
	/// message gets when its consumer process keeps dying before it is acknowledged.
	/// </summary>
	public int MaxDeliver { get; set; } = 5;

	/// <summary>
	/// How many times a handler runs a message whose failure it reports as retryable
	/// (<see cref="Abstractions.RetryableMessageException"/>), the first run included.
	/// </summary>
	public int HandlerMaxAttempts { get; set; } = 3;

	/// <summary>The delay before the first in-process retry; each later retry doubles it.</summary>
	public TimeSpan HandlerRetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);

	/// <summary>
	/// Streams other than the bus streams whose size the broker monitor reports, such as the
	/// ConnectionServer's replay stream.
	/// </summary>
	public List<string> MonitoredStreams { get; } = [];

	/// <summary>How often the broker monitor reads stream, consumer and storage figures.</summary>
	public TimeSpan MonitorInterval { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>
	/// Name of the JetStream stream to consume from.
	/// When null the value of <see cref="StreamName"/> is used (publish = consume,
	/// suitable for single-stream setups or tests).
	/// </summary>
	public string? ConsumeStreamName { get; set; }

	/// <summary>
	/// Subject prefix used for the consuming side (i.e. the publisher's prefix in the
	/// other application).  When null the value of <see cref="SubjectPrefix"/> is used.
	/// </summary>
	public string? ConsumeSubjectPrefix { get; set; }

	/// <summary>
	/// Maximum size in bytes for a single message accepted by the JetStream stream.
	/// Defaults to 6 MB. Set to -1 for unlimited.
	/// </summary>
	public int MaxMsgSize { get; set; } = 6 * 1024 * 1024;

	/// <summary>Maximum time allowed for a message publish, including broker disconnection.</summary>
	public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(2);

	/// <summary>
	/// Maximum time to wait for the first connection to the broker at startup. Failed attempts are
	/// retried until this deadline passes.
	/// </summary>
	public TimeSpan ConnectTimeout { get; set; } = NatsStartupConnection.DefaultTimeout;

	/// <summary>
	/// Throws when a limit is missing or contradicts another. Every stream creator calls it before it
	/// touches the broker, so a bad setting stops startup instead of producing an unbounded stream.
	/// </summary>
	public void Validate()
	{
		if (MaxBytes <= 0)
			throw new ArgumentOutOfRangeException(nameof(MaxBytes), MaxBytes, "A bus stream needs a positive byte budget.");
		if (MaxMsgSize != -1 && (MaxMsgSize <= 0 || MaxMsgSize > MaxBytes))
			throw new ArgumentOutOfRangeException(nameof(MaxMsgSize), MaxMsgSize,
				"The largest message must be positive and fit inside the stream's byte budget.");
		if (MaxMsgs is 0 or < -1)
			throw new ArgumentOutOfRangeException(nameof(MaxMsgs), MaxMsgs, "Use a positive message count, or -1 for none.");
		if (MaxAge <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(MaxAge), MaxAge, "A bus stream needs a positive maximum age.");
		if (AckWait <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(AckWait), AckWait, "The acknowledgment wait must be positive.");
		if (MaxDeliver < 1)
			throw new ArgumentOutOfRangeException(nameof(MaxDeliver), MaxDeliver, "A message must be delivered at least once.");
		if (HandlerMaxAttempts < 1)
			throw new ArgumentOutOfRangeException(nameof(HandlerMaxAttempts), HandlerMaxAttempts, "A handler runs at least once.");
		if (HandlerRetryDelay < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(HandlerRetryDelay), HandlerRetryDelay, "A retry delay cannot be negative.");
		if (MonitorInterval <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(MonitorInterval), MonitorInterval, "The monitor interval must be positive.");
	}

	/// <summary>
	/// Reads <c>SHARPMUSH_NATS_MAX_BYTES</c> (bytes) and <c>SHARPMUSH_NATS_MAX_AGE</c> (e.g. <c>30m</c>,
	/// <c>1h</c>, or seconds). A value that does not parse throws: a typo must not quietly fall back to a
	/// budget the operator did not choose.
	/// </summary>
	public void ApplyEnvironment(Func<string, string?> read)
	{
		if (read("SHARPMUSH_NATS_MAX_BYTES") is { Length: > 0 } bytes)
			MaxBytes = long.TryParse(bytes, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
				? parsed
				: throw new FormatException($"SHARPMUSH_NATS_MAX_BYTES is '{bytes}', which is not a byte count.");
		if (read("SHARPMUSH_NATS_MAX_AGE") is { Length: > 0 } age)
			MaxAge = TryParseInterval(age, out var interval)
				? interval
				: throw new FormatException($"SHARPMUSH_NATS_MAX_AGE is '{age}', which is not an interval like 30m, 1h or a count of seconds.");
	}

	/// <summary>Reads an interval written as a count with an <c>s</c>, <c>m</c>, <c>h</c> or <c>d</c> suffix, or as bare seconds.</summary>
	public static bool TryParseInterval(string text, out TimeSpan interval)
	{
		interval = default;
		var trimmed = text.Trim();
		if (trimmed.Length == 0) return false;
		var unit = char.ToLowerInvariant(trimmed[^1]);
		var number = char.IsAsciiLetter(unit) ? trimmed[..^1] : trimmed;
		if (!double.TryParse(number, System.Globalization.NumberStyles.AllowDecimalPoint,
			System.Globalization.CultureInfo.InvariantCulture, out var count) || count <= 0) return false;
		TimeSpan? parsed = unit switch
		{
			'd' => TimeSpan.FromDays(count),
			'h' => TimeSpan.FromHours(count),
			'm' => TimeSpan.FromMinutes(count),
			's' => TimeSpan.FromSeconds(count),
			_ when char.IsAsciiDigit(unit) => TimeSpan.FromSeconds(count),
			_ => null
		};
		if (parsed is not { } value) return false;
		interval = value;
		return true;
	}

	internal string GetConsumeStreamName() => ConsumeStreamName ?? StreamName;
	internal string GetConsumeSubjectPrefix() => ConsumeSubjectPrefix ?? SubjectPrefix;
}
