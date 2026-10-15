using System.Text.Json;
using System.Threading.Channels;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Webhooks;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@webhook</c> through the command path, and a real event reaching a subscription: the engine's events
/// pass through the webhook service before the event handler.
/// </summary>
public class WebhookCommandTests : ServerTestBase
{
	private IWebhookService Webhooks => WebAppFactoryArg.Services.GetRequiredService<IWebhookService>();
	private ISecretStore Secrets => WebAppFactoryArg.Services.GetRequiredService<ISecretStore>();

	private async Task<string> SecretOfAsync(string name)
		=> (await Secrets.GetAsync(WebhookSubscription.SecretName(name))).Expect<string>();

	private async Task<string> HeardAfterAsync(string command)
	{
		var before = Notifications.DeliveryCountFor(WebAppFactoryArg.ExecutorDBRef);
		await Cmd(command);
		return string.Join("\n", Notifications.DeliveriesFor(WebAppFactoryArg.ExecutorDBRef)
			.Skip(before)
			.Select(delivery => delivery.Message));
	}

	/// <summary>A unique webhook name: letters and digits only.</summary>
	private static string UniqueName(string prefix)
		=> new(TestIsolationHelpers.GenerateUniqueName(prefix).Where(char.IsAsciiLetterOrDigit).ToArray());

	[Test]
	public async Task AMortalCannotRunIt()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services,
			WebAppFactoryArg.Services.GetRequiredService<IMediator>(),
			ConnectionService,
			"WebhookMortal");

		var answer = await CmdAs(mortal.DbRef, mortal.Handle, "@webhook");

		await Assert.That(answer).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	[Test]
	public async Task CreateShowsTheSecretOnceAndTheSubscriptionCanBeChangedAndDeleted()
	{
		var name = UniqueName("hook");

		var created = await HeardAfterAsync($"@webhook/create {name}=https://hooks.example.com/{name}");
		var subscription = (await Webhooks.GetAsync(name)).Expect<WebhookSubscription>();
		var secret = await SecretOfAsync(name);
		await Assert.That(created).Contains(secret).And.Contains("not shown again");
		await Assert.That(subscription.Events).IsEmpty();

		var events = await HeardAfterAsync($"@webhook/events {name}=player`* object`create");
		await Assert.That(events).Contains("now hears: PLAYER`* OBJECT`CREATE");

		var listed = await HeardAfterAsync("@webhook");
		await Assert.That(listed).Contains(name);

		var info = await HeardAfterAsync($"@webhook {name}");
		await Assert.That(info).Contains($"https://hooks.example.com/{name}").And.Contains(WebhookSubscription.SecretName(name))
			.And.DoesNotContain(secret);

		var reveal = await HeardAfterAsync($"@webhook/secret {name}");
		await Assert.That(reveal).DoesNotContain(secret);

		await HeardAfterAsync($"@webhook/disable {name}");
		await Assert.That((await Webhooks.GetAsync(name)).Expect<WebhookSubscription>().Enabled).IsFalse();

		var rotated = await HeardAfterAsync($"@webhook/rotate {name}");
		var after = await SecretOfAsync(name);
		await Assert.That(after).IsNotEqualTo(secret);
		await Assert.That(rotated).Contains(after);

		await HeardAfterAsync($"@webhook/delete {name}");
		await Assert.That(await Webhooks.GetAsync(name) is NotFound).IsTrue();
		(await Secrets.GetAsync(WebhookSubscription.SecretName(name))).Expect<NotFound>();
	}

	[Test]
	public async Task AnAddressOnThePrivateNetworkIsRefused()
	{
		var name = UniqueName("inward");

		var heard = await HeardAfterAsync($"@webhook/create {name}=https://169.254.169.254/latest");

		await Assert.That(heard).Contains("private network");
		await Assert.That(await Webhooks.GetAsync(name) is NotFound).IsTrue();
	}

	[Test]
	public async Task AnEventTheGameRaisesReachesASubscriptionThatHearsIt()
	{
		var name = UniqueName("stream");
		await Cmd($"@webhook/create {name}");
		await Cmd($"@webhook/events {name}=OBJECT`CREATE");
		var stream = Channel.CreateUnbounded<WebhookMessage>();

		string created;
		using (Webhooks.OpenStream(name, stream.Writer))
		{
			created = await Cmd($"@create {UniqueName("Gadget")}");
		}

		await Cmd($"@webhook/delete {name}");

		// Other tests create objects in the same world: find this one's event by the objid @create returned.
		var bodies = new List<JsonElement>();
		while (stream.Reader.TryRead(out var message))
		{
			bodies.Add(JsonDocument.Parse(message.Body).RootElement);
		}

		var mine = bodies.Single(body =>
			body.GetProperty("data").GetProperty("arguments").GetProperty("objid").GetString() == created);
		await Assert.That(mine.GetProperty("type").GetString()).IsEqualTo("OBJECT`CREATE");
		await Assert.That(mine.GetProperty("data").GetProperty("enactor").GetProperty("dbref").GetString())
			.IsEqualTo($"#{WebAppFactoryArg.ExecutorDBRef.Number}");
	}

	[Test]
	public async Task TheEventStreamWritesEachEventToAToolThatPresentsTheSecret()
	{
		var name = UniqueName("socket");
		await Cmd($"@webhook/create {name}");
		await Cmd($"@webhook/events {name}=OBJECT`CREATE");
		var secret = await SecretOfAsync(name);

		var client = WebAppFactoryArg.CreateWebSocketClient();
		client.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {secret}";
		using var socket = await client.ConnectAsync(new Uri($"wss://localhost/api/webhooks/{name}/stream"), CancellationToken.None);

		var created = await Cmd($"@create {UniqueName("Widget")}");
		JsonElement mine;
		var buffer = new byte[64 * 1024];
		do
		{
			var frame = await socket.ReceiveAsync(buffer, CancellationToken.None);
			mine = JsonDocument.Parse(buffer.AsMemory(0, frame.Count)).RootElement;
		}
		while (mine.GetProperty("data").GetProperty("arguments").GetProperty("objid").GetString() != created);

		await Assert.That(mine.GetProperty("id").GetString()).StartsWith("msg_");
		await Assert.That(mine.GetProperty("type").GetString()).IsEqualTo("OBJECT`CREATE");

		await Cmd($"@webhook/delete {name}");
		var closing = await socket.ReceiveAsync(buffer, CancellationToken.None);
		await Assert.That(closing.MessageType).IsEqualTo(System.Net.WebSockets.WebSocketMessageType.Close);
	}

	[Test]
	public async Task TheEventStreamRefusesAWrongSecret()
	{
		var name = UniqueName("refused");
		await Cmd($"@webhook/create {name}");

		var client = WebAppFactoryArg.CreateWebSocketClient();
		client.ConfigureRequest = request => request.Headers.Authorization = "Bearer whsec_wrong";

		await Assert.That(async () => await client.ConnectAsync(new Uri($"wss://localhost/api/webhooks/{name}/stream"), CancellationToken.None))
			.Throws<InvalidOperationException>().WithMessageContaining("401");
		await Cmd($"@webhook/delete {name}");
	}
}
