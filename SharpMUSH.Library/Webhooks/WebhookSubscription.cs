namespace SharpMUSH.Library.Webhooks;

/// <summary>
/// An endpoint outside the game that hears the game's events: what it hears, where they are sent and
/// the secret that signs them. Kept in the world, in <see cref="WebhooksData"/>.
/// </summary>
public sealed record WebhookSubscription
{
	/// <summary>The subscription's key, unique without regard to case: letters, digits, <c>-</c> and <c>_</c>.</summary>
	public string Name { get; init; } = string.Empty;

	/// <summary>
	/// Where each event is posted. Empty for a subscription heard only through its event stream
	/// (<c>api/webhooks/&lt;name&gt;/stream</c>), for a tool that has no address of its own.
	/// </summary>
	public string? Url { get; init; }

	/// <summary>The events delivered: event names or patterns (<c>*</c>, <c>?</c>), such as <c>PLAYER`*</c>. None delivers nothing.</summary>
	public IReadOnlyList<string> Events { get; init; } = [];

	/// <summary>The signing secret, <c>whsec_</c> and the key in base64 (<see cref="WebhookSignature"/>).</summary>
	public string Secret { get; init; } = string.Empty;

	/// <summary>Whether events are delivered. A disabled subscription keeps its settings and hears nothing.</summary>
	public bool Enabled { get; init; } = true;

	/// <summary>Why the server disabled it (an endpoint answering 410 Gone); empty when someone did, or it is on.</summary>
	public string? DisabledReason { get; init; }

	/// <summary>The objid of whoever created it.</summary>
	public string? CreatedBy { get; init; }

	public DateTimeOffset CreatedAt { get; init; }

	/// <summary>Whether <paramref name="eventName"/> is one this subscription hears.</summary>
	public bool Hears(string eventName) => Events.Any(pattern => WebhookEventPattern.Matches(pattern, eventName));
}

/// <summary>Every webhook subscription in the world, stored as one server document.</summary>
public sealed class WebhooksData
{
	public List<WebhookSubscription> Subscriptions { get; set; } = [];
}
