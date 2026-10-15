using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Mediator;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Implementation.Services.Webhooks;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Webhooks;

namespace SharpMUSH.Tests.Services.Webhooks;

/// <summary>
/// What a subscription's endpoint receives: one signed POST per event it hears, the same delivery tried again
/// with backoff when the endpoint fails, dropped after the last try, and never sent once the subscription is
/// off or gone.
/// </summary>
public class WebhookDeliveryTests
{
	private const string Url = "https://hooks.example.com/game";

	private sealed class Fixture
	{
		public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
		public RecordingEndpoint Endpoint { get; } = new();
		public static SecretAuthor Author { get; } = new("#1:1", "God");
		public MemoryServerData World { get; } = new();

		/// <summary>The environment the secret store reads overrides from.</summary>
		public Dictionary<string, string> Environment { get; } = [];

		public SecretStore Secrets { get; }
		public WebhookDispatcher Dispatcher { get; }
		public WebhookService Webhooks { get; }

		public Fixture(Action<WebhookOptions>? configure = null)
		{
			var settings = new WebhookOptions();
			configure?.Invoke(settings);
			var options = Options.Create(settings);
			Secrets = new SecretStore(new MemoryServerData(), new EphemeralDataProtectionProvider(), [],
				new SecretStoreOptions(Path.Join(Path.GetTempPath(), $"no-secrets-{Guid.NewGuid():N}"), Environment.GetValueOrDefault),
				Clock, NullLogger<SecretStore>.Instance);
			Dispatcher = new WebhookDispatcher(Endpoint, Secrets, Clock, options, NullLogger<WebhookDispatcher>.Instance);
			Webhooks = new WebhookService(World, Substitute.For<IMediator>(), Dispatcher, Secrets, Clock, options,
				NullLogger<WebhookService>.Instance);
		}

		public async Task<WebhookIssued> IssueAsync(string name = "relay", string? url = Url, params string[] events)
			=> (await Webhooks.CreateAsync(name, url, events.Length == 0 ? ["PLAYER`*"] : events, EventPrivacy.Private, "#1", Author))
				.Expect<WebhookIssued>();

		public async Task<WebhookSubscription> CreateAsync(string name = "relay", string? url = Url, params string[] events)
			=> (await IssueAsync(name, url, events)).Subscription;

		public Task<int> RunAsync() => Dispatcher.RunDueAsync(Webhooks.Current, Webhooks.MarkGoneAsync, CancellationToken.None);

		public ValueTask ConnectAsync() => Webhooks.PublishAsync("PLAYER`CONNECT", null, ["#5:100", "1", "7"]);
	}

	[Test]
	public async Task AHeardEventIsPostedOnceSignedWithItsArgumentsByName()
	{
		var fixture = new Fixture();
		var (subscription, secret, _) = await fixture.IssueAsync();

		await fixture.ConnectAsync();
		var sent = await fixture.RunAsync();

		await Assert.That(sent).IsEqualTo(1);
		var request = fixture.Endpoint.Received.Single();
		await Assert.That(request.Uri!.ToString()).IsEqualTo(Url);
		await Assert.That(request.Headers["content-type"]).StartsWith("application/json");

		var id = request.Headers[WebhookSignature.IdHeader];
		var timestamp = long.Parse(request.Headers[WebhookSignature.TimestampHeader]);
		await Assert.That(id).StartsWith("msg_");
		await Assert.That(timestamp).IsEqualTo(fixture.Clock.GetUtcNow().ToUnixTimeSeconds());
		await Assert.That(request.Headers[WebhookSignature.SignatureHeader])
			.IsEqualTo(WebhookSignature.Sign(secret!, id, timestamp, request.Body));

		using var body = JsonDocument.Parse(request.Body);
		var root = body.RootElement;
		await Assert.That(root.GetProperty("type").GetString()).IsEqualTo("PLAYER`CONNECT");
		await Assert.That(root.GetProperty("timestamp").GetString()).IsEqualTo("2026-10-14T12:00:00.000Z");
		var arguments = root.GetProperty("data").GetProperty("arguments");
		await Assert.That(arguments.GetProperty("objid").GetString()).IsEqualTo("#5:100");
		await Assert.That(arguments.GetProperty("connections").GetString()).IsEqualTo("1");
		await Assert.That(arguments.GetProperty("descriptor").GetString()).IsEqualTo("7");
		await Assert.That(root.GetProperty("data").GetProperty("positional").GetArrayLength()).IsEqualTo(3);
		await Assert.That(root.GetProperty("data").GetProperty("enactor").ValueKind).IsEqualTo(JsonValueKind.Null);

		var status = fixture.Webhooks.Status(subscription.Name);
		await Assert.That(status.Delivered).IsEqualTo(1);
		await Assert.That(status.Pending).IsEqualTo(0);
	}

