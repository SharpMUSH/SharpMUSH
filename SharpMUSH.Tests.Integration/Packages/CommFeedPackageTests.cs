using System.Text.Json.Nodes;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Packages;

/// <summary>
/// The bundled <c>comm-feed</c> package turns the engine's <c>CHANNEL`MESSAGE</c>, <c>PAGE`MESSAGE</c>,
/// <c>PLAYER`CHANNELS</c> and <c>CHANNEL`WHO</c> events into the <c>comm.message</c>, <c>comm.channels</c>
/// and <c>comm.who</c> OOB pushes the portal's Play sidebar reads (docs/softcode/comm-feed-handler.md).
///
/// <para>Every test runs the real path: players on websocket connections, the command they would type,
/// the event the engine raises, the package's attributes as installed at boot, and <c>oob()</c> publishing
/// to NATS. What each connection was sent is read off the NATS subject the connection server consumes, up
/// to a probe each watched player sends itself afterwards — so "received nothing" is an observation, not a
/// timeout.</para>
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class CommFeedPackageTests(ServerWebAppFactory factory)
{
	private IMediator Mediator => factory.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => factory.Services.GetRequiredService<IConnectionService>();

	private IPackageRegistryService Registry =>
		(IPackageRegistryService)factory.Services.GetRequiredService<ISharpDatabase>();

	/// <summary>A player on a websocket connection: what the portal holds.</summary>
	private sealed record Viewer(DBRef DbRef, long Handle, string Name)
	{
		public string Number => $"#{DbRef.Number}";
	}

	private async Task<Viewer> ViewerAsync(string prefix)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		var options = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();

		// Made and connected in a room of its own, as the notification tests do. A viewer that arrives in
		// DefaultHome, or leaves it, has ROOM`CONTENTS queued for DefaultHome — a room the whole session
		// fills, so each refresh of it builds a row for everything there, on the one queue every test's
		// events wait behind.
		var dug = await factory.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName($"{prefix}Room")}"));
		var room = DBRef.Parse(dug.Message.ToPlainText().Trim());
		var player = await Mediator.Send(new CreatePlayerCommand(
			name, "TestPassword123", room, room, (int)options.CurrentValue.Limit.StartingQuota));
		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, player, "websocket");

		return new Viewer(player, handle, name);
	}

	private Task God(string command) =>
		factory.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	private Task Run(Viewer who, string command) =>
		factory.CommandParser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command)).AsTask();

	private async Task<string> Objid(Viewer who) =>
		(await factory.FunctionParser.FunctionParse(MarkupText.Plain($"objid({who.Number})")))!.Message.ToPlainText();

	private static string UniqueChannel(string prefix) =>
		TestIsolationHelpers.GenerateUniqueName(prefix).Replace("_", string.Empty);

	/// <summary>A player-joinable channel with <paramref name="members"/> on it, made by God.</summary>
	private Task<string> ChannelAsync(string prefix, params Viewer[] members) =>
		ChannelAsync(prefix, "player open hide_ok", members);

	private async Task<string> ChannelAsync(string prefix, string privileges, params Viewer[] members)
	{
		var name = UniqueChannel(prefix);
		await God($"@channel/add {name}={privileges}");
		foreach (var member in members)
		{
			await God($"@channel/on {name}={member.Number}");
		}

		return name;
	}

	/// <summary>
	/// Watches the NATS subject websocket output is published on. <see cref="SentWhile"/> runs an action,
	/// waits for the events it queued to run, and then has every watched player <c>oob()</c> itself a probe;
	/// since one publisher's messages arrive in order, a handle's probe arriving means everything published
	/// to it before the probe has too.
	/// </summary>
	private sealed class OobWatch : IAsyncDisposable
	{
		private readonly NatsConnection _nats;
		private readonly INatsSub<WebSocketOutputMessage> _sub;
		private readonly ServerWebAppFactory _factory;

		private OobWatch(NatsConnection nats, INatsSub<WebSocketOutputMessage> sub, ServerWebAppFactory factory)
		{
			_nats = nats;
			_sub = sub;
			_factory = factory;
		}

		public static async Task<OobWatch> OpenAsync(ServerWebAppFactory factory)
		{
			// What the test's setup queued (a join's PLAYER`CHANNELS, say) is pushed before the watch starts.
			await factory.QueueBarrierAsync();
			var nats = new NatsConnection(new NatsOpts
			{
				Url = $"nats://localhost:{factory.NatsTestServer.Instance.GetMappedPublicPort(4222)}"
			});
			var subject = NatsSubjects.For(typeof(WebSocketOutputMessage),
				factory.Services.GetRequiredService<NatsOptions>().SubjectPrefix);
			var sub = await nats.SubscribeCoreAsync(subject,
				serializer: CompressingNatsSerializer<WebSocketOutputMessage>.Default);
			// The subscription is in place on the server before anything is published.
			await nats.PingAsync();
			return new OobWatch(nats, sub, factory);
		}

		/// <summary>The OOB frames (package, data) each watched viewer's connection was sent while <paramref name="action"/> ran.</summary>
		public async Task<Dictionary<long, List<(string Package, JsonNode? Data)>>> SentWhile(
			Func<Task> action, Func<Viewer, string, Task> run, params Viewer[] watched)
		{
			await action();
			// The events the action raised are queue entries of their own; their pushes come once they run.
			await _factory.QueueBarrierAsync();

			var probe = $"probe.{Guid.NewGuid():N}";
			foreach (var viewer in watched)
			{
				await run(viewer, $"think [null(oob(%#,{probe}))]");
			}

			var handles = watched.Select(v => v.Handle).ToHashSet();
			var sent = watched.ToDictionary(v => v.Handle, _ => new List<(string, JsonNode?)>());
			var probed = new HashSet<long>();
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			await foreach (var message in _sub.Msgs.ReadAllAsync(timeout.Token))
			{
				if (message.Data is not { } output || !handles.Contains(output.Handle)) continue;
				if (JsonNode.Parse(output.Data) is not JsonObject envelope) continue;

				var package = envelope["package"]?.GetValue<string>() ?? string.Empty;
				if (package == probe)
				{
					probed.Add(output.Handle);
					if (probed.Count == handles.Count) break;
					continue;
				}

				sent[output.Handle].Add((package, envelope["data"]?.DeepClone()));
			}

			return sent;
		}

		public async ValueTask DisposeAsync()
		{
			await _sub.DisposeAsync();
			await _nats.DisposeAsync();
		}
	}

	private static List<JsonObject> Frames(List<(string Package, JsonNode? Data)> sent, string package) =>
		sent.Where(f => f.Package == package).Select(f => f.Data).OfType<JsonObject>().ToList();

	private static string[] Strings(JsonNode? array) =>
		array is JsonArray items ? items.Select(i => i!.GetValue<string>()).ToArray() : [];

	[Test]
	public async Task IsInstalledAtBoot_OnTheConfiguredEventHandler()
	{
		if (await Registry.GetInstalledPackageAsync("comm-feed") is not InstalledPackageRecord)
			throw new InvalidOperationException("comm-feed is not installed.");

		var managed = await Registry.GetManagedAttributesAsync("comm-feed");
		var names = managed.Select(m => m.Attribute).ToList();
		await Assert.That(names).Contains("CHANNEL`MESSAGE");
		await Assert.That(names).Contains("PAGE`MESSAGE");
		await Assert.That(names).Contains("PLAYER`CHANNELS");
		await Assert.That(names).Contains("CHANNEL`WHO");
		await Assert.That((await Registry.GetPackageObjectsAsync("comm-feed")).Count).IsEqualTo(0);

		var configured = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>()
			.CurrentValue.Database.EventHandler;
		await Assert.That(managed.Single(m => m.Attribute == "CHANNEL`MESSAGE").Objid).StartsWith($"#{configured}:");
	}

	[Test]
	public async Task ChannelLine_ReachesItsMembers_AndNobodyElse()
	{
		var speaker = await ViewerAsync("CommSpeaker");
		var member = await ViewerAsync("CommMember");
		var outsider = await ViewerAsync("CommOutsider");
		var channel = await ChannelAsync("CommLine", speaker, member);
		var marker = TestIsolationHelpers.GenerateUniqueName("line");
		var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(() => Run(speaker, $"@chat {channel}=hello, [add(1,2)] \\[add(1,2)\\] {marker}"), Run,
			speaker, member, outsider);

		await Assert.That(Frames(sent[outsider.Handle], "comm.message")).IsEmpty()
			.Because("someone not on the channel did not see the line in their terminal");

		var speakerObjid = await Objid(speaker);
		foreach (var message in new[] { speaker, member }.Select(who => Frames(sent[who.Handle], "comm.message").Single()))
		{
			await Assert.That(message["v"]!.GetValue<int>()).IsEqualTo(2);
			await Assert.That(message["kind"]!.GetValue<string>()).IsEqualTo("channel");
			await Assert.That(message["channel"]!.GetValue<string>()).IsEqualTo(channel);
			await Assert.That(Strings(message["to"])).IsEmpty();
			await Assert.That(message["from"]!.GetValue<string>()).IsEqualTo(speaker.Name);
			await Assert.That(message["fromObjid"]!.GetValue<string>()).IsEqualTo(speakerObjid);
			await Assert.That(message["style"]!.GetValue<string>()).IsEqualTo("say");
			// @chat evaluated the message once, as the speaker typed it; the handler passes the result on
			// without evaluating it again, so the escaped brackets arrive as text and the comma survives.
			await Assert.That(message["text"]!.GetValue<string>()).IsEqualTo($"hello, 3 [add(1,2)] {marker}");
			await Assert.That(message["ts"]!.GetValue<long>()).IsGreaterThanOrEqualTo(before);
		}
	}

	/// <summary>
	/// A line's <c>id</c> is the id the recall endpoint returns for it, so the portal can tell a line it
	/// pulled from one pushed to it and keep one copy.
	/// </summary>
	[Test]
	public async Task ChannelLine_CarriesTheIdTheRecallEndpointReturnsForIt()
	{
		var speaker = await ViewerAsync("CommIdSpeaker");
		var member = await ViewerAsync("CommIdMember");
		var channel = await ChannelAsync("CommId", speaker, member);
		var marker = TestIsolationHelpers.GenerateUniqueName("id");

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(() => Run(speaker, $"@chat {channel}={marker}"), Run, member);

		var pushed = Frames(sent[member.Handle], "comm.message").Single();
		var recall = await (await Portal.PortalControllers.CommControllerAs(factory, member.DbRef))
			.Recall(channel, null, null, CancellationToken.None);
		var pulled = recall.Value!.Single(line => line.Text == marker);

		await Assert.That(pushed["id"]!.GetValue<long>()).IsEqualTo(pulled.Id);
	}

	/// <summary>
	/// A page's <c>id</c> is the id the page log keeps it under, so the portal keeps one copy of a page it
	/// both pulled from the log and was pushed. The pager's and the recipient's pushes carry the same one.
	/// </summary>
	[Test]
	public async Task Page_CarriesTheIdThePageLogKeepsItUnder()
	{
		var pager = await ViewerAsync("CommIdPager");
		var recipient = await ViewerAsync("CommIdPaged");
		var marker = TestIsolationHelpers.GenerateUniqueName("pageid");
		using var pageLog = TestOptionsOverride.Scope(options => options with { Chat = options.Chat with { PageLog = true } });

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(() => Run(pager, $"page {recipient.Name}={marker}"), Run, pager, recipient);

		var pushedToRecipient = Frames(sent[recipient.Handle], "comm.message").Single();
		var pushedToPager = Frames(sent[pager.Handle], "comm.message").Single();
		var recall = await (await Portal.PortalControllers.CommControllerAs(factory, recipient.DbRef))
			.ConversationRecall(await Objid(pager), null, null, CancellationToken.None);
		var pulled = recall.Value!.Lines.Single(line => line.Text == marker);

		await Assert.That(pushedToRecipient["id"]!.GetValue<long>()).IsEqualTo(pulled.Id);
		await Assert.That(pushedToPager["id"]!.GetValue<long>()).IsEqualTo(pulled.Id);
	}

	/// <summary>
	/// A logged page's recalled <c>text</c> goes through the installed <c>FN`COMM`TEXT</c> as the push's
	/// does, with the pager as enactor, so a game that redefined it gets the same page text pulled as pushed.
	/// Like <see cref="ChannelLine_RecallComposesTextThroughTheInstalledFnCommText"/>, the redefinition
	/// answers differently only for this test's marker.
	/// </summary>
	// Both FnCommText tests redefine and then restore the one attribute; run together, one restores it under the other.
	[Test, NotInParallel("FnCommText")]
	public async Task Page_RecallComposesTextThroughTheInstalledFnCommText()
	{
		var pager = await ViewerAsync("CommPageTextPager");
		var recipient = await ViewerAsync("CommPageTextPaged");
		var marker = TestIsolationHelpers.GenerateUniqueName("pagecustom");
		var handler = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Database.EventHandler;
		var original = (await factory.FunctionParser.FunctionParse(MarkupText.Plain($"get(#{handler}/FN`COMM`TEXT)")))!
			.Message.ToPlainText();
		using var pageLog = TestOptionsOverride.Scope(options => options with { Chat = options.Chat with { PageLog = true } });

		await God($"&FN`COMM`TEXT #{handler}=switch(%2,*{marker}*,custom:%#:%!:%0:%1:%2,switch(%0,pose,%1 %2,semipose,%1%2,%2))");
		try
		{
			await using var watch = await OobWatch.OpenAsync(factory);
			var sent = await watch.SentWhile(() => Run(pager, $"page {recipient.Name}=:nods {marker}"), Run, recipient);

			var pushed = Frames(sent[recipient.Handle], "comm.message").Single();
			var recall = await (await Portal.PortalControllers.CommControllerAs(factory, recipient.DbRef))
				.ConversationRecall(await Objid(pager), null, null, CancellationToken.None);
			var pulled = recall.Value!.Lines.Single(line => line.Id == pushed["id"]!.GetValue<long>());

			await Assert.That(pushed["text"]!.GetValue<string>()).StartsWith("custom:")
				.Because("the redefinition is what the push composed with");
			await Assert.That(pulled.Text).IsEqualTo(pushed["text"]!.GetValue<string>());
		}
		finally
		{
			await God($"&FN`COMM`TEXT #{handler}={original}");
		}
	}

	/// <summary>A page has an id with the page log off too: a resumed connection's replay is still kept once.</summary>
	[Test]
	public async Task Page_CarriesAnId_WithThePageLogOff()
	{
		var pager = await ViewerAsync("CommIdUnloggedPager");
		var recipient = await ViewerAsync("CommIdUnloggedPaged");
		using var pageLog = TestOptionsOverride.Scope(options => options with { Chat = options.Chat with { PageLog = false } });

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(() => Run(pager, $"page {recipient.Name}=unlogged"), Run, recipient);

		await Assert.That(Frames(sent[recipient.Handle], "comm.message").Single()["id"]!.GetValue<long>()).IsGreaterThan(0);
	}

	/// <summary>
	/// Recall composes a line's <c>text</c> through the installed <c>FN`COMM`TEXT</c>, as the push does, with
	/// the same executor (the handler), enactor (the speaker) and arguments — so a game that redefined it
	/// gets the same text pulled as pushed.
	/// </summary>
	/// <remarks>
	/// The tests share one world and run in parallel, so the redefinition answers differently only for a
	/// line carrying this test's marker and composes every other line exactly as the bundled default does.
	/// </remarks>
	[Test, NotInParallel("FnCommText")]
	public async Task ChannelLine_RecallComposesTextThroughTheInstalledFnCommText()
	{
		var speaker = await ViewerAsync("CommTextSpeaker");
		var member = await ViewerAsync("CommTextMember");
		var channel = await ChannelAsync("CommText", speaker, member);
		var marker = TestIsolationHelpers.GenerateUniqueName("custom");
		var handler = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Database.EventHandler;
		var original = (await factory.FunctionParser.FunctionParse(MarkupText.Plain($"get(#{handler}/FN`COMM`TEXT)")))!
			.Message.ToPlainText();

		await God($"&FN`COMM`TEXT #{handler}=switch(%2,*{marker}*,custom:%#:%!:%0:%1:%2,switch(%0,pose,%1 %2,semipose,%1%2,%2))");
		try
		{
			await using var watch = await OobWatch.OpenAsync(factory);
			var sent = await watch.SentWhile(() => Run(speaker, $"@chat {channel}=:waves {marker}"), Run, member);

			var pushed = Frames(sent[member.Handle], "comm.message").Single();
			var recall = await (await Portal.PortalControllers.CommControllerAs(factory, member.DbRef))
				.Recall(channel, null, null, CancellationToken.None);
			var pulled = recall.Value!.Single(line => line.Id == pushed["id"]!.GetValue<long>());

			await Assert.That(pushed["text"]!.GetValue<string>()).StartsWith("custom:")
				.Because("the redefinition is what the push composed with");
			await Assert.That(pulled.Text).IsEqualTo(pushed["text"]!.GetValue<string>());
		}
		finally
		{
			await God($"&FN`COMM`TEXT #{handler}={original}");
		}
	}

	[Test]
	public async Task ChannelPose_TextIsThePoseAsTheTerminalReadsIt()
	{
		var speaker = await ViewerAsync("CommPoser");
		var member = await ViewerAsync("CommPoseMember");
		var channel = await ChannelAsync("CommPose", speaker, member);
		var marker = TestIsolationHelpers.GenerateUniqueName("pose");

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(() => Run(speaker, $"@chat {channel}=:waves {marker}"), Run, member);

		var message = Frames(sent[member.Handle], "comm.message").Single();
		await Assert.That(message["style"]!.GetValue<string>()).IsEqualTo("pose");
		await Assert.That(message["text"]!.GetValue<string>()).IsEqualTo($"{speaker.Name} waves {marker}");
	}

	[Test]
	public async Task ChannelLine_IsNotSentToAGaggedMember()
	{
		var speaker = await ViewerAsync("CommGagSpeaker");
		var gagged = await ViewerAsync("CommGagged");
		var listener = await ViewerAsync("CommGagListener");
		var channel = await ChannelAsync("CommGag", speaker, gagged, listener);
		await Run(gagged, $"@channel/gag {channel}=yes");

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(() => Run(speaker, $"@chat {channel}=anyone there"), Run,
			gagged, listener);

		await Assert.That(Frames(sent[gagged.Handle], "comm.message")).IsEmpty()
			.Because("a gagged member hears nothing on the channel");
		await Assert.That(Frames(sent[listener.Handle], "comm.message")).Count().IsEqualTo(1)
			.Because("the control: an ungagged member is sent the line");
	}

	[Test]
	public async Task ChannelLine_FromAHiddenSpeaker_ReachesMembersNamingTheSpeaker()
	{
		var hider = await ViewerAsync("CommHider");
		var member = await ViewerAsync("CommHiderMember");
		var channel = await ChannelAsync("CommHide", hider, member);
		await Run(hider, $"@channel/hide {channel}=yes");
		var marker = TestIsolationHelpers.GenerateUniqueName("hidden");

		var window = factory.Notifications.CountFor(member.DbRef);
		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(() => Run(hider, $"@chat {channel}={marker}"), Run, member);

		// Hiding keeps a member off the channel's who list; it does not anonymise what they say, and the
		// member's terminal names them. The payload says what the terminal says.
		await Assert.That(factory.Notifications.For(member.DbRef).Skip(window)
			.Any(line => line.Contains(marker) && line.Contains(hider.Name))).IsTrue();
		var message = Frames(sent[member.Handle], "comm.message").Single();
		await Assert.That(message["from"]!.GetValue<string>()).IsEqualTo(hider.Name);
		await Assert.That(message["text"]!.GetValue<string>()).IsEqualTo(marker);
	}

	/// <summary>
	/// An <c>@cemit</c> line does not name its emitter in a member's terminal (only a NOSPOOF member sees
	/// who sent it), so the payload names nobody either — with or without <c>/spoof</c>.
	/// </summary>
	[Test]
	[Arguments("@cemit")]
	[Arguments("@cemit/spoof")]
	public async Task ChannelEmit_DoesNotNameTheEmitter(string command)
	{
		var member = await ViewerAsync("CommEmitMember");
		var channel = await ChannelAsync("CommEmit", member);
		var marker = TestIsolationHelpers.GenerateUniqueName("emit");

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(() => God($"{command} {channel}={marker}"), Run, member);

		var message = Frames(sent[member.Handle], "comm.message").Single();
		await Assert.That(message["style"]!.GetValue<string>()).IsEqualTo("emit");
		await Assert.That(message["text"]!.GetValue<string>()).IsEqualTo(marker);
		await Assert.That(message["from"]!.GetValue<string>()).IsEqualTo(string.Empty);
		await Assert.That(message.ContainsKey("fromObjid")).IsFalse();
	}

	/// <summary>
	/// Connecting puts the member on their channels' member lists in the portal, in place of the connect line
	/// (#1579): the line still goes to the channel and its recall buffer, as on a terminal, but no
	/// <c>comm.message</c> carries it. The list follows <c>@channel/who</c>: a member hiding on the channel
	/// comes on only for a See_All member, and nobody else hears of it.
	/// </summary>
	[Test]
	public async Task Connecting_SendsNoLineToThePortal_AndPutsTheMemberOnItsList()
	{
		var comer = await ViewerAsync("CommWhoComer");
		var hider = await ViewerAsync("CommWhoHider");
		var mortal = await ViewerAsync("CommWhoMortal");
		var seer = await ViewerAsync("CommWhoSeer");
		await God($"@power {seer.Number}=See_All");
		var channel = await ChannelAsync("CommWhoConnect", comer, hider, mortal, seer);
		await Run(hider, $"@channel/hide {channel}=yes");
		var comerSocket = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService, "websocket");
		var hiderSocket = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService, "websocket");

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(async () =>
			{
				await factory.CommandParser.CommandParse(comerSocket, ConnectionService,
					MarkupText.Plain($"connect {comer.Name} TestPassword123"));
				await factory.CommandParser.CommandParse(hiderSocket, ConnectionService,
					MarkupText.Plain($"connect {hider.Name} TestPassword123"));
			},
			Run, mortal, seer);

		var buffered = await Mediator
			.CreateStream(new GetChannelMessagesQuery((await Mediator.Send(new GetChannelQuery(channel)))!.Id!, int.MaxValue))
			.ToListAsync();
		await Assert.That(buffered.Count(line => line.Style == "presence")).IsEqualTo(2)
			.Because("the channel is not quiet, so both connect lines went out");
		await Assert.That(Frames(sent[mortal.Handle], "comm.message")).IsEmpty()
			.Because("the portal is not sent connect lines");
		await Assert.That(Frames(sent[seer.Handle], "comm.message")).IsEmpty();

		var comerObjid = await Objid(comer);
		var hiderObjid = await Objid(hider);
		var mortalSaw = Frames(sent[mortal.Handle], "comm.who");
		await Assert.That(mortalSaw.Select(f => f["member"]!["objid"]!.GetValue<string>())).IsEquivalentTo(new[] { comerObjid })
			.Because("a mortal does not list a member hiding on the channel");
		var update = mortalSaw.Single();
		await Assert.That(update["v"]!.GetValue<int>()).IsEqualTo(2);
		await Assert.That(update["channel"]!.GetValue<string>()).IsEqualTo(channel);
		await Assert.That(update["member"]!["name"]!.GetValue<string>()).IsEqualTo(comer.Name);
		await Assert.That(update["online"]!.GetValue<bool>()).IsTrue();

		await Assert.That(Frames(sent[seer.Handle], "comm.who").Select(f => f["member"]!["objid"]!.GetValue<string>()))
			.IsEquivalentTo(new[] { comerObjid, hiderObjid });
	}

	/// <summary>
	/// Joining puts a connected member on the list, hiding takes them off it for those who may not see
	/// hidden members (and changes nothing for those who may), and leaving takes them off for whoever still
	/// listed them.
	/// </summary>
	[Test]
	public async Task JoiningHidingAndLeaving_UpdateTheMemberList_ForWhoeverSeesTheChange()
	{
		var subject = await ViewerAsync("CommWhoSubject");
		var mortal = await ViewerAsync("CommWhoWatcher");
		var seer = await ViewerAsync("CommWhoStaff");
		await God($"@power {seer.Number}=See_All");
		var channel = await ChannelAsync("CommWhoJoin", mortal, seer);
		var objid = await Objid(subject);

		static IEnumerable<(string, bool)> Changes(List<JsonObject> frames) =>
			frames.Select(f => (f["member"]!["objid"]!.GetValue<string>(), f["online"]!.GetValue<bool>()));

		await using var watch = await OobWatch.OpenAsync(factory);

		var joined = await watch.SentWhile(() => Run(subject, $"@channel/on {channel}"), Run, mortal, seer);
		await Assert.That(Changes(Frames(joined[mortal.Handle], "comm.who"))).IsEquivalentTo(new[] { (objid, true) });
		await Assert.That(Changes(Frames(joined[seer.Handle], "comm.who"))).IsEquivalentTo(new[] { (objid, true) });

		var hid = await watch.SentWhile(() => Run(subject, $"@channel/hide {channel}=yes"), Run, mortal, seer);
		await Assert.That(Changes(Frames(hid[mortal.Handle], "comm.who"))).IsEquivalentTo(new[] { (objid, false) });
		await Assert.That(Frames(hid[seer.Handle], "comm.who")).IsEmpty()
			.Because("a See_All member lists a hidden member all the same");

		var left = await watch.SentWhile(() => Run(subject, $"@channel/off {channel}"), Run, mortal, seer);
		await Assert.That(Frames(left[mortal.Handle], "comm.who")).IsEmpty()
			.Because("the mortal stopped listing them when they hid");
		await Assert.That(Changes(Frames(left[seer.Handle], "comm.who"))).IsEquivalentTo(new[] { (objid, false) });
	}

	[Test]
	public async Task DestroyingAThingOnAChannel_TakesItOffTheMemberList()
	{
		var watcher = await ViewerAsync("CommWhoDestroyWatcher");
		var channel = await ChannelAsync("CommWhoDestroy", "player object open", watcher);
		var created = await factory.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@create {TestIsolationHelpers.GenerateUniqueName("CommWhoDoomed")}"));
		var thing = DBRef.Parse(created.Message.ToPlainText().Trim());
		// @channel/on names only players; a thing joins itself, which is what this stands in for.
		await Mediator.Send(new AddUserToChannelCommand(
			(await Mediator.Send(new GetChannelQuery(channel)))!,
			(await Mediator.Send(new GetObjectNodeQuery(thing))).Expect<AnySharpObject>()));
		var objid = (await factory.FunctionParser.FunctionParse(MarkupText.Plain($"objid(#{thing.Number})")))!
			.Message.ToPlainText();

		await using var watch = await OobWatch.OpenAsync(factory);
		var destroyed = await watch.SentWhile(async () =>
		{
			await God($"@nuke #{thing.Number}");
			await God($"@nuke #{thing.Number}");
		}, Run, watcher);

		var changes = Frames(destroyed[watcher.Handle], "comm.who")
			.Select(f => (f["member"]!["objid"]!.GetValue<string>(), f["online"]!.GetValue<bool>()));
		await Assert.That(changes).IsEquivalentTo(new[] { (objid, false) })
			.Because("a destroyed member leaves its channels, so an open member list drops it");
	}

	[Test]
	public async Task PageToOne_ReachesThePagerAndTheRecipientOnly()
	{
		var pager = await ViewerAsync("CommPager");
		var recipient = await ViewerAsync("CommPaged");
		var bystander = await ViewerAsync("CommPageBystander");
		var marker = TestIsolationHelpers.GenerateUniqueName("page");

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(() => Run(pager, $"page {recipient.Name}=psst, {marker}"), Run,
			pager, recipient, bystander);

		await Assert.That(Frames(sent[bystander.Handle], "comm.message")).IsEmpty();

		var pagerObjid = await Objid(pager);
		var recipientObjid = await Objid(recipient);
		foreach (var message in new[] { pager, recipient }.Select(who => Frames(sent[who.Handle], "comm.message").Single()))
		{
			await Assert.That(message["v"]!.GetValue<int>()).IsEqualTo(2);
			await Assert.That(message["kind"]!.GetValue<string>()).IsEqualTo("page");
			await Assert.That(message.ContainsKey("channel")).IsFalse();
			await Assert.That(Strings(message["to"])).IsEquivalentTo(new[] { recipient.Name });
			await Assert.That(Strings(message["toObjids"])).IsEquivalentTo(new[] { recipientObjid });
			await Assert.That(message["from"]!.GetValue<string>()).IsEqualTo(pager.Name);
			await Assert.That(message["fromObjid"]!.GetValue<string>()).IsEqualTo(pagerObjid);
			await Assert.That(message["style"]!.GetValue<string>()).IsEqualTo("say");
			await Assert.That(message["text"]!.GetValue<string>()).IsEqualTo($"psst, {marker}");
		}
	}

	[Test]
	public async Task PageToSeveral_ReachesEveryRecipientAndNamesThemAll()
	{
		var pager = await ViewerAsync("CommGroupPager");
		var first = await ViewerAsync("CommGroupFirst");
		var second = await ViewerAsync("CommGroupSecond");
		var marker = TestIsolationHelpers.GenerateUniqueName("group");

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(() => Run(pager, $"page {first.Name} {second.Name}=:nods {marker}"), Run,
			pager, first, second);

		foreach (var message in new[] { pager, first, second }.Select(who => Frames(sent[who.Handle], "comm.message").Single()))
		{
			await Assert.That(Strings(message["to"])).IsEquivalentTo(new[] { first.Name, second.Name });
			await Assert.That(message["style"]!.GetValue<string>()).IsEqualTo("pose");
			await Assert.That(message["text"]!.GetValue<string>()).IsEqualTo($"{pager.Name} nods {marker}");
		}
	}

	[Test]
	public async Task PageRefusedByAPageLock_SendsNothing()
	{
		var pager = await ViewerAsync("CommLockedPager");
		var locked = await ViewerAsync("CommPageLocked");
		await God($"@lock/page {locked.Number}=#FALSE");

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(() => Run(pager, $"page {locked.Name}=let me in"), Run, pager, locked);

		await Assert.That(Frames(sent[pager.Handle], "comm.message")).IsEmpty();
		await Assert.That(Frames(sent[locked.Handle], "comm.message")).IsEmpty();
	}

	[Test]
	public async Task PageToAHavenPlayer_SendsNothing()
	{
		var pager = await ViewerAsync("CommHavenPager");
		var haven = await ViewerAsync("CommHaven");
		await God($"@set {haven.Number}=HAVEN");

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(() => Run(pager, $"page {haven.Name}=hello?"), Run, pager, haven);

		await Assert.That(Frames(sent[pager.Handle], "comm.message")).IsEmpty();
		await Assert.That(Frames(sent[haven.Handle], "comm.message")).IsEmpty();
	}

	/// <summary>
	/// A group page that some recipients refuse still reaches the others, and neither names nor reaches
	/// the ones who refused: <c>to</c> is who the page reached, as the terminal's "(to ...)" is. This is
	/// also the control for the two tests above, which pass trivially if nothing is ever sent.
	/// </summary>
	[Test]
	public async Task GroupPage_LeavesOutTheRecipientsWhoRefusedIt()
	{
		var pager = await ViewerAsync("CommMixedPager");
		var locked = await ViewerAsync("CommMixedLocked");
		var haven = await ViewerAsync("CommMixedHaven");
		var willing = await ViewerAsync("CommMixedWilling");
		await God($"@lock/page {locked.Number}=#FALSE");
		await God($"@set {haven.Number}=HAVEN");

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(
			() => Run(pager, $"page {locked.Name} {haven.Name} {willing.Name}=all of you"),
			Run, pager, locked, haven, willing);

		await Assert.That(Frames(sent[locked.Handle], "comm.message")).IsEmpty();
		await Assert.That(Frames(sent[haven.Handle], "comm.message")).IsEmpty();
		foreach (var message in new[] { pager, willing }.Select(who => Frames(sent[who.Handle], "comm.message").Single()))
		{
			await Assert.That(Strings(message["to"])).IsEquivalentTo(new[] { willing.Name });
		}
	}

	[Test]
	public async Task JoiningAndLeaving_PushTheJoinersChannelList()
	{
		var joiner = await ViewerAsync("CommJoiner");
		var other = await ViewerAsync("CommJoinOther");
		var channel = await ChannelAsync("CommJoin");
		var joinerObjid = await Objid(joiner);

		await using var watch = await OobWatch.OpenAsync(factory);
		var joined = await watch.SentWhile(() => Run(joiner, $"@channel/on {channel}"), Run, joiner, other);

		await Assert.That(Frames(joined[other.Handle], "comm.channels")).IsEmpty()
			.Because("only the player whose list changed is sent one");
		var list = Frames(joined[joiner.Handle], "comm.channels").Single();
		await Assert.That(list["v"]!.GetValue<int>()).IsEqualTo(2);
		await Assert.That(list["viewer"]!["objid"]!.GetValue<string>()).IsEqualTo(joinerObjid);
		await Assert.That(list["viewer"]!["name"]!.GetValue<string>()).IsEqualTo(joiner.Name);
		var row = list["channels"]!.AsArray().OfType<JsonObject>().Single(c => c["name"]!.GetValue<string>() == channel);
		await Assert.That(row["joined"]!.GetValue<bool>()).IsTrue();
		await Assert.That(row.ContainsKey("gagged")).IsFalse();

		var gagged = await watch.SentWhile(() => Run(joiner, $"@channel/gag {channel}=yes"), Run, joiner);
		var gagRow = Frames(gagged[joiner.Handle], "comm.channels").Single()["channels"]!.AsArray()
			.OfType<JsonObject>().Single(c => c["name"]!.GetValue<string>() == channel);
		await Assert.That(gagRow["gagged"]!.GetValue<bool>()).IsTrue();

		var left = await watch.SentWhile(() => Run(joiner, $"@channel/off {channel}"), Run, joiner);
		var after = Frames(left[joiner.Handle], "comm.channels").Single()["channels"]!.AsArray()
			.OfType<JsonObject>().Single(c => c["name"]!.GetValue<string>() == channel);
		await Assert.That(after["joined"]!.GetValue<bool>()).IsFalse()
			.Because("a channel left is still one the player may see, for the channel browser");
		await Assert.That(after.ContainsKey("gagged")).IsFalse();
	}

	/// <summary>
	/// The list is every channel the player may see, as their own <c>@channel/list</c> shows them: each says
	/// whether they are on it, how many are, and its description. A channel they may not see is not in it,
	/// though the wizard handler that builds the list sees it.
	/// </summary>
	[Test]
	public async Task TheChannelList_IsEveryChannelThePlayerMaySee()
	{
		var player = await ViewerAsync("CommListPlayer");
		var other = await ViewerAsync("CommListOther");
		var open = await ChannelAsync("CommListOpen", other);
		await God($"@channel/describe {open}=Ask anything\\, any time.");
		var hidden = await ChannelAsync("CommListWizard", "player wizard");
		var mine = await ChannelAsync("CommListMine");

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(() => Run(player, $"@channel/on {mine}"), Run, player);

		var rows = Frames(sent[player.Handle], "comm.channels").Single()["channels"]!.AsArray()
			.OfType<JsonObject>().ToDictionary(c => c["name"]!.GetValue<string>());
		await Assert.That(rows[open]["joined"]!.GetValue<bool>()).IsFalse();
		await Assert.That(rows[open]["members"]!.GetValue<int>()).IsEqualTo(2).Because("God made it and is on it, and so is the other player");
		await Assert.That(rows[open]["description"]!.GetValue<string>()).IsEqualTo("Ask anything, any time.");
		await Assert.That(rows[mine]["joined"]!.GetValue<bool>()).IsTrue();
		await Assert.That(rows[mine]["description"]!.GetValue<string>()).IsEqualTo(string.Empty);
		await Assert.That(rows.ContainsKey(hidden)).IsFalse();
	}

	/// <summary>
	/// A channel deleted or renamed after <c>channels()</c> listed it, by someone else while the list is
	/// being built, has no row: one missing count would otherwise make the whole payload invalid JSON,
	/// and <c>oob()</c> would send nothing.
	/// </summary>
	[Test]
	public async Task AChannelGoneSinceItWasListed_HasNoRow()
	{
		var handler = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Database.EventHandler;
		var gone = UniqueChannel("CommGone");
		var row = (await factory.FunctionParser.FunctionParse(
			MarkupText.Plain($"[u(#{handler}/FN`COMM`CHANNELROW,{gone},#1)]")))!.Message.ToPlainText();

		await Assert.That(row).IsEqualTo(string.Empty);
	}

	/// <summary>
	/// A listed name that is gone but abbreviates another channel (renamed from <c>Public</c> to
	/// <c>Public Chat</c>, say) still has no row: the lookup would take the other channel's figures under
	/// the stale name.
	/// </summary>
	[Test]
	public async Task AChannelGoneThatAbbreviatesAnother_HasNoRow()
	{
		var handler = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Database.EventHandler;
		var longer = await ChannelAsync("CommAbbrev");
		var gone = longer[..^2];
		var row = (await factory.FunctionParser.FunctionParse(
			MarkupText.Plain($"[u(#{handler}/FN`COMM`CHANNELROW,{gone},#1)]")))!.Message.ToPlainText();

		await Assert.That(row).IsEqualTo(string.Empty);
	}

	/// <summary>
	/// Renaming or deleting a channel changes every member's list, not the admin's who did it, so each
	/// member is sent theirs. Deletion reads the members before the channel is gone.
	/// </summary>
	[Test]
	public async Task RenamingAndDeleting_PushEveryMembersChannelList()
	{
		var first = await ViewerAsync("CommRenameFirst");
		var second = await ViewerAsync("CommRenameSecond");
		var channel = await ChannelAsync("CommRename", first, second);
		var renamed = UniqueChannel("CommRenamed");

		await using var watch = await OobWatch.OpenAsync(factory);
		var afterRename = await watch.SentWhile(() => God($"@channel/rename {channel}={renamed}"), Run, first, second);
		foreach (var who in new[] { first, second })
		{
			var names = Frames(afterRename[who.Handle], "comm.channels").Single()["channels"]!.AsArray()
				.OfType<JsonObject>().Select(c => c["name"]!.GetValue<string>()).ToList();
			await Assert.That(names).Contains(renamed);
			await Assert.That(names).DoesNotContain(channel);
		}

		var afterDelete = await watch.SentWhile(() => God($"@channel/delete {renamed}"), Run, first, second);
		foreach (var who in new[] { first, second })
		{
			var names = Frames(afterDelete[who.Handle], "comm.channels").Single()["channels"]!.AsArray()
				.OfType<JsonObject>().Select(c => c["name"]!.GetValue<string>()).ToList();
			await Assert.That(names).DoesNotContain(renamed);
		}
	}

	/// <summary>
	/// Creating a channel puts its creator on it (PennMUSH's do_chan_admin adds the owner), and that is a
	/// change to the creator's channel list like any join.
	/// </summary>
	[Test]
	public async Task CreatingAChannel_PushesTheCreatorsChannelList()
	{
		var creator = await ViewerAsync("CommCreator");
		var channel = UniqueChannel("CommCreate");

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(() => Run(creator, $"@channel/add {channel}=player"), Run, creator);

		var list = Frames(sent[creator.Handle], "comm.channels").Single();
		await Assert.That(list["channels"]!.AsArray().OfType<JsonObject>()
			.Any(c => c["name"]!.GetValue<string>() == channel)).IsTrue();
	}

	[Test]
	public async Task Connecting_PushesTheChannelList()
	{
		var player = await ViewerAsync("CommConnecter");
		var channel = await ChannelAsync("CommConnect", player);
		var socket = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService, "websocket");
		var connecting = player with { Handle = socket };

		await using var watch = await OobWatch.OpenAsync(factory);
		var sent = await watch.SentWhile(
			() => factory.CommandParser.CommandParse(socket, ConnectionService,
				MarkupText.Plain($"connect {player.Name} TestPassword123")).AsTask(),
			Run, connecting);

		var list = Frames(sent[socket], "comm.channels").Single();
		await Assert.That(list["channels"]!.AsArray().OfType<JsonObject>()
			.Any(c => c["name"]!.GetValue<string>() == channel)).IsTrue();
	}
}
