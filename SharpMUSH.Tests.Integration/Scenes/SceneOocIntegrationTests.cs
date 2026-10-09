using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// The Scene plugin's <c>ooc &lt;text&gt;</c> command, and the author's objid on the realtime scene event.
///
/// <para><c>ooc</c> speaks out of character to the room as <c>&lt;OOC&gt; Name: text</c> (or
/// <c>&lt;OOC&gt; Name waves</c> after a <c>:</c>, <c>&lt;OOC&gt; Name's</c> after a <c>;</c>). When the
/// speaker is focused on the active scene in the room they are standing in — the rule the capture hooks
/// use for a pose — it is also recorded there as the <c>ooc</c> type, which is what the portal's OOC band
/// keys off. Anywhere else it is only said.</para>
///
/// <para>Every test digs its own room: a room holds one active scene, and a player's OOC must never be
/// heard, or recorded, anywhere but there.</para>
/// </summary>
[NotInParallel]
public class SceneOocIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private readonly ConcurrentDictionary<long, DBRef> _actors = new();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message.ToPlainText().Trim();

	private async Task<CallState> God1(string command) =>
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private static string Num(string dbref)
	{
		var s = dbref.Trim();
		var colon = s.IndexOf(':');
		return colon < 0 ? s : s[..colon];
	}

	private async Task RunAs(long handle, string command) =>
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));

	// The recorder keys on the recipient's full objid, which is what pmatch() answers: a bare #N would
	// find nobody, and a negative assertion over nobody passes whatever happened.
	private IReadOnlyList<string> HeardBy(string objid, int fromCount) =>
		[.. Notifications.For(DBRef.Parse(objid)).Skip(fromCount)];

	private int HeardCount(string objid) => Notifications.CountFor(DBRef.Parse(objid));

	private async Task<(string Dbref, long Handle, string Name)> CreatePlayerAsync(string prefix)
	{
		// Player names stop at 15 characters, which GenerateUniqueName's suffix alone would overrun.
		var name = $"{prefix}{Guid.NewGuid():N}"[..15];
		await God1($"@pcreate {name}=pw-{name}");
		var dbref = (await God1($"think [pmatch({name})]")).Message.ToPlainText()?.Trim() ?? string.Empty;
		if (!DBRef.TryParse(dbref, out var parsed) || parsed is null)
			throw new InvalidOperationException($"Failed to create player {name}; pmatch returned '{dbref}'.");

		await God1($"@role/assign {dbref}=approved");
		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, parsed.Value);
		_actors[handle] = parsed.Value;
		return (dbref, handle, name);
	}

	private async Task<string> DigAsync(string prefix)
	{
		var dug = (await God1($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}")).Message.ToPlainText()?.Trim()
			?? string.Empty;
		return dug.Split(' ').First(t => t.StartsWith('#'));
	}

	/// <summary>See <c>SceneWebComposeIntegrationTests.PutLoggerInMasterRoomAsync</c>: other suites move it.</summary>
	private async Task PutLoggerInMasterRoomAsync()
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var objects = await registry.GetPackageObjectsAsync("scene");
		var logger = DBRef.Parse(objects.Single(o => o.Ref == "logger").Objid).ToString();
		await God1($"@teleport {logger}=#2");
	}

	/// <summary>A room with an active scene its owner is focused on, and a witness standing in it.</summary>
	private async Task<(string SceneId, (string Dbref, long Handle, string Name) Poser, string Witness)> SceneRoomAsync(
		string prefix)
	{
		await PutLoggerInMasterRoomAsync();
		var room = await DigAsync($"{prefix}Room");
		var poser = await CreatePlayerAsync($"{prefix}P");
		var (witness, _, _) = await CreatePlayerAsync($"{prefix}W");
		await God1($"@tel {poser.Dbref}={room}");
		await God1($"@tel {witness}={room}");

		await RunAs(poser.Handle, $"+scene/create {prefix} scene");
		var sceneId = await Eval($"scenefocus({Num(poser.Dbref)})");
		if (sceneId.StartsWith("#-1", StringComparison.Ordinal))
			throw new InvalidOperationException($"+scene/create left no focus: {sceneId}");

		return (sceneId, poser, witness);
	}

	private async Task<string> PoseCountAsync(string sceneId) => await Eval($"words(sceneposes({sceneId}))");

	private async Task<string> LastPoseAsync(string sceneId, string field) =>
		await Eval($"scenepose({sceneId},[last(sceneposes({sceneId}))],{field})");

	[Test]
	public async Task Ooc_in_a_scene_is_recorded_as_the_ooc_type_and_heard_as_the_band()
	{
		var (sceneId, poser, witness) = await SceneRoomAsync("Rec");
		var before = int.Parse(await PoseCountAsync(sceneId));
		var witnessBefore = HeardCount(witness);

		await RunAs(poser.Handle, "ooc brb, phone");

		await Assert.That(await PoseCountAsync(sceneId)).IsEqualTo((before + 1).ToString());
		await Assert.That(await LastPoseAsync(sceneId, "content")).IsEqualTo($"{poser.Name}: brb, phone");
		await Assert.That(await LastPoseAsync(sceneId, "type")).IsEqualTo("ooc");
		await Assert.That(await LastPoseAsync(sceneId, "source")).IsEqualTo("ooc");
		await Assert.That(await LastPoseAsync(sceneId, "tags")).IsEqualTo("");
		await Assert.That(Num(await LastPoseAsync(sceneId, "author"))).IsEqualTo(Num(poser.Dbref));
		await Assert.That(HeardBy(witness, witnessBefore))
			.Contains($"<OOC> {poser.Name}: brb, phone");
	}

	[Test]
	public async Task Ooc_colon_and_semicolon_pose_and_semipose()
	{
		var (sceneId, poser, witness) = await SceneRoomAsync("Pos");
		var witnessBefore = HeardCount(witness);

		await RunAs(poser.Handle, "ooc :waves.");
		await Assert.That(await LastPoseAsync(sceneId, "content")).IsEqualTo($"{poser.Name} waves.");

		await RunAs(poser.Handle, "ooc ;'s back.");
		await Assert.That(await LastPoseAsync(sceneId, "content")).IsEqualTo($"{poser.Name}'s back.");

		var heard = HeardBy(witness, witnessBefore);
		await Assert.That(heard).Contains($"<OOC> {poser.Name} waves.");
		await Assert.That(heard).Contains($"<OOC> {poser.Name}'s back.");
	}

	/// <summary>
	/// Focus alone is not enough: a player focused on a scene elsewhere is only heard where they stand,
	/// and nothing is written into the scene they left.
	/// </summary>
	[Test]
	public async Task Ooc_away_from_the_focused_scene_is_heard_and_not_recorded()
	{
		var (sceneId, poser, _) = await SceneRoomAsync("Awy");
		var elsewhere = await DigAsync("OocElsewhere");
		var (bystander, _, _) = await CreatePlayerAsync("By");
		await God1($"@tel {poser.Dbref}={elsewhere}");
		await God1($"@tel {bystander}={elsewhere}");
		var before = await PoseCountAsync(sceneId);
		var bystanderBefore = HeardCount(bystander);

		await RunAs(poser.Handle, "ooc anyone around?");

		await Assert.That(await PoseCountAsync(sceneId)).IsEqualTo(before);
		await Assert.That(HeardBy(bystander, bystanderBefore)).Contains($"<OOC> {poser.Name}: anyone around?");
	}

	/// <summary>
	/// Approval is re-checked on every write, as the scene package's capture hooks do: focus and
	/// membership survive a revoked approved role, so they cannot stand in for it.
	/// </summary>
	[Test]
	public async Task Ooc_from_a_player_no_longer_approved_is_heard_and_not_recorded()
	{
		var (sceneId, poser, witness) = await SceneRoomAsync("Una");
		await God1($"@role/unassign {poser.Dbref}=approved");
		var before = await PoseCountAsync(sceneId);
		var witnessBefore = HeardCount(witness);

		await RunAs(poser.Handle, "ooc still here");

		await Assert.That(HeardBy(witness, witnessBefore)).Contains($"<OOC> {poser.Name}: still here");
		await Assert.That(await PoseCountAsync(sceneId)).IsEqualTo(before);
	}

	/// <summary>
	/// The speaker is named as <c>say</c> and <c>pose</c> name them (NAMEACCENT here), and the scene
	/// records the name the room heard.
	/// </summary>
	[Test]
	public async Task Ooc_names_the_speaker_as_speech_does()
	{
		var (sceneId, poser, witness) = await SceneRoomAsync("Acc");
		await God1($"&NAMEACCENT {poser.Dbref}='{new string('-', poser.Name.Length - 1)}");
		var accented = "Á" + poser.Name[1..];
		var witnessBefore = HeardCount(witness);

		await RunAs(poser.Handle, "ooc hi all");

		await Assert.That(HeardBy(witness, witnessBefore)).Contains($"<OOC> {accented}: hi all");
		await Assert.That(await LastPoseAsync(sceneId, "content")).IsEqualTo($"{accented}: hi all");
	}

	/// <summary>
	/// SPEECHMOD transforms what was said — once, and only the words, as it does for a pose — and the
	/// scene records exactly what the room heard.
	/// </summary>
	[Test]
	public async Task Ooc_applies_SPEECHMOD_to_the_words_and_records_what_was_heard()
	{
		var (sceneId, poser, witness) = await SceneRoomAsync("Mod");
		await God1($"&SPEECHMOD {poser.Dbref}=[ucstr(%0)]");
		var witnessBefore = HeardCount(witness);

		await RunAs(poser.Handle, "ooc quiet words");

		await Assert.That(HeardBy(witness, witnessBefore)).Contains($"<OOC> {poser.Name}: QUIET WORDS");
		await Assert.That(await LastPoseAsync(sceneId, "content")).IsEqualTo($"{poser.Name}: QUIET WORDS");
	}

	[Test]
	public async Task Ooc_with_no_scene_at_all_is_only_heard()
	{
		var room = await DigAsync("OocPlain");
		var speaker = await CreatePlayerAsync("Spk");
		var (listener, _, _) = await CreatePlayerAsync("Lsn");
		await God1($"@tel {speaker.Dbref}={room}");
		await God1($"@tel {listener}={room}");
		var listenerBefore = HeardCount(listener);

		await RunAs(speaker.Handle, "ooc hello");

		await Assert.That(HeardBy(listener, listenerBefore)).Contains($"<OOC> {speaker.Name}: hello");
		await Assert.That(await Eval($"scenefocus({Num(speaker.Dbref)})")).StartsWith("#-1");
	}

	[Test]
	public async Task Ooc_with_nothing_to_say_says_nothing()
	{
		var (sceneId, poser, witness) = await SceneRoomAsync("Emp");
		var before = await PoseCountAsync(sceneId);
		var witnessBefore = HeardCount(witness);

		await RunAs(poser.Handle, "ooc :");

		await Assert.That(await PoseCountAsync(sceneId)).IsEqualTo(before);
		await Assert.That(HeardBy(witness, witnessBefore).Any(m => m.Contains("<OOC>", StringComparison.Ordinal)))
			.IsFalse();
	}

	/// <summary>A room whose Speech lock refuses the speaker hears nothing, and nothing is recorded.</summary>
	[Test]
	public async Task Ooc_refused_by_the_speech_lock_says_and_records_nothing()
	{
		var (sceneId, poser, witness) = await SceneRoomAsync("Lck");
		var room = await Eval($"loc({Num(poser.Dbref)})");
		await God1($"@lock/speech {room}=#1");
		var before = await PoseCountAsync(sceneId);
		var witnessBefore = HeardCount(witness);

		await RunAs(poser.Handle, "ooc hush");

		await Assert.That(await PoseCountAsync(sceneId)).IsEqualTo(before);
		await Assert.That(HeardBy(witness, witnessBefore).Any(m => m.Contains("hush", StringComparison.Ordinal)))
			.IsFalse();
	}

	[Test]
	public async Task Ooc_from_a_gagged_player_is_refused()
	{
		var (sceneId, poser, witness) = await SceneRoomAsync("Gag");
		await God1($"@set {poser.Dbref}=GAGGED");
		var before = await PoseCountAsync(sceneId);
		var witnessBefore = HeardCount(witness);

		await RunAs(poser.Handle, "ooc muffled");

		await Assert.That(await PoseCountAsync(sceneId)).IsEqualTo(before);
		await Assert.That(HeardBy(witness, witnessBefore).Any(m => m.Contains("muffled", StringComparison.Ordinal)))
			.IsFalse();
	}

	[Test]
	public async Task Ooc_noeval_keeps_brackets_literal()
	{
		var (sceneId, poser, witness) = await SceneRoomAsync("Nev");
		var witnessBefore = HeardCount(witness);

		await RunAs(poser.Handle, "ooc/noeval try [add(1,2)]");

		await Assert.That(await LastPoseAsync(sceneId, "content")).IsEqualTo($"{poser.Name}: try [add(1,2)]");
		await Assert.That(HeardBy(witness, witnessBefore)).Contains($"<OOC> {poser.Name}: try [add(1,2)]");
	}

	/// <summary>
	/// Colour survives into the record. Compared with an uncoloured line's markup, since storage serialises
	/// plain text too and markup never equals content.
	/// </summary>
	[Test]
	public async Task Ooc_records_colour_with_its_markup()
	{
		var (sceneId, poser, _) = await SceneRoomAsync("Clr");
		await RunAs(poser.Handle, "ooc a red ember");
		var plainMarkup = await LastPoseAsync(sceneId, "markup");

		await RunAs(poser.Handle, "ooc a [ansi(hr,red)] ember");

		await Assert.That(await LastPoseAsync(sceneId, "content")).IsEqualTo($"{poser.Name}: a red ember");
		await Assert.That(await LastPoseAsync(sceneId, "markup")).IsNotEqualTo(plainMarkup);
	}

	/// <summary>
	/// The built-in shadows a game's own master-room <c>$ooc</c> command; <c>@command/disable OOC</c> takes
	/// it out of the table, and the line falls through to the game's command again.
	/// </summary>
	[Test]
	public async Task Disabling_ooc_lets_a_games_own_ooc_command_answer()
	{
		var speaker = await CreatePlayerAsync("Dis");
		var room = await DigAsync("OocDisRoom");
		await God1($"@tel {speaker.Dbref}={room}");
		var marker = Guid.NewGuid().ToString("N")[..10];
		var created = (await God1($"@create OocSoftcode{marker}")).Message.ToPlainText()?.Trim() ?? string.Empty;
		var softcode = created.Split(' ').First(t => t.StartsWith('#'));
		await God1($"&CMD`OOC {softcode}=$ooc *:@pemit %#=GAMEOOC{marker} %0");
		await God1($"@set {softcode}=!NO_COMMAND");
		await God1($"@tel {softcode}=#2");

		try
		{
			var before = HeardCount(speaker.Dbref);
			await RunAs(speaker.Handle, "ooc builtin");
			await Assert.That(HeardBy(speaker.Dbref, before)).Contains($"<OOC> {speaker.Name}: builtin");

			await God1("@command/disable OOC");
			before = HeardCount(speaker.Dbref);
			await RunAs(speaker.Handle, "ooc softcode");
			await Assert.That(HeardBy(speaker.Dbref, before)).Contains($"GAMEOOC{marker} softcode");
		}
		finally
		{
			await God1("@command/enable OOC");
			await God1($"@nuke {softcode}");
			await God1($"@nuke {softcode}");
		}
	}

	/// <summary>
	/// The realtime event names its author by objid, resolved when it is sent from the dbref the pose
	/// stores (so it is whoever holds that dbref at the time).
	/// </summary>
	[Test]
	public async Task The_scene_event_carries_the_authors_objid()
	{
		var (sceneId, poser, _) = await SceneRoomAsync("Obj");
		var objid = await Eval($"objid({Num(poser.Dbref)})");
		await Assert.That(objid).Contains(":");

		await using var nats = new NatsConnection(new NatsOpts
		{
			Url = $"nats://localhost:{WebAppFactoryArg.NatsTestServer.Instance.GetMappedPublicPort(4222)}"
		});
		await using var events = await nats.SubscribeCoreAsync<string>($"game.scene.{sceneId}");
		await nats.PingAsync();

		await RunAs(poser.Handle, "ooc portrait check");

		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		JsonElement? received = null;
		await foreach (var message in events.Msgs.ReadAllAsync(timeout.Token))
		{
			if (message.Data is not { } data) continue;
			var root = JsonDocument.Parse(data).RootElement.Clone();
			if (root.GetProperty("Content").GetString()?.Contains("portrait check", StringComparison.Ordinal) != true) continue;
			received = root;
			break;
		}

		if (received is not { } sceneEvent)
		{
			Assert.Fail("no scene event carried the pose");
			return;
		}

		await Assert.That(sceneEvent.GetProperty("ActorObjId").GetString()).IsEqualTo(objid);
		await Assert.That(sceneEvent.GetProperty("Type").GetString()).IsEqualTo("ooc");
	}
}
