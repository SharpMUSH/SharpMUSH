using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The engine's side of MUD-client support: <c>gmcpsupports()</c> reads the client's GMCP Core.Supports
/// list, <c>oob(..., supported)</c> sends only what the client listed, and <c>exitmap()</c> gives a mapper
/// the exits a viewer can see.
/// </summary>
public class MudClientFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private static readonly DBRef God = new(1);

	private async Task<string> EvalAs(DBRef executor, string expr)
		=> (await WebAppFactoryArg.FunctionParserFor(executor).FunctionParse(MarkupText.Plain(expr)))
			?.Message.ToPlainText() ?? "<null>";

	private async Task<string> AsGod(string command)
		=> (await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)))?.Message.ToPlainText() ?? "";

	private async Task<DBRef> Dig(string label)
		=> DBRef.Parse((await AsGod($"@dig {TestIsolationHelpers.GenerateUniqueName(label)}")).Trim().Split(' ')[^1].Trim());

	/// <summary>A telnet connection that agreed to GMCP and listed <paramref name="supports"/>.</summary>
	private async Task<long> GmcpClientAsync(string supports, string type = "telnet")
		=> await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService, type, new ConcurrentDictionary<string, string>
		{
			["ConnectionType"] = type,
			["GMCP"] = "1",
			[GmcpSupports.MetadataKey] = supports
		});

	#region gmcpsupports()

	[Test]
	public async Task GmcpSupports_ReturnsTheListTheClientSent()
	{
		var handle = await GmcpClientAsync("Room 1 Char.Vitals 1");

		await Assert.That(await EvalAs(God, $"gmcpsupports({handle})")).IsEqualTo("Room 1 Char.Vitals 1");
	}

	[Test]
	[Arguments("Room.Info", "1")]
	[Arguments("ROOM", "1")]
	[Arguments("char.vitals", "1")]
	[Arguments("Char", "0")]
	[Arguments("Comm.Channel.Text", "0")]
	public async Task GmcpSupports_WithAPackage_AnswersWhetherTheClientListedIt(string package, string expected)
	{
		var handle = await GmcpClientAsync("Room 1 Char.Vitals 1");

		await Assert.That(await EvalAs(God, $"gmcpsupports({handle},{package})")).IsEqualTo(expected);
	}

	[Test]
	public async Task GmcpSupports_ForAClientThatNeverSaid_IsEmpty()
	{
		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService, "telnet");

		await Assert.That(await EvalAs(God, $"gmcpsupports({handle})")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(God, $"gmcpsupports({handle},Room)")).IsEqualTo("0");
	}

	[Test]
	public async Task GmcpSupports_ForAPlayer_ReadsTheirLeastIdleConnection()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "GmcpLeastIdle");
		var idle = await GmcpClientAsync("Char 1");
		var active = await GmcpClientAsync("Room 1");
		await ConnectionService.Bind(idle, player.DbRef);
		await ConnectionService.Bind(active, player.DbRef);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		ConnectionService.Update(player.Handle, "LastConnectionSignal", (now - 600_000).ToString(CultureInfo.InvariantCulture));
		ConnectionService.Update(idle, "LastConnectionSignal", (now - 300_000).ToString(CultureInfo.InvariantCulture));
		ConnectionService.Update(active, "LastConnectionSignal", now.ToString(CultureInfo.InvariantCulture));

		await Assert.That(await EvalAs(God, $"gmcpsupports({player.DbRef})")).IsEqualTo("Room 1");
		await Assert.That(await EvalAs(player.DbRef, "gmcpsupports(me)")).IsEqualTo("Room 1")
			.Because("a player may read their own connection");
	}

	[Test]
	public async Task GmcpSupports_OfAnotherPlayer_IsDeniedToAMortal()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "GmcpMortal");
		var other = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "GmcpOther");
		ConnectionService.Update(other.Handle, GmcpSupports.MetadataKey, "Room 1");

		await Assert.That(await EvalAs(mortal.DbRef, $"gmcpsupports({other.DbRef})")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await EvalAs(mortal.DbRef, $"gmcpsupports({other.Handle})"))
			.IsEqualTo(ErrorMessages.Returns.NoSuchDescriptorOrPermissionDenied);

		ConnectionService.Update(mortal.Handle, GmcpSupports.MetadataKey, "Char 1");
		await Assert.That(await EvalAs(mortal.DbRef, $"gmcpsupports({mortal.Handle})")).IsEqualTo("Char 1")
			.Because("a mortal may read their own descriptor");

		await AsGod($"@power {mortal.DbRef}=Send_OOB");

		await Assert.That(await EvalAs(mortal.DbRef, $"gmcpsupports({other.Handle})")).IsEqualTo("Room 1")
			.Because("Send_OOB opens other connections to gmcpsupports() as it does to oob()");
	}

	#endregion

	#region oob(..., supported)

	[Test]
	[Arguments("Room 1", "Room.Info", "1")]
	[Arguments("Room 1", "Comm.Channel.Text", "0")]
	[Arguments("", "Comm.Channel.Text", "1")]
	public async Task Oob_Supported_SendsOnlyWhatTheClientListed(string supports, string package, string expected)
	{
		var handle = await GmcpClientAsync(supports);

		await Assert.That(await EvalAs(God, $"oob({handle},{package},,supported)")).IsEqualTo(expected);
	}

	[Test]
	public async Task Oob_WithoutSupported_SendsAnyGmcpClient()
	{
		var handle = await GmcpClientAsync("Room 1");

		await Assert.That(await EvalAs(God, $"oob({handle},Comm.Channel.Text)")).IsEqualTo("1");
	}

	[Test]
	public async Task Oob_Supported_AlwaysSendsAWebSocket()
	{
		var handle = await GmcpClientAsync("Room 1", "websocket");

		await Assert.That(await EvalAs(God, $"oob({handle},Comm.Channel.Text,,supported)")).IsEqualTo("1");
	}

	[Test]
	public async Task Oob_WithAnyOtherFourthArgument_IsAnError()
	{
		var handle = await GmcpClientAsync("Room 1");

		await Assert.That(await EvalAs(God, $"oob({handle},Room.Info,,always)")).IsEqualTo(ErrorMessages.Returns.InvalidArgument);
	}

	#endregion

	#region exitmap()

	/// <summary>
	/// A room with a plain exit (aliased), a DARK exit, an unlinked exit and one linked to HOME, and a
	/// mortal standing in it whose home is another room.
	/// </summary>
	private async Task<(TestIsolationHelpers.TestPlayer Mortal, DBRef Room, DBRef Beyond, DBRef Home)> MappedRoomAsync(string label)
	{
		var room = await Dig($"{label}Room");
		var beyond = await Dig($"{label}Beyond");
		var home = await Dig($"{label}Home");
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, label, home);
		await AsGod($"@tel {mortal.DbRef}=#{room.Number}");

		await AsGod($"@open Up;u=#{beyond.Number},,#{room.Number}");
		var hidden = DBRef.Parse((await AsGod($"@open Hidden=#{beyond.Number},,#{room.Number}")).Trim());
		await AsGod($"@open Nowhere=,,#{room.Number}");
		await AsGod($"@open Homeward=home,,#{room.Number}");
		await AsGod($"@set #{hidden.Number}=DARK");

		return (mortal, room, beyond, home);
	}

	private static Dictionary<string, int> Map(string json)
		=> JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? [];

	[Test]
	public async Task ExitMap_ListsWhatTheViewerSeesWithDestinations()
	{
		var (mortal, room, beyond, home) = await MappedRoomAsync("ExitMapSees");

		var map = Map(await EvalAs(mortal.DbRef, $"exitmap(#{room.Number})"));

		await Assert.That(map).IsEquivalentTo(new Dictionary<string, int>
		{
			["Up"] = beyond.Number,
			["Nowhere"] = -1,
			["Homeward"] = home.Number
		}).Because("a DARK exit is not listed to a mortal, as look does not list it");
	}

	[Test]
	public async Task ExitMap_ForGod_IncludesTheDarkExit()
	{
		var (_, room, beyond, _) = await MappedRoomAsync("ExitMapGod");

		var map = Map(await EvalAs(God, $"exitmap(#{room.Number})"));

		await Assert.That(map["Hidden"]).IsEqualTo(beyond.Number);
	}

	[Test]
	public async Task ExitMap_AsAnotherViewer_UsesTheirSight()
	{
		var (mortal, room, beyond, home) = await MappedRoomAsync("ExitMapViewer");

		var map = Map(await EvalAs(God, $"exitmap(#{room.Number},{mortal.DbRef})"));

		await Assert.That(map.ContainsKey("Hidden")).IsFalse();
		await Assert.That(map["Up"]).IsEqualTo(beyond.Number);
		await Assert.That(map["Homeward"]).IsEqualTo(home.Number).Because("HOME leads to the viewer's own home");
	}

	[Test]
	public async Task ExitMap_OfARoomTheMortalIsNotIn_IsDenied()
	{
		var (_, room, _, _) = await MappedRoomAsync("ExitMapAway");
		var outsider = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExitMapOutsider", await Dig("ExitMapElsewhere"));
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExitMapNotMine");

		await Assert.That(await EvalAs(outsider.DbRef, $"exitmap(#{room.Number})")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await EvalAs(outsider.DbRef, $"exitmap(#{room.Number},{viewer.DbRef})"))
			.IsEqualTo(ErrorMessages.Returns.PermissionDenied)
			.Because("seeing as someone else needs control of them or the room's own gate");
	}

	[Test]
	public async Task ExitMap_OfSomethingNotARoom_IsAnError()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExitMapNotRoom");

		await Assert.That(await EvalAs(God, $"exitmap({mortal.DbRef})")).IsEqualTo(ErrorMessages.Returns.NotARoom);
	}

	#endregion
}