	[Test]
	public async Task AnEventMorePrivateThanTheSubscriptionsReachIsWithheld()
	{
		var fixture = new Fixture();
		(await fixture.Webhooks.CreateAsync("relay", Url, ["*"], EventPrivacy.Staff, "#1", Fixture.Author)).Expect<WebhookIssued>();

		await fixture.ConnectAsync();
		await fixture.Webhooks.PublishAsync("LOG`ERR", null, ["disk full"]);
		await fixture.Webhooks.PublishAsync("PLUGIN`UNLISTED", null, []);

		await Assert.That(await fixture.RunAsync()).IsEqualTo(1);
		using var body = JsonDocument.Parse(fixture.Endpoint.Received.Single().Body);
		await Assert.That(body.RootElement.GetProperty("type").GetString()).IsEqualTo("PLAYER`CONNECT");
	}

	[Test]
	public async Task SettingEventsTakesTheReachOfWhoeverSetThem()
	{
		var fixture = new Fixture();
		await fixture.CreateAsync(events: "LOG`ERR");

		var narrowed = (await fixture.Webhooks.SetEventsAsync("relay", ["LOG`ERR"], EventPrivacy.Public)).Expect<WebhookSubscription>();
		await fixture.Webhooks.PublishAsync("LOG`ERR", null, ["disk full"]);

		await Assert.That(narrowed.Reach).IsEqualTo(EventPrivacy.Public);
		await Assert.That(await fixture.RunAsync()).IsEqualTo(0);
	}

	[Test]
	[Arguments(false, false, EventPrivacy.Public)]
	[Arguments(true, false, EventPrivacy.Staff)]
	[Arguments(false, true, EventPrivacy.Public)]
	[Arguments(true, true, EventPrivacy.Private)]
	public async Task ReachNeedsSeeAllForStaffEventsAndModerationAsWellForPrivateOnes(bool seesAll, bool readsPrivate,
		EventPrivacy reach)
		=> await Assert.That(WebhookSubscription.ReachOf(seesAll, readsPrivate)).IsEqualTo(reach);

	[Test]
	public async Task AnEventNoSubscriptionHearsSendsNothing()
	{
		var fixture = new Fixture();
		await fixture.CreateAsync(events: "CHANNEL`MESSAGE");

		await fixture.ConnectAsync();

		await Assert.That(await fixture.RunAsync()).IsEqualTo(0);
		await Assert.That(fixture.Endpoint.Received).IsEmpty();
	}

