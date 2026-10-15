namespace SharpMUSH.Library.API;

/// <summary>The keys of a browser's push subscription, as <c>PushSubscription.toJSON()</c> gives them.</summary>
/// <param name="P256dh">The browser's public key, base64url.</param>
/// <param name="Auth">The browser's authentication secret, base64url.</param>
public sealed record PushSubscriptionKeys(string P256dh, string Auth);

/// <summary>A browser's push subscription, sent to <c>POST api/push/subscriptions</c>.</summary>
/// <param name="Endpoint">The push service URL the browser gave.</param>
/// <param name="Keys">Its keys.</param>
public sealed record PushSubscribeRequest(string Endpoint, PushSubscriptionKeys Keys);

/// <summary>
/// A subscription the browser replaced, sent by the service worker to <c>POST api/push/renew</c>. It has no
/// session; the old endpoint, which only the browser and the server know, is the proof.
/// </summary>
public sealed record PushRenewRequest(string OldEndpoint, string Endpoint, PushSubscriptionKeys Keys);

/// <summary>The server's public VAPID key, base64url, for <c>pushManager.subscribe()</c>.</summary>
public sealed record PushKeyResponse(string PublicKey);

/// <summary>One of the account's devices with notifications on.</summary>
/// <param name="Id">The subscription's id.</param>
/// <param name="Device">What it is called, such as "Firefox on Linux".</param>
/// <param name="Endpoint">Its endpoint, so a browser can tell which entry is its own.</param>
/// <param name="CreatedAt">When notifications were turned on.</param>
/// <param name="LastSuccessAt">When a notification last reached its push service.</param>
/// <param name="LastFailureAt">When one last failed.</param>
/// <param name="LastFailure">Why it last failed.</param>
/// <param name="Current">False when it was made with a key the server has since replaced: it receives nothing until turned on again.</param>
public sealed record PushDeviceResponse(
	string Id,
	string Device,
	string Endpoint,
	DateTimeOffset CreatedAt,
	DateTimeOffset? LastSuccessAt,
	DateTimeOffset? LastFailureAt,
	string? LastFailure,
	bool Current);
