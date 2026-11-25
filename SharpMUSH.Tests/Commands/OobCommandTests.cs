using System.Collections.Concurrent;
using System.Text;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@oob</c> is <c>oob()</c> as a command: the same targets, permission and delivery, with the package as the
/// right side's first word and the JSON message the rest.
/// </summary>
public class OobCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();

	private async Task<string> Run(long handle, string command)
		=> (await Factory.CommandParser.CommandParse(handle, ConnectionService, MarkupText.Plain(command)))?.Message.ToPlainText() ?? "";

	private async Task<string> AsGod(string command) => await Run(1, command);

	/// <summary>A telnet connection that agreed to GMCP and listed <paramref name="supports"/>.</summary>
	private async Task<long> GmcpClientAsync(string supports)
		=> await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService, "telnet", new ConcurrentDictionary<string, string>
		{
			["ConnectionType"] = "telnet",
			["GMCP"] = "1",
			[GmcpSupports.MetadataKey] = supports
		});

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string label)
		=> await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, label);

	[Test]
	public async Task Oob_SendsThePackageAndMessageToAPortalConnection()
	{
		var package = $"Test.{TestIsolationHelpers.GenerateUniqueName("OobCmd")}";
		var socket = TestIsolationHelpers.GenerateUniqueHandle();
		await ConnectionService.Register(socket, "localhost", "localhost", "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);

		await using var nats = new NatsConnection(new NatsOpts
		{
			Url = $"nats://localhost:{Factory.NatsTestServer.Instance.GetMappedPublicPort(4222)}"
		});
		var subject = NatsSubjects.For(typeof(WebSocketOutputMessage), Factory.Services.GetRequiredService<NatsOptions>().SubjectPrefix);
		await using var published = await nats.SubscribeCoreAsync(subject,
			serializer: CompressingNatsSerializer<WebSocketOutputMessage>.Default);
		await nats.PingAsync();

		var said = await AsGod($"@oob/noeval {socket}={package} {{\"hp\": 10}}");

		string? envelope = null;
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await foreach (var message in published.Msgs.ReadAllAsync(timeout.Token))
		{
			if (message.Data is not { } output || output.Handle != socket) continue;
			envelope = output.Data;
			break;
		}

		await Assert.That(said).IsEqualTo("1").Because("the output is the number of connections reached, as oob() returns");
		await Assert.That(envelope).IsEqualTo(WebSocketOobEnvelope.Build(package, "{\"hp\": 10}"));
	}

	[Test]
	public async Task Oob_EvaluatesTheMessage()
	{
		var handle = await GmcpClientAsync("");

		await Assert.That(await AsGod($"@oob {handle}=Char.Vitals [json(object,hp,json(number,10))]")).IsEqualTo("1");
	}

	[Test]
	public async Task Oob_WithoutAMessage_SendsThePackageAlone()
	{
		var handle = await GmcpClientAsync("");

		await Assert.That(await AsGod($"@oob {handle}=Core.Ping")).IsEqualTo("1");
	}

	[Test]
	[Arguments("Room 1", "Room.Info", "1")]
	[Arguments("Room 1", "Comm.Channel.Text", "0")]
	[Arguments("", "Comm.Channel.Text", "1")]
	public async Task Oob_Supported_SendsOnlyWhatTheClientListed(string supports, string package, string expected)
	{
		var handle = await GmcpClientAsync(supports);

		await Assert.That(await AsGod($"@oob/supported {handle}={package}")).IsEqualTo(expected);
		await Assert.That(await AsGod($"@oob {handle}={package}")).IsEqualTo("1")
			.Because("without /supported every GMCP client gets the package");
	}

	[Test]
	public async Task Oob_ToThemselves_IsAllowedToAMortal()
	{
		var mortal = await PlayerAsync("OobCmdSelf");
		ConnectionService.Update(mortal.Handle, "GMCP", "1");

		await Assert.That(await Run(mortal.Handle, "@oob/noeval me=Char.Vitals {\"hp\":1}")).IsEqualTo("1");
	}

	[Test]
	public async Task Oob_ToAnotherPlayer_IsDeniedToAMortal()
	{
		var mortal = await PlayerAsync("OobCmdMortal");
		var other = await PlayerAsync("OobCmdOther");

		var said = await Run(mortal.Handle, $"@oob {other.DbRef}=Char.Vitals");

		await Assert.That(said).IsEqualTo("#-1 PERMISSION DENIED: NEEDS GAME.WIZARD OR GAME.SEND_OOB");
		await Assert.That(Factory.Notifications.For(mortal.DbRef))
			.Contains("Permission denied. You need one of these permissions: game.wizard, game.send_oob.");
	}

	[Test]
	public async Task Oob_WithTheSendOobPower_MayReachAnotherPlayer()
	{
		var mortal = await PlayerAsync("OobCmdPowered");
		var other = await PlayerAsync("OobCmdPoweredOther");
		ConnectionService.Update(other.Handle, "GMCP", "1");
		await AsGod($"@power {mortal.DbRef}=Send_OOB");

		await Assert.That(await Run(mortal.Handle, $"@oob {other.DbRef}=Char.Vitals")).IsEqualTo("1");
	}

	[Test]
	public async Task Oob_WithAMessageThatIsNotJson_IsRefused()
	{
		var mortal = await PlayerAsync("OobCmdBadJson");

		var said = await Run(mortal.Handle, "@oob me=Char.Vitals hp=10");

		await Assert.That(said).IsEqualTo(ErrorMessages.Returns.InvalidJsonMessage);
		await Assert.That(Factory.Notifications.For(mortal.DbRef)).Contains(ErrorMessages.Notifications.OobInvalidJson);
	}

	[Test]
	[Arguments("@oob me")]
	[Arguments("@oob me=")]
	[Arguments("@oob =Char.Vitals")]
	[Arguments("@oob/noeval me={\"hp\":1}")]
	public async Task Oob_WithoutPlayersOrPackage_ShowsUsage(string command)
	{
		var mortal = await PlayerAsync("OobCmdUsage");

		await Assert.That(await Run(mortal.Handle, command)).IsEqualTo(ErrorMessages.Returns.InvalidArgument);
		await Assert.That(Factory.Notifications.For(mortal.DbRef)).Contains(ErrorMessages.Notifications.OobUsage);
	}
}
