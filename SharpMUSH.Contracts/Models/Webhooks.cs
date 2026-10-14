using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Webhooks;

/// <summary>How one try at a delivery ended. Written by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WebhookAttemptOutcome>))]
public enum WebhookAttemptOutcome
{
	/// <summary>The endpoint answered 2xx.</summary>
	Delivered,

	/// <summary>The try failed and the delivery will be tried again.</summary>
	Retrying,

	/// <summary>The try failed and the delivery was given up: it was the last try, or the subscription is gone or off.</summary>
	Dropped,
}

/// <summary>One try at a delivery, as <c>@webhook &lt;name&gt;</c> shows it.</summary>
/// <param name="StatusCode">The endpoint's HTTP status; none when it never answered.</param>
/// <param name="Error">Why the try failed, when it did.</param>
public sealed record WebhookAttempt(
	string MessageId,
	string EventName,
	int Attempt,
	DateTimeOffset At,
	int? StatusCode,
	string? Error,
	TimeSpan Duration,
	WebhookAttemptOutcome Outcome);

/// <summary>What one subscription's deliveries have come to since the server started.</summary>
public sealed record WebhookStatus(
	long Delivered,
	long FailedAttempts,
	long Dropped,
	int Pending,
	int Streams,
	DateTimeOffset? LastDelivered,
	DateTimeOffset? LastFailed,
	IReadOnlyList<WebhookAttempt> Recent)
{
	public static WebhookStatus None { get; } = new(0, 0, 0, 0, 0, null, null, []);
}

/// <summary>A webhook subscription as the admin API shows it: everything but its secret.</summary>
public sealed record WebhookView(
	string Name,
	string? Url,
	IReadOnlyList<string> Events,
	bool Enabled,
	string? DisabledReason,
	string? CreatedBy,
	DateTimeOffset CreatedAt,
	WebhookStatus Status);

/// <summary>A new subscription: <see cref="Url"/> empty for one heard only through its event stream.</summary>
public sealed record WebhookCreateRequest(string Name, string? Url, IReadOnlyList<string>? Events);

/// <summary>A change to a subscription; what is left out stays as it is. An empty <see cref="Url"/> removes the address.</summary>
public sealed record WebhookUpdateRequest(string? Url, IReadOnlyList<string>? Events, bool? Enabled);

/// <summary>A subscription's signing secret, given on creation, on rotation and when asked for.</summary>
public sealed record WebhookSecret(string Name, string Secret);

/// <summary>The id of a test delivery that was sent.</summary>
public sealed record WebhookTestSent(string Name, string MessageId);
