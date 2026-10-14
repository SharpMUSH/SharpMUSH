using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Implementation.Services.Webhooks;
using SharpMUSH.Library.DiscriminatedUnions;
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
		public WebhookDispatcher Dispatcher { get; }
		public WebhookService Webhooks { get; }

		public Fixture(Action<WebhookOptions>? configure = null)
		{
			var settings = new WebhookOptions();
			configure?.Invoke(settings);
			var options = Options.Create(settings);
			Dispatcher = new WebhookDispatcher(Endpoint, Clock, options, NullLogger<WebhookDispatcher>.Instance);
			Webhooks = new WebhookService(new MemoryServerData(), Substitute.For<IMediator>(), Dispatcher, Clock, options,
				NullLogger<WebhookService>.Instance);
		}

		public async Task<WebhookSubscription> CreateAsync(string name = "relay", string? url = Url, params string[] events)
			=> (await Webhooks.CreateAsync(name, url, events.Length == 0 ? ["PLAYER`*"] : events, "#1")).Expect<WebhookSubscription>();

		public Task<int> RunAsync() => Dispatcher.RunDueAsync(Webhooks.Current, Webhooks.MarkGoneAsync, CancellationToken.None);

		public ValueTask ConnectAsync() => Webhooks.PublishAsync("PLAYER`CONNECT", null, ["#5:100", "1", "7"]);
	}

	[Test]
	public async Task AHeardEventIsPostedOnceSignedWithItsArgumentsByName()
	{
		var fixture = new Fixture();
		var subscription = await fixture.CreateAsync();

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
			.IsEqualTo(WebhookSignature.Sign(subscription.Secret, id, timestamp, request.Body));

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
		var subscription = await fixture.CreateAsync();

		await fixture.ConnectAsync();
		await fixture.RunAsync();
		var rotated = (await fixture.Webhooks.RotateSecretAsync(subscription.Name)).Expect<WebhookSubscription>();
		fixture.Clock.Advance(TimeSpan.FromSeconds(5));
		await fixture.RunAsync();

		var retry = fixture.Endpoint.Received[1];
		await Assert.That(rotated.Secret).IsNotEqualTo(subscription.Secret);
		await Assert.That(retry.Headers[WebhookSignature.SignatureHeader]).IsEqualTo(WebhookSignature.Sign(rotated.Secret,
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
	[Arguments("bad name", Url, "letters, digits")]
	[Arguments("plain", "http://hooks.example.com/game", "must use https")]
	[Arguments("local", "https://127.0.0.1/hook", "private network")]
	[Arguments("internal", "https://10.0.0.5/hook", "private network")]
	[Arguments("relative", "/hook", "full https:// URL")]
	public async Task AnUnusableNameOrAddressIsRefused(string name, string url, string reason)
	{
		var fixture = new Fixture();

		var result = await fixture.Webhooks.CreateAsync(name, url, ["*"], "#1");

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

		var result = await fixture.Webhooks.CreateAsync("lan", "http://127.0.0.1:9000/hook", ["*"], "#1");

		await Assert.That(result.Expect<WebhookSubscription>().Url).IsEqualTo("http://127.0.0.1:9000/hook");
	}

	[Test]
	public async Task NamesAreUniqueWithoutRegardToCase()
	{
		var fixture = new Fixture();
		await fixture.CreateAsync("Relay");

		var second = await fixture.Webhooks.CreateAsync("relay", Url, ["*"], "#1");

		await Assert.That(second.Expect<Error<string>>().Value).Contains("already");
	}
}
