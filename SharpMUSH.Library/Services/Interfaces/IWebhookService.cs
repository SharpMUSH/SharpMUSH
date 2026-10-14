using SharpMUSH.Library.Definitions;
using System.Threading.Channels;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Webhooks;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Outbound webhooks: endpoints outside the game that hear its events, each delivery signed, retried with
/// backoff and dropped after <see cref="WebhookOptions.MaxAttempts"/> tries. Managed with <c>@webhook</c>
/// and the admin API; every event the event service raises is offered here first.
/// </summary>
public interface IWebhookService
{
	/// <summary>Every subscription, by name.</summary>
	ValueTask<IReadOnlyList<WebhookSubscription>> ListAsync();

	/// <summary>The subscription called <paramref name="name"/>, whatever its case.</summary>
	ValueTask<Found<WebhookSubscription>> GetAsync(string name);

	/// <summary>
	/// Adds a subscription with a new secret. <paramref name="url"/> may be empty for one heard only through
	/// its event stream. <paramref name="createdBy"/> names who made it: an objid, or a portal account, and
	/// <paramref name="reach"/> what they may be told about (<see cref="WebhookSubscription.Reach"/>). Fails on a
	/// taken or malformed name, an unusable address or a malformed pattern.
	/// </summary>
	ValueTask<Result<WebhookSubscription>> CreateAsync(string name, string? url, IReadOnlyList<string> events, EventPrivacy reach,
		string? createdBy);

	/// <summary>Points the subscription at <paramref name="url"/>, or at nothing (stream only).</summary>
	ValueTask<FoundResult<WebhookSubscription>> SetUrlAsync(string name, string? url);

	/// <summary>
	/// Replaces the event patterns the subscription hears, and its <see cref="WebhookSubscription.Reach"/> with
	/// <paramref name="reach"/>: the reach of whoever set them.
	/// </summary>
	ValueTask<FoundResult<WebhookSubscription>> SetEventsAsync(string name, IReadOnlyList<string> events, EventPrivacy reach);

	/// <summary>Turns delivery on or off. Turning it on clears a reason the server gave for turning it off.</summary>
	ValueTask<FoundResult<WebhookSubscription>> SetEnabledAsync(string name, bool enabled);

	/// <summary>
	/// Gives the subscription a new secret; deliveries still waiting are signed with it, and its open streams are
	/// closed, to connect again with the new one.
	/// </summary>
	ValueTask<FoundResult<WebhookSubscription>> RotateSecretAsync(string name);

	/// <summary>Removes the subscription. Its waiting deliveries are dropped and its streams closed.</summary>
	ValueTask<Found<WebhookSubscription>> DeleteAsync(string name);

	/// <summary>
	/// Sends the subscription a <c>WEBHOOK`TEST</c> event, whatever it hears and even while it is off, so an
	/// endpoint can be checked before anything real reaches it.
	/// </summary>
	ValueTask<FoundResult<WebhookMessage>> TestAsync(string name, DBRef? enactor);

	/// <summary>
	/// Offers an event to every enabled subscription that hears it. Returns at once: deliveries are made in the
	/// background. With no subscription hearing <paramref name="eventName"/>, costs a dictionary lookup.
	/// </summary>
	ValueTask PublishAsync(string eventName, DBRef? enactor, IReadOnlyList<string> args);

	/// <summary>What the subscription's deliveries have come to since the server started.</summary>
	WebhookStatus Status(string name);

	/// <summary>
	/// Adds an event stream to the subscription: every event it hears is also written to
	/// <paramref name="writer"/>, which is completed when the subscription is deleted or its secret rotated. Disposing the result
	/// removes the stream.
	/// </summary>
	IDisposable OpenStream(string name, ChannelWriter<WebhookMessage> writer);
}
