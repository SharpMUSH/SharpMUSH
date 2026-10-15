using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The MUD-kit packages (gmcp-room, gmcp-comm, gmcp-items, gmcp-char, mudlet-client and the mud-kit meta
/// package), installed from the catalogue and driven the way a game drives them: a player walks through an
/// exit, speaks on a channel, picks something up, has a stat changed, or their client says something over
/// GMCP. What each telnet connection was sent is read off the NATS subject the connection server consumes
/// GMCP from, up to a probe God sends each watched connection afterwards, so "sent nothing" is an
/// observation, not a timeout.
///
/// <para>Each test installs what it needs and removes it again. Not in parallel: the packages attach to the
/// one event handler every test shares.</para>
/// </summary>
[NotInParallel]
public class MudKitIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IEventService Events => WebAppFactoryArg.Services.GetRequiredService<IEventService>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	/// <summary>A player on a telnet connection that agreed to GMCP.</summary>
	private sealed record Client(DBRef DbRef, long Handle, string Name)
	{
		public string Number => $"#{DbRef.Number}";
	}

	private async Task<string> God(string command) =>
		(await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message?.ToPlainText()?.Trim() ?? string.Empty;

	private Task<string> Think(string expression) => God($"think {expression}");

	private Task Run(Client who, string command) =>
		Parser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command)).AsTask();

	/// <summary>What <paramref name="who"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(Client who, string command)
	{
		var before = Notifications.CountFor(who.DbRef);
		await Run(who, command);
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(who.DbRef).Skip(before));
	}

	private async Task<DBRef> Dig(string prefix) =>
		DBRef.Parse(await God($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));

	/// <summary>
	/// A player made in <paramref name="room"/> and connected over telnet with GMCP agreed.
	/// <paramref name="supports"/> is the Core.Supports list its client sent, or null for one that sent none.
	/// </summary>
	private async Task<Client> ClientAsync(string prefix, DBRef room, string? supports = null, bool screenReader = false)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		var options = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var player = await Mediator.Send(new CreatePlayerCommand(
			name, "TestPassword123", room, room, (int)options.CurrentValue.Limit.StartingQuota));
		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, player, "telnet",
			Metadata(supports, screenReader));
		return new Client(player, handle, name);
	}

	/// <summary>Another telnet connection, with GMCP agreed, for a player who already has one.</summary>
	private async Task<long> ConnectAgainAsync(Client who, string? supports = null) =>
		await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, who.DbRef, "telnet", Metadata(supports, false));

	private static ConcurrentDictionary<string, string> Metadata(string? supports, bool screenReader)
	{
		// The times a real connection registers with; without them the player's idle time is unreadable and
		// +who, which every connected test player shows up in, prints an error for it.
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
		var metadata = new ConcurrentDictionary<string, string>
		{
			["ConnectionStartTime"] = now,
			["LastConnectionSignal"] = now,
			["ConnectionType"] = "telnet",
			["GMCP"] = "1"
		};
		if (supports is not null) metadata[GmcpSupports.MetadataKey] = supports;
		if (screenReader) metadata[TerminalCapabilityReader.ScreenReaderKey] = "1";
		return metadata;
	}

	private async Task ApplyAsync(string package)
	{
		var controller = new PackagesController(
			WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		// AINSTALL is queued after the apply.
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private async Task UninstallAsync(params string[] packages)
	{
		foreach (var package in packages) await Installer.UninstallAsync(package, force: true, CancellationToken.None);
	}

	private async Task<string> PackageObject(string package, string reference) =>
		(await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync(package))
		.First(o => o.Ref == reference).Objid;

	/// <summary>
	/// Watches the NATS subject GMCP output is published on. <see cref="SentWhile"/> runs an action, waits
	/// for the events it queued, and then has God send each watched connection a probe; one publisher's
	/// messages arrive in order, so a connection's probe arriving means everything sent to it before has too.
	/// </summary>
	private sealed class GmcpWatch : IAsyncDisposable
	{
		private readonly NatsConnection _nats;
		private readonly INatsSub<GMCPOutputMessage> _sub;
		private readonly MudKitIntegrationTests _test;

		private GmcpWatch(NatsConnection nats, INatsSub<GMCPOutputMessage> sub, MudKitIntegrationTests test)
		{
			_nats = nats;
			_sub = sub;
			_test = test;
		}

		public static async Task<GmcpWatch> OpenAsync(MudKitIntegrationTests test)
		{
			var factory = test.WebAppFactoryArg;
			// What the setup queued is sent before the watch starts.
			await factory.QueueBarrierAsync();
			var nats = new NatsConnection(new NatsOpts
			{
				Url = $"nats://localhost:{factory.NatsTestServer.Instance.GetMappedPublicPort(4222)}"
			});
			var subject = NatsSubjects.For(typeof(GMCPOutputMessage),
				factory.Services.GetRequiredService<NatsOptions>().SubjectPrefix);
			var sub = await nats.SubscribeCoreAsync(subject,
				serializer: CompressingNatsSerializer<GMCPOutputMessage>.Default);
			await nats.PingAsync();
			return new GmcpWatch(nats, sub, test);
		}

		/// <summary>The GMCP frames (module, body) each watched connection was sent while <paramref name="action"/> ran.</summary>
		public async Task<Dictionary<long, List<(string Module, JsonNode? Data)>>> SentWhile(Func<Task> action,
			params long[] handles)
		{
			// Whatever the test did since the last window (setting the attributes it then changes, say) is sent
			// and read off before the action, so it is not counted as the action's.
			await Collect(handles);
			await action();
			return await Collect(handles);
		}

		private async Task<Dictionary<long, List<(string Module, JsonNode? Data)>>> Collect(long[] handles)
		{
			await _test.WebAppFactoryArg.QueueBarrierAsync();

			var probe = $"Probe.P{Guid.NewGuid():N}";
			foreach (var handle in handles)
			{
				await _test.God($"think null(oob({handle},{probe}))");
			}

			var watched = handles.ToHashSet();
			var sent = handles.ToDictionary(h => h, _ => new List<(string, JsonNode?)>());
			var probed = new HashSet<long>();
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			await foreach (var message in _sub.Msgs.ReadAllAsync(timeout.Token))
			{
				if (message.Data is not { } output || !watched.Contains(output.Handle)) continue;
				if (output.Module == probe)
				{
					probed.Add(output.Handle);
					if (probed.Count == watched.Count) break;
					continue;
				}

				sent[output.Handle].Add((output.Module,
					string.IsNullOrEmpty(output.Message) ? null : JsonNode.Parse(output.Message)));
			}

			return sent;
		}

		public async ValueTask DisposeAsync()
		{
			await _sub.DisposeAsync();
			await _nats.DisposeAsync();
		}
	}

	/// <summary>
	/// The MUD-kit's frames among those sent: GMCP modules are capitalised (Room.Info). room-contents and
	/// comm-feed, installed at boot for the portal, send their own lower-case packages (room.contents,
	/// comm.message) to every GMCP connection too.
	/// </summary>
	private static List<(string Module, JsonNode? Data)> Kit(List<(string Module, JsonNode? Data)> sent) =>
		sent.Where(f => f.Module.Length > 0 && char.IsUpper(f.Module[0])).ToList();

	private static List<JsonNode?> Frames(List<(string Module, JsonNode? Data)> sent, string module) =>
		sent.Where(f => f.Module == module).Select(f => f.Data).ToList();

	private static JsonObject Single(List<(string Module, JsonNode? Data)> sent, string module) =>
		Frames(sent, module).Single() as JsonObject
		?? throw new InvalidOperationException($"{module} was not an object");

	private static string[] Names(JsonNode? players) =>
		players is JsonArray array ? array.Select(p => p!["name"]!.GetValue<string>()).ToArray() : [];

	#region gmcp-room

	[Test]
	public async Task GmcpRoom_AMoveSendsTheRoomAndTellsBothRooms()
	{
		try
		{
			await ApplyAsync("gmcp-room");
			var from = await Dig("GrFrom");
			var to = await Dig("GrTo");
			await God($"@open North;n={to},South;s,{from}");
			await God($"&AREA {to}=Harbour");
			await God($"&ENVIRONMENT {to}=docks");
			await God($"&X {to}=3");
			await God($"&Y {to}=-4");

			var mover = await ClientAsync("GrMover", from, "Room 1 Char 1");
			var left = await ClientAsync("GrLeft", from);
			var waiting = await ClientAsync("GrWaiting", to, "Room 1");
			var unlisted = await ClientAsync("GrUnlisted", to, "Char 1");
			await God($"&FULLNAME {waiting.Number}=Waiting In The Rain");

			await using var watch = await GmcpWatch.OpenAsync(this);
			var sent = await watch.SentWhile(() => Run(mover, "north"),
				mover.Handle, left.Handle, waiting.Handle, unlisted.Handle);

			var info = Single(sent[mover.Handle], "Room.Info");
			await Assert.That(info["num"]!.GetValue<int>()).IsEqualTo(to.Number);
			await Assert.That(info["name"]!.GetValue<string>()).StartsWith("GrTo");
			await Assert.That(info["area"]!.GetValue<string>()).IsEqualTo("Harbour");
			await Assert.That(info["environment"]!.GetValue<string>()).IsEqualTo("docks");
			await Assert.That(info["coords"]!.GetValue<string>()).IsEqualTo("3,-4,0")
				.Because("a missing coordinate is 0, and a room with any is sent all three");
			await Assert.That(info["exits"]!["South"]!.GetValue<int>()).IsEqualTo(from.Number)
				.Because("exits is exitmap(): each exit's name up to its first ; to the room it leads to");

			var players = Frames(sent[mover.Handle], "Room.Players").Single() as JsonArray;
			await Assert.That(Names(players)).Contains(mover.Name).And.Contains(waiting.Name).And.Contains(unlisted.Name)
				.And.DoesNotContain(left.Name);
			var waitingRow = players!.First(p => p!["name"]!.GetValue<string>() == waiting.Name)!;
			await Assert.That(waitingRow["fullname"]!.GetValue<string>()).IsEqualTo("Waiting In The Rain");

			var added = Single(sent[waiting.Handle], "Room.AddPlayer");
			await Assert.That(added["name"]!.GetValue<string>()).IsEqualTo(mover.Name);
			await Assert.That(added["fullname"]!.GetValue<string>()).IsEqualTo(mover.Name)
				.Because("a player without FULLNAME is shown in full by name");
			await Assert.That(Frames(sent[left.Handle], "Room.RemovePlayer").Single()!.GetValue<string>()).IsEqualTo(mover.Name)
				.Because("a client that listed nothing in Core.Supports is sent everything");

			await Assert.That(Kit(sent[unlisted.Handle])).IsEmpty()
				.Because("a client that listed modules without Room is sent no Room package");
			await Assert.That(Frames(sent[mover.Handle], "Room.AddPlayer")).IsEmpty()
				.Because("the mover is not told they arrived");
		}
		finally
		{
			await UninstallAsync("gmcp-room");
		}
	}

	[Test]
	public async Task GmcpRoom_ADarkPlayerIsNotAnnounced_AndExitsFollowExitmap()
	{
		try
		{
			await ApplyAsync("gmcp-room");
			var from = await Dig("GdFrom");
			var to = await Dig("GdTo");
			await God($"@open East;e={to},West;w,{from}");
			var sneak = await ClientAsync("GdSneak", from);
			var mortal = await ClientAsync("GdMortal", to);
			await God($"@set {sneak.Number}=DARK");

			await using var watch = await GmcpWatch.OpenAsync(this);
			var sent = await watch.SentWhile(() => God($"@tel {sneak.Number}={to}"), sneak.Handle, mortal.Handle);

			await Assert.That(Frames(sent[mortal.Handle], "Room.AddPlayer")).IsEmpty()
				.Because("a DARK player arriving is not shown to a mortal");
			var info = Single(sent[sneak.Handle], "Room.Info");
			await Assert.That(info["exits"]!.AsObject().Select(e => e.Key)).Contains("West");
			await Assert.That(Names(Frames(sent[sneak.Handle], "Room.Players").Single())).Contains(sneak.Name)
				.Because("a player always sees themself");
		}
		finally
		{
			await UninstallAsync("gmcp-room");
		}
	}

	[Test]
	public async Task GmcpRoom_ConnectingSendsTheRoomOnTheNewConnectionOnly()
	{
		try
		{
			await ApplyAsync("gmcp-room");
			var room = await Dig("GcRoom");
			var player = await ClientAsync("GcPlayer", room);
			var other = await ClientAsync("GcOther", room);
			var second = await ConnectAgainAsync(player);

			await using var watch = await GmcpWatch.OpenAsync(this);
			var sent = await watch.SentWhile(
				() => Events.TriggerEventAsync(SharpEvents.PlayerConnect, player.DbRef, player.Number, "2", second.ToString()).AsTask(),
				player.Handle, second, other.Handle);

			await Assert.That(Single(sent[second], "Room.Info")["num"]!.GetValue<int>()).IsEqualTo(room.Number);
			await Assert.That(Frames(sent[second], "Room.Players")).Count().IsEqualTo(1);
			await Assert.That(Kit(sent[player.Handle])).IsEmpty().Because("the connection already open has the room");
			await Assert.That(Kit(sent[other.Handle])).IsEmpty().Because("a second connection is not an arrival");

			var disconnect = await watch.SentWhile(
				() => Events.TriggerEventAsync(SharpEvents.PlayerDisconnect, player.DbRef, player.Number, "0", "0", "quit",
					"127.0.0.1", second.ToString(), "1", "0", "0/0/0").AsTask(),
				other.Handle);
			await Assert.That(Frames(disconnect[other.Handle], "Room.RemovePlayer").Single()!.GetValue<string>())
				.IsEqualTo(player.Name);
		}
		finally
		{
			await UninstallAsync("gmcp-room");
		}
	}

	#endregion

	#region gmcp-comm

	private async Task<string> ChannelAsync(string prefix, params Client[] members)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix).Replace("_", string.Empty);
		await God($"@channel/add {name}=player open");
		foreach (var member in members) await God($"@channel/on {name}={member.Number}");
		return name;
	}

	[Test]
	public async Task GmcpComm_AChannelLineReachesItsMembersAsTheyReadIt()
	{
		try
		{
			await ApplyAsync("gmcp-comm");
			var room = await Dig("GmRoom");
			var speaker = await ClientAsync("GmSpeaker", room, "Comm.Channel 1");
			var reader = await ClientAsync("GmReader", room, screenReader: true);
			var outsider = await ClientAsync("GmOutsider", room);
			var mudlet = await ClientAsync("GmMudlet", room, "Char 1 Room 1");
			var channel = await ChannelAsync("GmChan", speaker, reader, mudlet);
			var marker = TestIsolationHelpers.GenerateUniqueName("line");

			await using var watch = await GmcpWatch.OpenAsync(this);
			var sent = await watch.SentWhile(() => Run(speaker, $"@chat {channel}=[ansi(r,{marker})], friends"),
				speaker.Handle, reader.Handle, outsider.Handle, mudlet.Handle);

			var heard = Single(sent[speaker.Handle], "Comm.Channel.Text");
			await Assert.That(heard["channel"]!.GetValue<string>()).IsEqualTo(channel);
			await Assert.That(heard["talker"]!.GetValue<string>()).IsEqualTo(speaker.Name);
			var text = heard["text"]!.GetValue<string>();
			await Assert.That(text).Contains("\u001b[").Because("Mudlet's chat window draws the line's ANSI colour");
			await Assert.That(text).Contains($"<{channel}> {speaker.Name} says, \"").And.Contains($"{marker}");

			var plain = Single(sent[reader.Handle], "Comm.Channel.Text")["text"]!.GetValue<string>();
			await Assert.That(plain).IsEqualTo($"<{channel}> {speaker.Name} says, \"{marker}, friends\"")
				.Because("a screen reader's copy is the line without colour");

			await Assert.That(Kit(sent[outsider.Handle])).IsEmpty().Because("someone not on the channel did not see the line");
			await Assert.That(Kit(sent[mudlet.Handle])).IsEmpty().Because("a client that listed modules without Comm is sent none");
		}
		finally
		{
			await UninstallAsync("gmcp-comm");
		}
	}

	[Test]
	public async Task GmcpComm_APageReachesBothEnds_AndJoiningSendsTheList()
	{
		try
		{
			await ApplyAsync("gmcp-comm");
			var room = await Dig("GpRoom");
			var pager = await ClientAsync("GpPager", room);
			var paged = await ClientAsync("GpPaged", room);
			var channel = await ChannelAsync("GpChan");

			await using var watch = await GmcpWatch.OpenAsync(this);
			var sent = await watch.SentWhile(() => Run(pager, $"page {paged.Name}=Hello there, you."), pager.Handle, paged.Handle);

			var received = Single(sent[paged.Handle], "Comm.Channel.Text");
			await Assert.That(received["channel"]!.GetValue<string>()).IsEqualTo("page");
			await Assert.That(received["talker"]!.GetValue<string>()).IsEqualTo(pager.Name);
			await Assert.That(received["text"]!.GetValue<string>()).IsEqualTo($"{pager.Name} pages: Hello there, you.");
			await Assert.That(Single(sent[pager.Handle], "Comm.Channel.Text")["text"]!.GetValue<string>())
				.IsEqualTo($"You paged {paged.Name} with 'Hello there, you.'");

			var joined = await watch.SentWhile(() => Run(pager, $"@channel/on {channel}"), pager.Handle);
			var list = Frames(joined[pager.Handle], "Comm.Channel.List").Last() as JsonArray;
			var row = list!.Single(c => c!["name"]!.GetValue<string>() == channel)!;
			await Assert.That(row["command"]!.GetValue<string>()).IsEqualTo($"+{channel.ToLowerInvariant()}");

			await God("+config gmcp-comm/pages=off");
			var quiet = await watch.SentWhile(() => Run(pager, $"page {paged.Name}=Again."), paged.Handle);
			await Assert.That(Kit(quiet[paged.Handle])).IsEmpty().Because("the pages setting is off");
		}
		finally
		{
			await UninstallAsync("gmcp-comm");
		}
	}

	#endregion

	#region gmcp-items

	[Test]
	public async Task GmcpItems_ThingsComingAndGoingAreAddedAndRemoved()
	{
		try
		{
			await ApplyAsync("gmcp-items");
			var room = await Dig("GiRoom");
			var holder = await ClientAsync("GiHolder", room, "Char.Items 1");
			var looker = await ClientAsync("GiLooker", room);
			var lantern = DBRef.Parse(await Think($"create({TestIsolationHelpers.GenerateUniqueName("GiLantern")})"));
			var id = lantern.Number.ToString();

			await using var watch = await GmcpWatch.OpenAsync(this);
			var dropped = await watch.SentWhile(() => God($"@tel #{lantern.Number}={room}"), holder.Handle, looker.Handle);
			foreach (var handle in new[] { holder.Handle, looker.Handle })
			{
				var add = Single(dropped[handle], "Char.Items.Add");
				await Assert.That(add["location"]!.GetValue<string>()).IsEqualTo("room");
				await Assert.That(add["item"]!["id"]!.GetValue<string>()).IsEqualTo(id);
			}

			var taken = await watch.SentWhile(() => God($"@tel #{lantern.Number}={holder.Number}"), holder.Handle, looker.Handle);
			await Assert.That(Single(taken[looker.Handle], "Char.Items.Remove")["location"]!.GetValue<string>()).IsEqualTo("room");
			await Assert.That(Frames(taken[holder.Handle], "Char.Items.Add").Cast<JsonObject>()
				.Single(f => f["location"]!.GetValue<string>() == "inv")["item"]!["id"]!.GetValue<string>()).IsEqualTo(id);

			// The client asks for its inventory; the answer goes to the connection that asked.
			var asked = await watch.SentWhile(
				() => Events.TriggerEventAsync(SharpEvents.SocketGmcp, holder.DbRef, holder.Handle.ToString(), "Char.Items.Inv",
					string.Empty, holder.Number).AsTask(),
				holder.Handle);
			var inventory = Single(asked[holder.Handle], "Char.Items.List");
			await Assert.That(inventory["location"]!.GetValue<string>()).IsEqualTo("inv");
			await Assert.That(inventory["items"]!.AsArray().Select(i => i!["id"]!.GetValue<string>())).Contains(id);
		}
		finally
		{
			await UninstallAsync("gmcp-items");
		}
	}

	#endregion

	#region gmcp-char

	[Test]
	public async Task GmcpChar_AWatchedStatSendsVitals_AndStatusFollowsItsSetting()
	{
		// MP is already in the table with a flag of its own, and MAXMP is watched by someone else.
		await God("@attribute/access MP=no_command");
		await God("@attribute/access MAXMP=none");
		await God("@attribute/watch MAXMP=WATCH`MAXMP`STAFF");
		try
		{
			await ApplyAsync("gmcp-char");
			var gmcpChar = await PackageObject("gmcp-char", "gmcp_char");
			await Assert.That(await Think("attrwatch(HP)")).IsEqualTo("WATCH`HP WATCH`HP`*")
				.Because("AINSTALL adds HP to the table and watches it with the default pattern");
			await Assert.That(await Think("hasattr(#9,WATCH`HP`GMCPCHAR)")).IsEqualTo("1");
			await Assert.That((await Mediator.Send(new SharpMUSH.Library.Queries.Database.GetAttributeEntryQuery("MP")))!.DefaultFlags
				.Select(flag => flag.ToLowerInvariant())).Contains("no_command").Because("an entry the game already has keeps its flags");
			await Assert.That(await Think("attrwatch(MAXMP)")).IsEqualTo("WATCH`MAXMP`STAFF")
				.Because("a watch with another pattern belongs to someone else");
			await Assert.That(await Think("hasattr(#9,WATCH`MAXMP`GMCPCHAR)")).IsEqualTo("0");
			await Assert.That(await Think($"get({gmcpChar}/DATA`FOREIGN)")).IsEqualTo("MAXMP");

			var room = await Dig("GvRoom");
			var player = await ClientAsync("GvPlayer", room, "Char 1");
			await God($"&MAXHP {player.Number}=40");

			await using var watch = await GmcpWatch.OpenAsync(this);
			var sent = await watch.SentWhile(() => God($"&HP {player.Number}=25"), player.Handle);
			var vitals = Single(sent[player.Handle], "Char.Vitals");
			await Assert.That(vitals["hp"]!.GetValue<int>()).IsEqualTo(25);
			await Assert.That(vitals["maxhp"]!.GetValue<int>()).IsEqualTo(40);
			await Assert.That(vitals.ContainsKey("mp")).IsFalse().Because("an attribute the player lacks is left out");

			await God("+config gmcp-char/status=LEVEL CLASS");
			await WebAppFactoryArg.QueueBarrierAsync();
			await Assert.That(await Think($"get({gmcpChar}/DATA`CONFIG`STATUS)")).IsEqualTo("LEVEL CLASS");
			await Assert.That(await Think("attrwatch(LEVEL)")).IsEqualTo("WATCH`LEVEL WATCH`LEVEL`*");
			await God($"&CLASS {player.Number}=Wanderer");
			var status = await watch.SentWhile(() => God($"&LEVEL {player.Number}=3"), player.Handle);
			var body = Single(status[player.Handle], "Char.Status");
			await Assert.That(body["name"]!.GetValue<string>()).IsEqualTo(player.Name);
			await Assert.That(body["level"]!.GetValue<int>()).IsEqualTo(3);
			await Assert.That(body["class"]!.GetValue<string>()).IsEqualTo("Wanderer");
			await Assert.That(Frames(status[player.Handle], "Char.Vitals")).IsEmpty();

			var connected = await watch.SentWhile(
				() => Events.TriggerEventAsync(SharpEvents.PlayerConnect, player.DbRef, player.Number, "1", player.Handle.ToString()).AsTask(),
				player.Handle);
			await Assert.That(Kit(connected[player.Handle]).Select(f => f.Module))
				.IsEquivalentTo(new[] { "Char.Name", "Char.Vitals", "Char.Status" });
			await Assert.That(Single(connected[player.Handle], "Char.Name")["fullname"]!.GetValue<string>()).IsEqualTo(player.Name);

			// Dropping LEVEL and CLASS from status: CLASS has a handler of someone else's, so it stays watched.
			await God("&WATCH`CLASS`MINE #9=think");
			await God("+config gmcp-char/status=");
			await WebAppFactoryArg.QueueBarrierAsync();
			await Assert.That(await Think("[hasattr(#9,WATCH`LEVEL`GMCPCHAR)] [hasattr(#9,WATCH`CLASS`GMCPCHAR)]")).IsEqualTo("0 0");
			await Assert.That(await Think("attrwatch(LEVEL)")).IsEqualTo(string.Empty)
				.Because("nothing else answers LEVEL's watch");
			await Assert.That(await Think("attrwatch(CLASS)")).IsEqualTo("WATCH`CLASS WATCH`CLASS`*")
				.Because("WATCH`CLASS`MINE still answers it");
			await Assert.That(await Think($"get({gmcpChar}/DATA`WATCHING)")).IsEqualTo("HP MAXHP MP");

			// Uninstalling runs no softcode; the next change to HP finds the package gone and cleans up.
			await UninstallAsync("gmcp-char");
			await God($"&HP {player.Number}=20");
			await WebAppFactoryArg.QueueBarrierAsync();
			await Assert.That(await Think("hasattr(#9,WATCH`HP`GMCPCHAR)")).IsEqualTo("0");
			await Assert.That(await Think("attrwatch(HP)")).IsEqualTo(string.Empty);
		}
		finally
		{
			await UninstallAsync("gmcp-char");
			await God("&WATCH`CLASS`MINE #9");
			foreach (var attribute in new[] { "HP", "MAXHP", "MP", "MAXMP", "LEVEL", "CLASS" })
			{
				await God($"@wipe #9/WATCH`{attribute}`GMCPCHAR");
				await God($"@attribute/delete {attribute}");
			}
		}
	}

	#endregion

	#region mudlet-client

	[Test]
	public async Task MudletClient_OffersTheMapOnHello_AndTheInterfaceToWhoeverWantsIt()
	{
		try
		{
			await ApplyAsync("mudlet-client");
			await God("+config mudlet-client/gui-url=https://example.org/game.mpackage");
			await God("+config mudlet-client/gui-version=2.1");
			await God("+config mudlet-client/map-url=https://example.org/map.xml");
			await God("+config mudlet-client/discord-invite=https://discord.gg/abcdef");
			var room = await Dig("MuRoom");
			var player = await ClientAsync("MuPlayer", room);
			var lobby = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService, "telnet", Metadata(null, false));

			await using var watch = await GmcpWatch.OpenAsync(this);
			var hello = await watch.SentWhile(
				() => Events.TriggerEventAsync(SharpEvents.SocketGmcp, null, lobby.ToString(), "Core.Hello",
					"""{"client":"Mudlet","version":"4.19"}""", string.Empty).AsTask(),
				lobby);
			await Assert.That(Kit(hello[lobby]).Select(f => f.Module)).IsEquivalentTo(new[] { "Client.Map" })
				.Because("before login nobody's +gui choice is known, so the interface waits for the login");
			await Assert.That(Single(hello[lobby], "Client.Map")["url"]!.GetValue<string>()).IsEqualTo("https://example.org/map.xml");

			var discord = await watch.SentWhile(
				() => Events.TriggerEventAsync(SharpEvents.SocketGmcp, null, lobby.ToString(), "External.Discord.Hello",
					"""{"user":"wren"}""", string.Empty).AsTask(),
				lobby);
			await Assert.That(Single(discord[lobby], "External.Discord.Info")["inviteurl"]!.GetValue<string>())
				.IsEqualTo("https://discord.gg/abcdef");
			await Assert.That(Single(discord[lobby], "External.Discord.Info").ContainsKey("applicationid")).IsFalse();
			await Assert.That(Single(discord[lobby], "External.Discord.Status").ContainsKey("details")).IsFalse()
				.Because("before login the status names only the game");

			var connected = await watch.SentWhile(ConnectEvent, player.Handle);
			var gui = Single(connected[player.Handle], "Client.GUI");
			await Assert.That(gui["version"]!.GetValue<string>()).IsEqualTo("2.1");
			await Assert.That(gui["url"]!.GetValue<string>()).IsEqualTo("https://example.org/game.mpackage");
			await Assert.That(Single(connected[player.Handle], "External.Discord.Status")["details"]!.GetValue<string>())
				.IsEqualTo($"Playing {player.Name}");

			await Assert.That(await As(player, "+gui off")).Contains("will not be offered");
			await Assert.That(await Think($"words(lattr({await PackageObject("mudlet-client", "client")}/DATA`GUI-OFF`*))")).IsEqualTo("1");
			var declined = await watch.SentWhile(ConnectEvent, player.Handle);
			await Assert.That(Frames(declined[player.Handle], "Client.GUI")).IsEmpty().Because("the player turned +gui off");

			var on = await watch.SentWhile(async () =>
				await Assert.That(await As(player, "+gui on")).Contains("It was offered just now."), player.Handle);
			await Assert.That(Frames(on[player.Handle], "Client.GUI")).Count().IsEqualTo(1);

			var stock = await ClientAsync("MuStock", room,
				"Char 1 Char.Skills 1 Char.Items 1 Room 1 IRE.Rift 1 IRE.Composer 1 External.Discord 1 Client.Media 1");
			var stockConnected = await watch.SentWhile(
				() => Events.TriggerEventAsync(SharpEvents.PlayerConnect, stock.DbRef, stock.Number, "1", stock.Handle.ToString()).AsTask(),
				stock.Handle);
			await Assert.That(Frames(stockConnected[stock.Handle], "Client.GUI")).Count().IsEqualTo(1)
				.Because("a stock Mudlet never lists Client, so the interface is offered without the filter");

			Task ConnectEvent() =>
				Events.TriggerEventAsync(SharpEvents.PlayerConnect, player.DbRef, player.Number, "1", player.Handle.ToString()).AsTask();
		}
		finally
		{
			await UninstallAsync("mudlet-client");
		}
	}

	[Test]
	public async Task MudletClient_GuiSpeaksPlainlyToAScreenReader()
	{
		try
		{
			await ApplyAsync("mudlet-client");
			var room = await Dig("MsRoom");
			var reader = await ClientAsync("MsReader", room, screenReader: true);
			var sighted = await ClientAsync("MsSighted", room);

			await Assert.That(await As(reader, "+gui")).Contains("no interface package");
			await God("+config mudlet-client/gui-url=https://example.org/ui.mpackage");

			var spoken = await As(reader, "+gui");
			await Assert.That(spoken).Contains("The game's interface for Mudlet is version 1.0, at https://example.org/ui.mpackage.")
				.And.Contains("It is offered to you when you connect.");
			var drawn = await As(sighted, "+gui");
			await Assert.That(drawn).Contains("Mudlet interface").And.Contains("when you connect")
				.And.DoesNotContain("The game's interface for Mudlet is version");
			await Assert.That(await As(sighted, "+gui maybe")).Contains("+gui on offers you");
		}
		finally
		{
			await UninstallAsync("mudlet-client");
		}
	}

	#endregion

	#region mud-kit

	[Test]
	public async Task MudKit_InstallsOnTheFive_AndCarriesTheOverview()
	{
		string[] five = ["gmcp-room", "gmcp-comm", "gmcp-items", "gmcp-char", "mudlet-client"];
		try
		{
			foreach (var package in five) await ApplyAsync(package);
			await ApplyAsync("mud-kit");

			var room = await Dig("MkRoom");
			var reader = await ClientAsync("MkReader", room);
			var overview = await As(reader, "+help gmcp");
			await Assert.That(overview).Contains("GMCP").And.Contains("gmcp room").And.Contains("gmcpsupports(me)");
			await Assert.That(await As(reader, "+help gmcp room")).Contains("Room.Info");
			await Assert.That(await As(reader, "+help gmcp char")).Contains("Char.Vitals");
			await Assert.That(await As(reader, "+help gui")).Contains("+gui off");
			await Assert.That(await As(reader, "+config")).Contains("GMCP Room").And.Contains("Mudlet client");

			var registry = WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>();
			var managed = (await registry.GetManagedAttributesAsync("gmcp-room")).Select(m => m.Attribute).ToList();
			await Assert.That(managed).Contains("OBJECT`MOVE`GMCPROOM").And.Contains("PLAYER`CONNECT`GMCPROOM");
		}
		finally
		{
			await UninstallAsync(["mud-kit", .. five.Reverse()]);
		}
	}

	#endregion
}