	[Test]
	public async Task AFailedDeliveryIsTriedAgainAfterItsWaitWithTheSameId()
	{
		var fixture = new Fixture();
		fixture.Endpoint.Answer(HttpStatusCode.InternalServerError);
		var subscription = await fixture.CreateAsync();

		await fixture.ConnectAsync();
		await fixture.RunAsync();
		fixture.Clock.Advance(TimeSpan.FromSeconds(4));
		var early = await fixture.RunAsync();
		fixture.Clock.Advance(TimeSpan.FromSeconds(1));
		var due = await fixture.RunAsync();

		await Assert.That(early).IsEqualTo(0);
		await Assert.That(due).IsEqualTo(1);
		var ids = fixture.Endpoint.Received.Select(request => request.Headers[WebhookSignature.IdHeader]).ToList();
		await Assert.That(ids.Distinct().Count()).IsEqualTo(1);
		var status = fixture.Webhooks.Status(subscription.Name);
		await Assert.That(status.Delivered).IsEqualTo(1);
		await Assert.That(status.FailedAttempts).IsEqualTo(1);
		await Assert.That(status.Recent.Select(attempt => attempt.Outcome))
			.IsEquivalentTo(new[] { WebhookAttemptOutcome.Delivered, WebhookAttemptOutcome.Retrying });
	}

	[Test]
	public async Task ADeliveryIsDroppedAfterItsLastTry()
	{
		var fixture = new Fixture(options => options.MaxAttempts = 3);
		fixture.Endpoint.Otherwise = HttpStatusCode.ServiceUnavailable;
		var subscription = await fixture.CreateAsync();

		await fixture.ConnectAsync();
		for (var i = 0; i < 5; i++)
		{
			await fixture.RunAsync();
			fixture.Clock.Advance(TimeSpan.FromHours(3));
		}

		await Assert.That(fixture.Endpoint.Received.Count).IsEqualTo(3);
		var status = fixture.Webhooks.Status(subscription.Name);
		await Assert.That(status.Dropped).IsEqualTo(1);
		await Assert.That(status.FailedAttempts).IsEqualTo(3);
		await Assert.That(status.Pending).IsEqualTo(0);
		await Assert.That(status.Recent[0].Outcome).IsEqualTo(WebhookAttemptOutcome.Dropped);
		await Assert.That(status.Recent[0].StatusCode).IsEqualTo(503);
	}

	[Test]
	public async Task AnEndpointAnsweringGoneTurnsItsSubscriptionOff()
	{
		var fixture = new Fixture();
		fixture.Endpoint.Answer(HttpStatusCode.Gone);
		var subscription = await fixture.CreateAsync();

		await fixture.ConnectAsync();
		await fixture.RunAsync();
		await fixture.ConnectAsync();

		var after = (await fixture.Webhooks.GetAsync(subscription.Name)).Expect<WebhookSubscription>();
		await Assert.That(after.Enabled).IsFalse();
		await Assert.That(after.DisabledReason).Contains("410");
		await Assert.That(await fixture.RunAsync()).IsEqualTo(0);
		await Assert.That(fixture.Endpoint.Received.Count).IsEqualTo(1);
	}

	[Test]
	public async Task ASubscriptionWithTooManyWaitingDropsNewEvents()
	{
		var fixture = new Fixture(options => options.MaxPendingPerSubscription = 2);
		var subscription = await fixture.CreateAsync();

		await fixture.ConnectAsync();
		await fixture.ConnectAsync();
		await fixture.ConnectAsync();

		var status = fixture.Webhooks.Status(subscription.Name);
		await Assert.That(status.Pending).IsEqualTo(2);
		await Assert.That(status.Dropped).IsEqualTo(1);
		await Assert.That(await fixture.RunAsync()).IsEqualTo(2);
	}

	[Test]
	public async Task TurningASubscriptionOffDropsWhatItHasWaiting()
	{
		var fixture = new Fixture();
		fixture.Endpoint.Answer(HttpStatusCode.InternalServerError);
		var subscription = await fixture.CreateAsync();

		await fixture.ConnectAsync();
		await fixture.RunAsync();
		await fixture.Webhooks.SetEnabledAsync(subscription.Name, false);
		fixture.Clock.Advance(TimeSpan.FromMinutes(1));
		await fixture.RunAsync();

		await Assert.That(fixture.Endpoint.Received.Count).IsEqualTo(1);
		var status = fixture.Webhooks.Status(subscription.Name);
		await Assert.That(status.Dropped).IsEqualTo(1);
		await Assert.That(status.Recent[0].Error).IsEqualTo("subscription is off");
	}

