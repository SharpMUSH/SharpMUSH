namespace SharpMUSH.Library.Webhooks;

/// <summary>
/// How the server delivers webhooks, bound from the <c>Webhooks</c> section (environment variables
/// <c>Webhooks__MaxAttempts</c> and so on). These are deployment settings; the subscriptions themselves
/// are game data, managed with <c>@webhook</c>.
/// </summary>
public sealed class WebhookOptions
{
	public const string Section = "Webhooks";

	/// <summary>How many times one delivery is tried before it is dropped. The first try counts.</summary>
	public int MaxAttempts { get; set; } = 9;

	/// <summary>The wait before the first retry. Each later wait is five times the one before.</summary>
	public TimeSpan FirstRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

	/// <summary>The longest wait between two tries.</summary>
	public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(2);

	/// <summary>How long one try may take, connection and answer together.</summary>
	public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

	/// <summary>How many deliveries are in flight at once, across every subscription.</summary>
	public int MaxConcurrency { get; set; } = 4;

	/// <summary>
	/// How many deliveries one subscription may have waiting. Past it, a new event for that subscription is
	/// dropped, so an endpoint that is down cannot grow the server's memory without end.
	/// </summary>
	public int MaxPendingPerSubscription { get; set; } = 1000;

	/// <summary>How many recent tries each subscription keeps for <c>@webhook &lt;name&gt;</c>.</summary>
	public int RecentAttempts { get; set; } = 20;

	/// <summary>
	/// Whether a subscription may post to a loopback, private or link-local address. Off by default, so
	/// a game administrator cannot use webhooks to reach the server's own network.
	/// </summary>
	public bool AllowPrivateNetworks { get; set; }

	/// <summary>Whether a subscription may post over plain <c>http</c>. Off by default: the body carries game content.</summary>
	public bool AllowHttp { get; set; }

	/// <summary>How many events one event stream may have waiting to be written before its oldest are dropped.</summary>
	public int StreamBuffer { get; set; } = 256;

	/// <summary>The wait before try <paramref name="attempt"/> (2 or more).</summary>
	public TimeSpan RetryDelay(int attempt)
	{
		var delay = FirstRetryDelay;
		for (var i = 2; i < attempt && delay < MaxRetryDelay; i++) delay *= 5;
		return delay < MaxRetryDelay ? delay : MaxRetryDelay;
	}
}
