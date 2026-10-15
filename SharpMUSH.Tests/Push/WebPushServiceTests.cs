using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Push;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Push;

/// <summary>
/// <see cref="WebPushService"/> against a stand-in push service: what it sends, how it reads the answer, and when it
/// sends nothing. The store is an in-memory one and the key lives in a substituted secret store.
/// </summary>
public class WebPushServiceTests
{
	private static readonly DBRef Character = new(42, 1_700_000_000_000);

	private sealed class Answering(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
	{
		public List<(HttpRequestMessage Request, byte[] Body)> Received { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Received.Add((request, await request.Content!.ReadAsByteArrayAsync(cancellationToken)));
			return answer(request);
		}
	}

	private sealed class MemoryStore : IPushSubscriptionStore
	{
		public Dictionary<string, PushSubscription> Rows { get; } = [];

		public ValueTask<PushSubscriptionSaveOutcome> SavePushSubscriptionAsync(PushSubscription subscription, int maxPerAccount,
			CancellationToken cancellationToken = default)
		{
			var own = Rows.TryGetValue(subscription.Id, out var previous) && previous.AccountId == subscription.AccountId;
			if (!own && Rows.Values.Count(row => row.AccountId == subscription.AccountId) >= maxPerAccount)
			{
				return ValueTask.FromResult(PushSubscriptionSaveOutcome.AccountFull);
			}

			var replaced = Rows.Remove(subscription.Id);
			Rows[subscription.Id] = subscription;
			return ValueTask.FromResult(replaced ? PushSubscriptionSaveOutcome.Replaced : PushSubscriptionSaveOutcome.Added);
		}

		public ValueTask<Found<PushSubscription>> GetPushSubscriptionAsync(string id, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<Found<PushSubscription>>(Rows.TryGetValue(id, out var row) ? row : new NotFound());

		public ValueTask<IReadOnlyList<PushSubscription>> GetPushSubscriptionsAsync(string accountId,
			CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<IReadOnlyList<PushSubscription>>([.. Rows.Values.Where(row => row.AccountId == accountId)]);

		public ValueTask<bool> RemovePushSubscriptionAsync(string id, string? accountId = null,
			CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(Rows.TryGetValue(id, out var row) && (accountId is null || row.AccountId == accountId)
				&& Rows.Remove(id));

		public ValueTask RecordPushResultAsync(string id, DateTimeOffset at, string? failure,
			CancellationToken cancellationToken = default)
		{
			if (Rows.TryGetValue(id, out var row))
			{
				Rows[id] = failure is null
					? row with { LastSuccessAt = at, LastFailure = null }
					: row with { LastFailureAt = at, LastFailure = failure };
			}

			return ValueTask.CompletedTask;
		}
	}

	private sealed record Rig(WebPushService Service, MemoryStore Store, Answering PushService, ISecretStore Secrets,
		ECDiffieHellman Browser, byte[] BrowserPublic, byte[] Auth) : IDisposable
	{
		public void Dispose()
		{
			Service.Dispose();
			Browser.Dispose();
		}
	}

	private static Rig Build(Func<HttpRequestMessage, HttpResponseMessage> answer, int pushLimit = 10, int queueCapacity = 10_000)
	{
		var secrets = Substitute.For<ISecretStore>();
		var stored = VapidKeys.Generate().Export();
		secrets.GetAsync(VapidKeys.SecretName, Arg.Any<CancellationToken>()).Returns(new FoundResult<string>(stored));

		var pushService = new Answering(answer);
		var clients = Substitute.For<IHttpClientFactory>();
		clients.CreateClient(WebPushService.HttpClientName).Returns(_ => new HttpClient(pushService, disposeHandler: false));

		var config = ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");
		var game = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		game.CurrentValue.Returns(config with { Limit = config.Limit with { PushLimit = (uint)pushLimit } });

		var store = new MemoryStore();
		var service = new WebPushService(secrets, store, clients, game,
			Options.Create(new WebPushOptions { Subject = "mailto:admin@example.com", QueueCapacity = queueCapacity }), TimeProvider.System,
			NullLogger<WebPushService>.Instance);

		var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
		return new Rig(service, store, pushService, secrets, browser, VapidKeys.PublicPoint(browser.ExportParameters(false)),
			RandomNumberGenerator.GetBytes(16));
	}

	private static string NewEndpoint() => $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}";

	private static async Task<PushSubscription> SubscribeAsync(Rig rig, string account = "node_accounts/7")
		=> (await rig.Service.SubscribeAsync(account, NewEndpoint(),
			VapidKeys.Base64Url(rig.BrowserPublic), VapidKeys.Base64Url(rig.Auth),
			"Mozilla/5.0 (X11; Linux x86_64) Chrome/130.0 Safari/537.36")).Expect<PushSubscription>();

	[Test]
	public async Task ATestIsEncryptedSignedAndRecorded()
	{
		using var rig = Build(_ => new HttpResponseMessage(HttpStatusCode.Created));
		var subscription = await SubscribeAsync(rig);

		var sent = await rig.Service.SendTestAsync(subscription.AccountId, subscription.Id, Character);

		await Assert.That(sent.Value).IsTypeOf<Success>();
		var (request, body) = rig.PushService.Received.Single();
		await Assert.That(request.RequestUri!.ToString()).IsEqualTo(subscription.Endpoint);
		await Assert.That(request.Content!.Headers.ContentEncoding.Single()).IsEqualTo("aes128gcm");
		await Assert.That(request.Headers.GetValues("TTL").Single()).IsEqualTo("86400");
		await Assert.That(request.Headers.GetValues("Urgency").Single()).IsEqualTo("normal");
		await Assert.That(request.Headers.GetValues("Authorization").Single()).StartsWith("vapid t=");

		var payload = JsonDocument.Parse(PushTestBrowser.Decrypt(body, rig.Browser, rig.BrowserPublic, rig.Auth)).RootElement;
		await Assert.That(payload.GetProperty("body").GetString()).IsEqualTo("Notifications are on for this device.");
		await Assert.That(payload.GetProperty("path").GetString()).IsEqualTo("/settings/notifications");
		await Assert.That(payload.GetProperty("character").GetString()).IsEqualTo(Character.ToString());
		await Assert.That(rig.Store.Rows[subscription.Id].LastSuccessAt).IsNotNull();
		await Assert.That(subscription.Device).IsEqualTo("Chrome on Linux");
	}

	[Test]
	public async Task ASubscriptionThePushServiceDroppedIsRemoved()
	{
		using var rig = Build(_ => new HttpResponseMessage(HttpStatusCode.Gone));
		var subscription = await SubscribeAsync(rig);

		var sent = await rig.Service.SendTestAsync(subscription.AccountId, subscription.Id, Character);

		await Assert.That(sent.Value).IsTypeOf<Error<string>>();
		await Assert.That(rig.Store.Rows).IsEmpty();
	}

	[Test]
	public async Task AnUnreachablePushServiceIsRecordedNotThrown()
	{
		using var rig = Build(_ => throw new HttpRequestException("connection refused"));
		var subscription = await SubscribeAsync(rig);

		var sent = await rig.Service.SendTestAsync(subscription.AccountId, subscription.Id, Character);

		await Assert.That(sent.Value is Error<string> { Value: "unreachable" }).IsTrue();
		await Assert.That(rig.Store.Rows[subscription.Id].LastFailure).IsEqualTo("unreachable");
	}

	[Test]
	public async Task AnotherAccountsDeviceIsNotFound()
	{
		using var rig = Build(_ => new HttpResponseMessage(HttpStatusCode.Created));
		var subscription = await SubscribeAsync(rig);

		var sent = await rig.Service.SendTestAsync("node_accounts/8", subscription.Id, Character);

		await Assert.That(sent.Value).IsTypeOf<NotFound>();
		await Assert.That(rig.PushService.Received).IsEmpty();
	}

	[Test]
	public async Task AnUnknownPushServiceIsRefused()
	{
		using var rig = Build(_ => new HttpResponseMessage(HttpStatusCode.Created));

		var refused = await rig.Service.SubscribeAsync("node_accounts/7", "https://push.example.org/abc",
			VapidKeys.Base64Url(rig.BrowserPublic), VapidKeys.Base64Url(rig.Auth), null);

		await Assert.That(refused.Value).IsTypeOf<Error<string>>();
		await Assert.That(rig.Store.Rows).IsEmpty();
	}

	[Test]
	public async Task NoDeviceIsNoneAndPastTheLimitIsLimited()
	{
		using var rig = Build(_ => new HttpResponseMessage(HttpStatusCode.Created), pushLimit: 2);
		var message = new PushMessage("node_accounts/7", Character, "Page from Tobin", "", "/play", "page-tobin", false);

		var none = await rig.Service.SendAsync(message);
		await SubscribeAsync(rig);
		var first = await rig.Service.SendAsync(message);
		var second = await rig.Service.SendAsync(message);
		var third = await rig.Service.SendAsync(message);

		await Assert.That(none).IsEqualTo(PushOutcome.None);
		await Assert.That(first).IsEqualTo(PushOutcome.Sent);
		await Assert.That(second).IsEqualTo(PushOutcome.Sent);
		await Assert.That(third).IsEqualTo(PushOutcome.Limited);
	}

	[Test]
	public async Task ALongMessageIsCutToFitOneRecord()
	{
		var payload = WebPushService.Payload("Mail from Tobin", new string('"', 10_000), "/mail/3", Character, "mail");

		var json = JsonDocument.Parse(payload).RootElement;
		await Assert.That(payload.Length).IsLessThanOrEqualTo(WebPushEncryption.MaxPlaintext);
		await Assert.That(json.GetProperty("title").GetString()).IsEqualTo("Mail from Tobin");
		await Assert.That(json.GetProperty("body").GetString()!).EndsWith("…");
		await Assert.That(json.GetProperty("tag").GetString()).IsEqualTo($"{Character}/mail");
	}

	[Test]
	public async Task ATopicIsAtMost32Base64UrlCharacters()
	{
		var topic = WebPushService.TopicHeader(Character, new string('x', 500));

		await Assert.That(topic.Length).IsEqualTo(32);
		await Assert.That(topic.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')).IsTrue();
		await Assert.That(WebPushService.TopicHeader(new DBRef(43, 1), "x")).IsNotEqualTo(WebPushService.TopicHeader(Character, "x"));
	}

	/// <summary>The service is not started, so nothing drains the queue: a full one is limited, not sent.</summary>
	[Test]
	public async Task AFullQueueIsLimited()
	{
		using var rig = Build(_ => new HttpResponseMessage(HttpStatusCode.Created), queueCapacity: 1);
		await SubscribeAsync(rig);
		var message = new PushMessage("node_accounts/7", Character, "Page from Tobin", "", "/play", null, false);

		var first = await rig.Service.SendAsync(message);
		var second = await rig.Service.SendAsync(message);

		await Assert.That(first).IsEqualTo(PushOutcome.Sent);
		await Assert.That(second).IsEqualTo(PushOutcome.Limited);
	}

	[Test]
	public async Task ARenewalMovesTheSubscriptionAndKeepsItsName()
	{
		using var rig = Build(_ => new HttpResponseMessage(HttpStatusCode.Created));
		var old = await SubscribeAsync(rig);
		var endpoint = NewEndpoint();

		var renewed = await rig.Service.RenewAsync(old.Endpoint, endpoint, VapidKeys.Base64Url(rig.BrowserPublic),
			VapidKeys.Base64Url(rig.Auth));

		var stored = renewed.Expect<PushSubscription>();
		await Assert.That(stored.Endpoint).IsEqualTo(endpoint);
		await Assert.That(stored.Device).IsEqualTo(old.Device);
		await Assert.That(rig.Store.Rows.Keys).IsEquivalentTo([stored.Id]);
	}

	[Test]
	public async Task ABadRenewalLeavesTheOldSubscription()
	{
		using var rig = Build(_ => new HttpResponseMessage(HttpStatusCode.Created));
		var old = await SubscribeAsync(rig);

		var refused = await rig.Service.RenewAsync(old.Endpoint, "https://push.example.org/abc",
			VapidKeys.Base64Url(rig.BrowserPublic), VapidKeys.Base64Url(rig.Auth));

		await Assert.That(refused.Value).IsTypeOf<Error<string>>();
		await Assert.That(rig.Store.Rows.Keys).IsEquivalentTo([old.Id]);
	}

	[Test]
	public async Task ARenewalUnderAReplacedKeyIsDropped()
	{
		using var rig = Build(_ => new HttpResponseMessage(HttpStatusCode.Created));
		var old = await SubscribeAsync(rig);
		rig.Secrets.GetAsync(VapidKeys.SecretName, Arg.Any<CancellationToken>())
			.Returns(new FoundResult<string>(VapidKeys.Generate().Export()));

		var refused = await rig.Service.RenewAsync(old.Endpoint, NewEndpoint(), VapidKeys.Base64Url(rig.BrowserPublic),
			VapidKeys.Base64Url(rig.Auth));

		await Assert.That(refused.Value).IsTypeOf<Error<string>>();
		await Assert.That(rig.Store.Rows).IsEmpty();
	}
}