	[Test]
	public async Task AWaitingDeliveryIsSignedWithTheSecretItIsSentUnder()
	{
		var fixture = new Fixture();
		fixture.Endpoint.Answer(HttpStatusCode.InternalServerError);
		var (subscription, secret, _) = await fixture.IssueAsync();

		await fixture.ConnectAsync();
		await fixture.RunAsync();
		var rotated = (await fixture.Webhooks.RotateSecretAsync(subscription.Name, Fixture.Author)).Expect<WebhookIssued>();
		fixture.Clock.Advance(TimeSpan.FromSeconds(5));
		await fixture.RunAsync();

		var retry = fixture.Endpoint.Received[1];
		await Assert.That(rotated.Secret).IsNotEqualTo(secret);
		await Assert.That(retry.Headers[WebhookSignature.SignatureHeader]).IsEqualTo(WebhookSignature.Sign(rotated.Secret!,
			retry.Headers[WebhookSignature.IdHeader], long.Parse(retry.Headers[WebhookSignature.TimestampHeader]), retry.Body));
	}

	[Test]
	public async Task ATestIsSentWhateverTheSubscriptionHearsAndEvenWhileOff()
	{
		var fixture = new Fixture();
		var subscription = await fixture.CreateAsync(events: "CHANNEL`MESSAGE");
		await fixture.Webhooks.SetEnabledAsync(subscription.Name, false);

		var sent = (await fixture.Webhooks.TestAsync(subscription.Name, null)).Expect<WebhookMessage>();
		await fixture.RunAsync();

		await Assert.That(sent.EventName).IsEqualTo(WebhookService.TestEvent);
		await Assert.That(fixture.Endpoint.Received.Single().Headers[WebhookSignature.IdHeader]).IsEqualTo(sent.Id);
	}

	[Test]
	public async Task AStreamHearsWhatItsSubscriptionHearsAndClosesWhenItIsDeleted()
	{
		var fixture = new Fixture();
		var subscription = await fixture.CreateAsync(url: null);
		var stream = Channel.CreateUnbounded<WebhookMessage>();

		using (fixture.Webhooks.OpenStream(subscription.Name, stream.Writer))
		{
			await Assert.That(fixture.Webhooks.Status(subscription.Name).Streams).IsEqualTo(1);
			await fixture.ConnectAsync();
			await fixture.Webhooks.DeleteAsync(subscription.Name);
		}

		var heard = await stream.Reader.ReadAllAsync().ToListAsync();
		await Assert.That(heard.Single().EventName).IsEqualTo("PLAYER`CONNECT");
		await Assert.That(stream.Reader.Completion.IsCompleted).IsTrue();
		// No address: nothing was queued to be posted.
		await Assert.That(await fixture.RunAsync()).IsEqualTo(0);
	}

	[Test]
	[Arguments("bad name", Url, "letters and digits")]
	[Arguments("mail-relay", Url, "letters and digits")]
	[Arguments("plain", "http://hooks.example.com/game", "must use https")]
	[Arguments("local", "https://127.0.0.1/hook", "private network")]
	[Arguments("internal", "https://10.0.0.5/hook", "private network")]
	[Arguments("relative", "/hook", "full https:// URL")]
	public async Task AnUnusableNameOrAddressIsRefused(string name, string url, string reason)
	{
		var fixture = new Fixture();

		var result = await fixture.Webhooks.CreateAsync(name, url, ["*"], EventPrivacy.Private, "#1", Fixture.Author);

		await Assert.That(result.Expect<Error<string>>().Value).Contains(reason);
	}

