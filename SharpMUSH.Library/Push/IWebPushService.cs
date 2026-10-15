using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Push;

/// <summary>
/// One notification for every browser an account has subscribed: what <c>@push</c> sends.
/// </summary>
/// <param name="AccountId">The account whose browsers are notified.</param>
/// <param name="Character">The character it is for, as an objid; a click opens the portal as this character.</param>
/// <param name="Title">The notification's title, plain text.</param>
/// <param name="Body">Its text, plain text; may be empty.</param>
/// <param name="Path">The portal page a click opens, starting with <c>/</c>.</param>
/// <param name="Topic">
/// When not null, a later notification with the same topic for the same character replaces this one: on the
/// device, and at the push service while it is still undelivered.
/// </param>
/// <param name="Urgent">Asks the push service to deliver at once even on a device saving power.</param>
public sealed record PushMessage(
	string AccountId,
	DBRef Character,
	string Title,
	string Body,
	string Path,
	string? Topic,
	bool Urgent);

/// <summary>What <see cref="IWebPushService.SendAsync"/> did, in the word <c>@push</c> outputs.</summary>
public enum PushOutcome
{
	/// <summary>Handed to the push service of at least one browser. <c>sent</c>.</summary>
	Sent,

	/// <summary>The account has no browser subscribed with the current key. <c>none</c>.</summary>
	None,

	/// <summary>The character had been sent <c>push_limit</c> notifications in the last minute, or the send queue is full. <c>limited</c>.</summary>
	Limited
}

/// <summary>
/// Web push: the server's VAPID key, the browsers subscribed to it, and sending to them. The engine never decides
/// what is worth a notification; <c>@push</c> calls <see cref="SendAsync"/>, and softcode calls <c>@push</c>.
/// </summary>
public interface IWebPushService
{
	/// <summary>
	/// The public VAPID key in base64url, what the portal subscribes with. The key is made and stored on first
	/// use.
	/// </summary>
	ValueTask<string> PublicKeyAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Stores a browser's subscription for an account, replacing one for the same endpoint. Refused when the
	/// endpoint is not on a push service <see cref="PushEndpoints"/> allows, the keys are malformed, or the
	/// account already has as many as it may.
	/// </summary>
	ValueTask<Result<PushSubscription>> SubscribeAsync(string accountId, string endpoint, string p256dh, string auth,
		string? userAgent, CancellationToken cancellationToken = default);

	/// <summary>
	/// Moves a subscription the browser replaced (<c>pushsubscriptionchange</c>) to its new endpoint and keys,
	/// under the same account. The old endpoint is the proof: only the browser and this server know it.
	/// </summary>
	ValueTask<Result<PushSubscription>> RenewAsync(string oldEndpoint, string endpoint, string p256dh, string auth,
		CancellationToken cancellationToken = default);

	/// <summary>Queues <paramref name="message"/> for each of the account's browsers.</summary>
	ValueTask<PushOutcome> SendAsync(PushMessage message, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sends a test notification to one of the account's subscriptions now, bypassing <c>push_limit</c> and the
	/// queue. <see cref="NotFound"/> when the account has no such subscription; an error naming what the push
	/// service answered when it did not take it.
	/// </summary>
	ValueTask<FoundResult<Success>> SendTestAsync(string accountId, string subscriptionId, DBRef? character,
		CancellationToken cancellationToken = default);
}
