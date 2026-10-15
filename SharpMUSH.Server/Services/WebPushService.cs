using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Push;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>The <c>WebPush</c> section of the server's configuration.</summary>
public sealed class WebPushOptions
{
	public const string Section = "WebPush";

	/// <summary>
	/// How a push service reaches the game's operator, a <c>mailto:</c> or <c>https:</c> URL, sent with every push.
	/// Unset, <c>mud_url</c> is used when it is an https address.
	/// </summary>
	public string? Subject { get; set; }

	/// <summary>
	/// Hosts besides the browsers' own push services that a subscription may name, for a browser
	/// <see cref="PushEndpoints.KnownHosts"/> does not list. <c>*.example.com</c> takes every host under it.
	/// </summary>
	public List<string> AdditionalHosts { get; set; } = [];

	/// <summary>How many pushes may wait to be sent; past it <c>@push</c> outputs <c>limited</c>.</summary>
	public int QueueCapacity { get; set; } = 10_000;
}

/// <inheritdoc cref="IWebPushService" />
/// <remarks>
/// Sends run on this service's own loop, a few at a time, so a slow push service never holds up the queue entry
/// that ran <c>@push</c>. A 404 or 410 means the browser dropped the subscription, and it is deleted; 429 and 5xx
/// are tried once more after the delay the service asks for (at most a minute); every result is recorded on the
/// subscription.
/// </remarks>
public sealed class WebPushService(
	ISecretStore secrets,
	IPushSubscriptionStore subscriptions,
	IHttpClientFactory httpClients,
	IOptionsWrapper<SharpMUSHOptions> game,
	IOptions<WebPushOptions> options,
	TimeProvider clock,
	ILogger<WebPushService> logger) : BackgroundService, IWebPushService
{
	/// <summary>The named HTTP client pushes go through: no redirects, a short timeout.</summary>
	public const string HttpClientName = "webpush";

	/// <summary>The VAPID key's declaration, so <c>@secret</c> lists it.</summary>
	public static readonly SecretDefinition VapidSecret = new(VapidKeys.SecretName,
		"The key web push notifications are signed with. Made on first use; clearing it makes a new one, and every browser has to turn notifications on again.");

	/// <summary>How long a push service keeps an undelivered notification.</summary>
	public static readonly TimeSpan TimeToLive = TimeSpan.FromDays(1);

	private const int Senders = 4;
	private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

	private readonly Channel<PushJob> _queue = Channel.CreateBounded<PushJob>(new BoundedChannelOptions(
		Math.Max(1, options.Value.QueueCapacity))
	// Wait, not a Drop mode: only then does TryWrite report a full queue, which @push outputs as limited.
	{ FullMode = BoundedChannelFullMode.Wait });

	private readonly SemaphoreSlim _keyLock = new(1, 1);
	private readonly Lock _limitLock = new();
	private readonly Dictionary<int, Queue<long>> _recent = [];
	private (string Stored, VapidKeys Keys)? _keys;
	private bool _warnedSubject;

	private sealed record PushJob(PushSubscription Subscription, byte[] Payload, string? Topic, bool Urgent, int Attempt);

	/// <inheritdoc />
	public async ValueTask<string> PublicKeyAsync(CancellationToken cancellationToken = default)
		=> (await KeysAsync(cancellationToken)).PublicKeyText;

	/// <inheritdoc />
	public async ValueTask<Result<PushSubscription>> SubscribeAsync(string accountId, string endpoint, string p256dh,
		string auth, string? userAgent, CancellationToken cancellationToken = default)
	{
		if (Validate(endpoint, p256dh, auth) is not (byte[] key, byte[] secret))
		{
			return new Error<string>(Refusal(endpoint));
		}

		var keys = await KeysAsync(cancellationToken);
		var subscription = new PushSubscription(PushEndpoints.IdOf(endpoint), accountId, endpoint, key, secret,
			DeviceName(userAgent), PushEndpoints.KeyIdOf(keys.PublicKey), clock.GetUtcNow(), null, null, null);
		return await subscriptions.SavePushSubscriptionAsync(subscription, (int)game.CurrentValue.Limit.PushDevices,
				cancellationToken) switch
		{
			PushSubscriptionSaveOutcome.AccountFull => new Error<string>(
				$"This account already has {game.CurrentValue.Limit.PushDevices} devices with notifications on. Remove one first."),
			_ => subscription
		};
	}

	/// <inheritdoc />
	public async ValueTask<Result<PushSubscription>> RenewAsync(string oldEndpoint, string endpoint, string p256dh,
		string auth, CancellationToken cancellationToken = default)
	{
		if (await subscriptions.GetPushSubscriptionAsync(PushEndpoints.IdOf(oldEndpoint), cancellationToken) is not PushSubscription old
			|| old.Endpoint != oldEndpoint)
		{
			return new Error<string>("No such subscription.");
		}

		// A subscription made with a key the server has since replaced cannot be renewed: the browser makes its
		// replacement with the old key, which the push service would refuse every push signed with the new one. It is
		// dropped, and Settings asks for notifications to be turned on again.
		if (old.KeyId != PushEndpoints.KeyIdOf((await KeysAsync(cancellationToken)).PublicKey))
		{
			await subscriptions.RemovePushSubscriptionAsync(old.Id, old.AccountId, cancellationToken);
			return new Error<string>("The game changed its key: turn notifications on again.");
		}

		// The replacement is checked before the old one goes, so a bad renewal leaves notifications on.
		if (Validate(endpoint, p256dh, auth) is null)
		{
			return new Error<string>(Refusal(endpoint));
		}

		// The browser has already replaced the old subscription, so it goes first: renewing must not be refused for
		// an account that is full only because the dead one still counts.
		await subscriptions.RemovePushSubscriptionAsync(old.Id, old.AccountId, cancellationToken);
		return await SubscribeAsync(old.AccountId, endpoint, p256dh, auth, null, cancellationToken) switch
		{
			// The browser that renewed it is the one it was named for.
			PushSubscription stored => await RenamedAsync(stored with { Device = old.Device }, cancellationToken),
			Error<string> error => error
		};
	}

	private async ValueTask<Result<PushSubscription>> RenamedAsync(PushSubscription subscription,
		CancellationToken cancellationToken)
	{
		await subscriptions.SavePushSubscriptionAsync(subscription, (int)game.CurrentValue.Limit.PushDevices, cancellationToken);
		return subscription;
	}

	/// <inheritdoc />
	public async ValueTask<PushOutcome> SendAsync(PushMessage message, CancellationToken cancellationToken = default)
	{
		var keyId = PushEndpoints.KeyIdOf((await KeysAsync(cancellationToken)).PublicKey);
		var targets = (await subscriptions.GetPushSubscriptionsAsync(message.AccountId, cancellationToken))
			.Where(subscription => subscription.KeyId == keyId)
			.ToList();
		if (targets.Count == 0)
		{
			return PushOutcome.None;
		}

		if (!Admit(message.Character))
		{
			return PushOutcome.Limited;
		}

		var payload = Payload(message.Title, message.Body, message.Path, message.Character, message.Topic);
		var topic = message.Topic is null ? null : TopicHeader(message.Character, message.Topic);
		var queued = targets.Count(subscription => _queue.Writer.TryWrite(new PushJob(subscription, payload, topic, message.Urgent, 0)));
		return queued > 0 ? PushOutcome.Sent : PushOutcome.Limited;
	}

	/// <inheritdoc />
	public async ValueTask<FoundResult<Success>> SendTestAsync(string accountId, string subscriptionId, DBRef? character,
		CancellationToken cancellationToken = default)
	{
		if (await subscriptions.GetPushSubscriptionAsync(subscriptionId, cancellationToken) is not PushSubscription subscription
			|| subscription.AccountId != accountId)
		{
			return new NotFound();
		}

		var payload = Payload(Truncate(game.CurrentValue.Net.MudName, 60), "Notifications are on for this device.", "/settings/notifications",
			character, "test");
		return await DeliverAsync(new PushJob(subscription, payload, null, false, 0), cancellationToken) switch
		{
			Success success => success,
			Error<string> error => error
		};
	}

	/// <summary>Sends from the queue until the host stops.</summary>
	protected override Task ExecuteAsync(CancellationToken stoppingToken)
		=> Task.WhenAll(Enumerable.Range(0, Senders).Select(_ => SendLoopAsync(stoppingToken)));

	private async Task SendLoopAsync(CancellationToken stoppingToken)
	{
		try
		{
			await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
			{
				try
				{
					await DeliverAsync(job, stoppingToken);
				}
				catch (Exception exception) when (exception is not OperationCanceledException)
				{
					logger.LogWarning(exception, "Web push to subscription {Subscription} failed", job.Subscription.Id);
				}
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// Stopping: whatever is still queued is dropped, as a push service would drop it on TTL.
		}
	}

	/// <summary>
	/// Sends one job and records how the push service answered on the subscription. An error when it did not take
	/// it, including when it could not be reached; a failure is never thrown, since nobody but the log would see it.
	/// </summary>
	private async Task<Result<Success>> DeliverAsync(PushJob job, CancellationToken cancellationToken)
	{
		var keys = await KeysAsync(cancellationToken);
		var subscription = job.Subscription;
		if (!PushEndpoints.IsAllowed(subscription.Endpoint, options.Value.AdditionalHosts))
		{
			return await FailedAsync(subscription, "endpoint not allowed", cancellationToken);
		}

		var endpoint = new Uri(subscription.Endpoint);
		using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
		request.Content = new ByteArrayContent(WebPushEncryption.Encrypt(job.Payload, subscription.P256dh, subscription.Auth));
		request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
		request.Content.Headers.ContentEncoding.Add("aes128gcm");
		request.Headers.TryAddWithoutValidation("Authorization",
			keys.Authorization(endpoint, Subject(), clock.GetUtcNow().AddHours(12)));
		request.Headers.TryAddWithoutValidation("TTL", ((int)TimeToLive.TotalSeconds).ToString());
		request.Headers.TryAddWithoutValidation("Urgency", job.Urgent ? "high" : "normal");
		if (job.Topic is not null)
		{
			request.Headers.TryAddWithoutValidation("Topic", job.Topic);
		}

		HttpResponseMessage response;
		try
		{
			response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
		}
		catch (Exception exception) when (exception is HttpRequestException
			|| (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
		{
			logger.LogWarning("Web push to {Host} failed: {Reason}", endpoint.Host, exception.Message);
			return await FailedAsync(subscription, exception is TaskCanceledException ? "no answer" : "unreachable", cancellationToken);
		}

		using (response)
		{
			var status = (int)response.StatusCode;
			switch (status)
			{
				case >= 200 and < 300:
					await subscriptions.RecordPushResultAsync(subscription.Id, clock.GetUtcNow(), null, cancellationToken);
					return new Success();
				case (int)HttpStatusCode.NotFound or (int)HttpStatusCode.Gone:
					logger.LogInformation("Web push subscription {Subscription} is gone ({Status}); removed", subscription.Id, status);
					await subscriptions.RemovePushSubscriptionAsync(subscription.Id, cancellationToken: cancellationToken);
					return new Error<string>("the browser has dropped this subscription");
				case 429 or >= 500 when job.Attempt == 0:
					_ = RetryAsync(job with { Attempt = 1 }, RetryDelay(response, clock.GetUtcNow()), cancellationToken);
					return await FailedAsync(subscription, $"HTTP {status}, retrying", cancellationToken);
				default:
					return await FailedAsync(subscription, $"HTTP {status}", cancellationToken);
			}
		}
	}

	private async Task<Result<Success>> FailedAsync(PushSubscription subscription, string failure, CancellationToken cancellationToken)
	{
		await subscriptions.RecordPushResultAsync(subscription.Id, clock.GetUtcNow(), failure, cancellationToken);
		return new Error<string>(failure);
	}

	private async Task RetryAsync(PushJob job, TimeSpan delay, CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(delay, clock, cancellationToken);
			_queue.Writer.TryWrite(job);
		}
		catch (OperationCanceledException)
		{
			// The server is stopping; a retry still waiting is dropped with the rest of the queue.
		}
	}

	private static TimeSpan RetryDelay(HttpResponseMessage response, DateTimeOffset now)
	{
		var asked = response.Headers.RetryAfter switch
		{
			{ Delta: { } delta } => delta,
			{ Date: { } date } => date - now,
			_ => TimeSpan.FromSeconds(10)
		};
		return asked < TimeSpan.Zero ? TimeSpan.Zero : asked > MaxRetryDelay ? MaxRetryDelay : asked;
	}

	/// <summary>True and counted when <paramref name="character"/> has had fewer than <c>push_limit</c> pushes in the last minute.</summary>
	private bool Admit(DBRef character)
	{
		var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
		var limit = game.CurrentValue.Limit.PushLimit;
		lock (_limitLock)
		{
			if (!_recent.TryGetValue(character.Number, out var times))
			{
				_recent[character.Number] = times = new Queue<long>();
			}

			while (times.Count > 0 && now - times.Peek() >= 60_000)
			{
				times.Dequeue();
			}

			if (times.Count >= limit)
			{
				return false;
			}

			times.Enqueue(now);
			return true;
		}
	}

	private async ValueTask<VapidKeys> KeysAsync(CancellationToken cancellationToken)
	{
		var stored = await secrets.GetAsync(VapidKeys.SecretName, cancellationToken);
		if (stored is string text && _keys is { } cached && cached.Stored == text)
		{
			return cached.Keys;
		}

		await _keyLock.WaitAsync(cancellationToken);
		try
		{
			stored = await secrets.GetAsync(VapidKeys.SecretName, cancellationToken);
			if (stored is string current && VapidKeys.Parse(current) is { } parsed)
			{
				_keys = (current, parsed);
				return parsed;
			}

			if (stored is string unreadable)
			{
				logger.LogError("The {Secret} secret is not a P-256 private key; web push makes a new one", VapidKeys.SecretName);
			}

			var made = VapidKeys.Generate();
			var exported = made.Export();
			await secrets.SetAsync(VapidKeys.SecretName, exported, new SecretAuthor("#1", "SharpMUSH"), cancellationToken);
			logger.LogInformation("Made a new web push key ({Secret})", VapidKeys.SecretName);
			_keys = (exported, made);
			return made;
		}
		finally
		{
			_keyLock.Release();
		}
	}

	private string Subject()
	{
		if (options.Value.Subject is { Length: > 0 } configured)
		{
			return configured;
		}

		if (Uri.TryCreate(game.CurrentValue.Net.MudUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps)
		{
			return url.GetLeftPart(UriPartial.Authority);
		}

		if (!_warnedSubject)
		{
			_warnedSubject = true;
			logger.LogWarning("Web push has no contact address: set mud_url to the game's https address, or WebPush:Subject. Some push services refuse pushes without one");
		}

		return "mailto:postmaster@localhost";
	}

	private (byte[] Key, byte[] Secret)? Validate(string endpoint, string p256dh, string auth)
	{
		if (!PushEndpoints.IsAllowed(endpoint, options.Value.AdditionalHosts))
		{
			return null;
		}

		try
		{
			var key = Base64Url.DecodeFromChars(p256dh);
			var secret = Base64Url.DecodeFromChars(auth);
			if (key.Length != 65 || key[0] != 4 || secret.Length != 16)
			{
				return null;
			}

			using var check = ECDiffieHellman.Create(VapidKeys.FromPublicPoint(key));
			return (key, secret);
		}
		catch (Exception exception) when (exception is FormatException or CryptographicException)
		{
			return null;
		}
	}

	private string Refusal(string endpoint)
		=> PushEndpoints.IsAllowed(endpoint, options.Value.AdditionalHosts)
			? "The subscription's keys are not valid."
			: "This browser's push service is not one the game sends to.";

	/// <summary>
	/// The JSON the service worker reads: title, body, the page to open and the character to open it as. Cut to
	/// fit one record: the body first, then the title.
	/// </summary>
	public static byte[] Payload(string title, string body, string path, DBRef? character, string? tag)
	{
		var bytes = Serialize(title, body, path, character, tag);
		if (bytes.Length <= WebPushEncryption.MaxPlaintext)
		{
			return bytes;
		}

		body = Longest(body, cut => Serialize(title, cut, path, character, tag));
		title = Longest(title, cut => Serialize(cut, body, path, character, tag));
		return Serialize(title, body, path, character, tag);
	}

	/// <summary>
	/// <paramref name="text"/> cut, by <see cref="Truncate"/>, to the most characters with which <paramref name="serialize"/>
	/// still fits one record. A search, since an escaped character takes up to six bytes of JSON.
	/// </summary>
	private static string Longest(string text, Func<string, byte[]> serialize)
	{
		if (serialize(text).Length <= WebPushEncryption.MaxPlaintext)
		{
			return text;
		}

		var (fits, tooLong) = (0, text.Length);
		while (tooLong - fits > 1)
		{
			var middle = (fits + tooLong) / 2;
			(fits, tooLong) = serialize(Truncate(text, middle)).Length <= WebPushEncryption.MaxPlaintext
				? (middle, tooLong)
				: (fits, middle);
		}

		return Truncate(text, fits);
	}

	private static byte[] Serialize(string title, string body, string path, DBRef? character, string? tag)
		=> JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string?>
		{
			["title"] = title,
			["body"] = body,
			["path"] = path,
			["character"] = character?.ToString(),
			["tag"] = tag is null ? null : $"{character}/{tag}"
		});

	/// <summary><paramref name="text"/> cut to <paramref name="length"/> characters, never between a surrogate pair, with an ellipsis when cut.</summary>
	public static string Truncate(string text, int length)
	{
		if (text.Length <= length)
		{
			return text;
		}

		if (length <= 1)
		{
			return string.Empty;
		}

		var cut = length - 1;
		if (char.IsHighSurrogate(text[cut - 1]))
		{
			cut--;
		}

		return text[..cut] + "…";
	}

	/// <summary>
	/// The <c>Topic</c> header for a topic of a character's: at most 32 characters of the base64url alphabet, as
	/// RFC 8030 asks, so it is a hash.
	/// </summary>
	public static string TopicHeader(DBRef character, string topic)
		=> Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes($"{character}/{topic}")))[..32];

	/// <summary>A device's name from its user agent: the browser and the system, such as "Firefox on Linux".</summary>
	public static string DeviceName(string? userAgent)
	{
		var agent = userAgent ?? string.Empty;
		var browser = agent switch
		{
			_ when agent.Contains("Edg/", StringComparison.Ordinal) => "Edge",
			_ when agent.Contains("OPR/", StringComparison.Ordinal) => "Opera",
			_ when agent.Contains("Firefox/", StringComparison.Ordinal) => "Firefox",
			_ when agent.Contains("Chrome/", StringComparison.Ordinal) || agent.Contains("CriOS/", StringComparison.Ordinal) => "Chrome",
			_ when agent.Contains("Safari/", StringComparison.Ordinal) => "Safari",
			_ => "Browser"
		};
		var system = agent switch
		{
			_ when agent.Contains("Android", StringComparison.Ordinal) => "Android",
			_ when agent.Contains("iPhone", StringComparison.Ordinal) || agent.Contains("iPad", StringComparison.Ordinal) => "iOS",
			_ when agent.Contains("Windows", StringComparison.Ordinal) => "Windows",
			_ when agent.Contains("Mac OS", StringComparison.Ordinal) => "macOS",
			_ when agent.Contains("CrOS", StringComparison.Ordinal) => "ChromeOS",
			_ when agent.Contains("Linux", StringComparison.Ordinal) => "Linux",
			_ => null
		};
		return system is null ? browser : $"{browser} on {system}";
	}
}