	[Test]
	public async Task TheServerCanAllowPlainHttpOnAPrivateNetwork()
	{
		var fixture = new Fixture(options =>
		{
			options.AllowHttp = true;
			options.AllowPrivateNetworks = true;
		});

		var result = await fixture.Webhooks.CreateAsync("lan", "http://127.0.0.1:9000/hook", ["*"], EventPrivacy.Private, "#1", Fixture.Author);

		await Assert.That(result.Expect<WebhookIssued>().Subscription.Url).IsEqualTo("http://127.0.0.1:9000/hook");
	}

	[Test]
	public async Task NamesAreUniqueWithoutRegardToCase()
	{
		var fixture = new Fixture();
		await fixture.CreateAsync("Relay");

		var second = await fixture.Webhooks.CreateAsync("relay", Url, ["*"], EventPrivacy.Private, "#1", Fixture.Author);

		await Assert.That(second.Expect<Error<string>>().Value).Contains("already");
	}

	[Test]
	public async Task TheSecretIsKeptInTheSecretStoreAndNotInTheWorld()
	{
		var fixture = new Fixture();
		var (subscription, secret, overriddenBy) = await fixture.IssueAsync("Relay");

		await Assert.That(overriddenBy).IsNull();
		await Assert.That(secret).StartsWith(WebhookSignature.SecretPrefix);
		await Assert.That((await fixture.Secrets.GetAsync("webhook.relay.signing")).Expect<string>()).IsEqualTo(secret!);
		var world = System.Text.Json.JsonSerializer.Serialize(await fixture.World.GetExpandedServerDataAsync<WebhooksData>());
		await Assert.That(world).DoesNotContain(WebhookSignature.SecretPrefix);

		await fixture.Webhooks.DeleteAsync(subscription.Name);

		(await fixture.Secrets.GetAsync("webhook.relay.signing")).Expect<NotFound>();
	}

	[Test]
	public async Task AMissingSecretFailsTheTryUntilANewOneIsMade()
	{
		var fixture = new Fixture();
		var subscription = await fixture.CreateAsync();
		await fixture.Secrets.ClearAsync(WebhookSubscription.SecretName(subscription.Name));

		await fixture.ConnectAsync();
		await fixture.RunAsync();

		await Assert.That(fixture.Endpoint.Received).IsEmpty();
		var failed = fixture.Webhooks.Status(subscription.Name).Recent.Single();
		await Assert.That(failed.Outcome).IsEqualTo(WebhookAttemptOutcome.Retrying);
		await Assert.That(failed.Error).Contains("webhook.relay.signing is not set");

		var rotated = (await fixture.Webhooks.RotateSecretAsync(subscription.Name, Fixture.Author)).Expect<WebhookIssued>();
		fixture.Clock.Advance(TimeSpan.FromSeconds(5));
		await fixture.RunAsync();

		var request = fixture.Endpoint.Received.Single();
		await Assert.That(request.Headers[WebhookSignature.SignatureHeader]).IsEqualTo(WebhookSignature.Sign(rotated.Secret!,
			request.Headers[WebhookSignature.IdHeader], long.Parse(request.Headers[WebhookSignature.TimestampHeader]), request.Body));
	}

	[Test]
	public async Task AnOverrideSignsInsteadAndNoSecretIsShown()
	{
		var fixture = new Fixture();
		var overriding = WebhookSignature.NewSecret();
		fixture.Environment["SHARPMUSH_SECRET_WEBHOOK_RELAY_SIGNING"] = overriding;

		var issued = await fixture.IssueAsync();
		await fixture.ConnectAsync();
		await fixture.RunAsync();

		await Assert.That(issued.Secret).IsNull();
		await Assert.That(issued.OverriddenBy).IsEqualTo("SHARPMUSH_SECRET_WEBHOOK_RELAY_SIGNING");
		var request = fixture.Endpoint.Received.Single();
		await Assert.That(request.Headers[WebhookSignature.SignatureHeader]).IsEqualTo(WebhookSignature.Sign(overriding,
			request.Headers[WebhookSignature.IdHeader], long.Parse(request.Headers[WebhookSignature.TimestampHeader]), request.Body));
	}
}
