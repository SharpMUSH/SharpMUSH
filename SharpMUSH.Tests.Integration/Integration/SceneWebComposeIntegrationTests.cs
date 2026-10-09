using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The <c>+scene/emit|pose|say|semipose &lt;id&gt;=&lt;text&gt;</c> verbs, which exist so the portal's
/// live-scene compose box has a command to send.
///
/// <para>The capture hooks record a pose only when the poser is focused on an active scene in the room
/// they are standing in. That rule is right for a MU* client, where you pose at the room you are in,
/// and wrong for the web, where you are reading one specific scene's page: the page names the scene,
/// and the pose typed into it must reach that scene rather than depending on where the character
/// happens to be standing. So these verbs take the scene id explicitly and record regardless of focus
/// or location — and, because a pose belongs to its author, add the poser to the cast if they are not
/// already in it.</para>
///
/// <para>The room emit is the part that stays conditional: recording from anywhere is the point, but
/// speaking into a room you are not standing in would put words in front of people you are not with.
/// So the text reaches the room only when the poser is actually there.</para>
/// </summary>
[NotInParallel]
public class SceneWebComposeIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private static readonly string Tag = Guid.NewGuid().ToString("N")[..8];

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

	/// <summary>
	/// What each recipient has been told, bucketed by dbref.
	///
	/// <para>Not <c>ReceivedCalls()</c> on the substitute: that is a session singleton, so a global
	/// count is no window onto one test, and filtering it by <c>Notify</c> alone reads a refusal sent
	/// via <c>NotifyLocalized</c> as silence — the direction that makes a negative assertion pass when
	/// it should fail. The recorder is fed by every overload and keys by recipient.</para>
	/// </summary>
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	/// <summary>The player each bound handle belongs to, so a command can be windowed on its actor.</summary>
	private readonly ConcurrentDictionary<long, DBRef> _actors = new();

	/// <summary>Everything <paramref name="who"/> was told after the first <paramref name="fromCount"/>.</summary>
	private IReadOnlyList<string> HeardBy(DBRef who, int fromCount) =>
		[.. Notifications.For(who).Skip(fromCount)];

	private async Task<IReadOnlyList<string>> RunAs(long handle, string command)
	{
		var actor = _actors[handle];
		var before = Notifications.CountFor(actor);
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
		return HeardBy(actor, before);
	}

	/// <summary>
	/// The room this test's players stand in. DefaultHome holds every player the session made, and each scene
	/// change in a room refreshes it for everyone connected there, on the queue every test waits behind.
	/// </summary>
	private DBRef? _room;

	private async Task<DBRef> RoomAsync() => _room ??= DBRef.Parse(
		(await God1($"@dig {TestIsolationHelpers.GenerateUniqueName("SceneRoom")}")).Message.ToPlainText().Trim());

	private async Task<(string Dbref, long Handle)> CreatePlayerAsync(string name)
	{
		await TestIsolationHelpers.CreateNamedTestPlayerAsync(WebAppFactoryArg.Services,
			WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>(), name, await RoomAsync());
		var dbref = (await God1($"think [pmatch({name})]")).Message.ToPlainText()?.Trim() ?? string.Empty;
		if (!DBRef.TryParse(dbref, out var parsed) || parsed is null)
			throw new InvalidOperationException($"Failed to create player {name}; pmatch returned '{dbref}'.");

		await God1($"@role/assign {dbref}=approved");
		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, parsed.Value);
		_actors[handle] = parsed.Value;
		return (dbref, handle);
	}

	/// <summary>
	/// Puts the Scene Logger in the master room, where its <c>$</c>-commands match for everyone.
	///
	/// <para>The Logger's location is ambient global state, and several scene suites teleport it into
	/// a room of their own to test co-located behaviour. A <c>$</c>-command only matches for objects
	/// in the caller's room or the master room, so a suite that leaves it in a dug room takes
	/// <c>+scene/*</c> away from every later test — silently, because an unmatched <c>$</c>-command on
	/// an absent object produces no output at all: no match, no "Huh?", nothing to read. These tests
	/// therefore assert nothing about where it starts; they put it where it belongs first.</para>
	/// </summary>
	private async Task PutLoggerInMasterRoomAsync()
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var objects = await registry.GetPackageObjectsAsync("scene");
		var logger = DBRef.Parse(objects.Single(o => o.Ref == "logger").Objid).ToString();
		await God1($"@teleport {logger}=#2");
	}

	/// <summary>Last recorded pose's field on a scene.</summary>
	private async Task<string> LastPoseAsync(string sceneId, string field) =>
		await Eval($"scenepose({sceneId},[last(sceneposes({sceneId}))],{field})");

	[Test]
	public async Task WebCompose_RecordsIntoTheNamedScene_FromOutsideItsRoom_AndDoesNotEmitThere()
	{
		await PutLoggerInMasterRoomAsync();

		var (witness, witnessHandle) = await CreatePlayerAsync($"Brin{Tag}");
		var (remote, remoteHandle) = await CreatePlayerAsync($"Aster{Tag}");

		// A room with the scene in it; the witness stands there, the remote poser does not.
		var yard = (await God1($"@dig Well Yard {Tag}")).Message.ToPlainText()?.Trim() ?? string.Empty;
		var yardRef = yard.Split(' ').First(t => t.StartsWith('#'));
		await God1($"@tel {witness}={yardRef}");

		await RunAs(witnessHandle, $"+scene/create Well Yard Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(witness)})");
		await Assert.That(sceneId).DoesNotStartWith("#-1");

		// Public, because that is the scene a reader can reach the compose box for. A private scene
		// stays unreadable to a non-member, and the verb's existence guard goes through the same
		// visibility rule — so this is also what stops it writing into scenes it may not see.
		await RunAs(witnessHandle, "+scene/public");

		var posesBefore = await Eval($"words(sceneposes({sceneId}))");

		// Windowed on the witness, because the witness is who the claim is about: the assertion below
		// says nobody standing in the yard heard this, and the only way to say that is to read what the
		// person standing in the yard was told.
		// The recorder keys on the full objid pmatch() answered; a bare #N reads as someone who was never
		// told anything, and the negative assertion below would then pass whatever was emitted.
		var witnessRef = DBRef.Parse(witness);
		var witnessHeardBefore = Notifications.CountFor(witnessRef);
		await Assert.That(witnessHeardBefore).IsGreaterThan(0)
			.Because("the witness created the scene and was told so; hearing nothing means the window is on nobody");

		await RunAs(remoteHandle, $"+scene/emit {sceneId}=A raven settles on the well.");

		// Recorded, verbatim — an emit carries no name prefix, which is why it is the portal's default.
		await Assert.That(await Eval($"words(sceneposes({sceneId}))"))
			.IsEqualTo((int.Parse(posesBefore) + 1).ToString());
		await Assert.That(await LastPoseAsync(sceneId, "content")).IsEqualTo("A raven settles on the well.");
		await Assert.That(Num(await LastPoseAsync(sceneId, "author"))).IsEqualTo(Num(remote));

		// ...and the poser, who was never focused and never joined, is now in the cast.
		await Assert.That(await Eval($"scenemember({sceneId},{Num(remote)},role)")).IsNotEmpty();

		// But nothing was said in a room the poser is not standing in.
		await Assert.That(HeardBy(witnessRef, witnessHeardBefore)
				.Any(m => m.Contains("A raven settles on the well.", StringComparison.Ordinal)))
			.IsFalse()
			.Because("posing into a scene from elsewhere must not put words in front of the people in its room");
	}

	[Test]
	public async Task WebCompose_EmitsToTheRoom_WhenThePoserIsStandingInIt()
	{
		await PutLoggerInMasterRoomAsync();

		var (owner, ownerHandle) = await CreatePlayerAsync($"Cass{Tag}");
		var yard = (await God1($"@dig Cass Yard {Tag}")).Message.ToPlainText()?.Trim() ?? string.Empty;
		var yardRef = yard.Split(' ').First(t => t.StartsWith('#'));
		await God1($"@tel {owner}={yardRef}");

		await RunAs(ownerHandle, $"+scene/create Cass Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(owner)})");

		var heard = await RunAs(ownerHandle, $"+scene/emit {sceneId}=The lantern gutters.");

		await Assert.That(await LastPoseAsync(sceneId, "content")).IsEqualTo("The lantern gutters.");
		await Assert.That(heard.Any(m => m.Contains("The lantern gutters.", StringComparison.Ordinal)))
			.IsTrue()
			.Because("a poser standing in the scene's room should be heard there, as a typed emit would be");
	}

	/// <summary>
	/// The mode selector's other options render exactly as the built-in commands do, so a pose composed
	/// on the web and one typed in a terminal are indistinguishable in the archive.
	/// </summary>
	[Test]
	public async Task WebCompose_PoseAndSay_RenderWithTheSpeakersName()
	{
		await PutLoggerInMasterRoomAsync();

		var (player, handle) = await CreatePlayerAsync($"Dree{Tag}");
		var name = await Eval($"name({Num(player)})");
		await RunAs(handle, $"+scene/create Dree Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(player)})");

		await RunAs(handle, $"+scene/pose {sceneId}=leans on the rail.");
		await Assert.That(await LastPoseAsync(sceneId, "content")).IsEqualTo($"{name} leans on the rail.");

		await RunAs(handle, $"+scene/say {sceneId}=Evening.");
		await Assert.That(await LastPoseAsync(sceneId, "content")).IsEqualTo($"{name} says, \"Evening.\"");

		await RunAs(handle, $"+scene/semipose {sceneId}='s hand tightens.");
		await Assert.That(await LastPoseAsync(sceneId, "content")).IsEqualTo($"{name}'s hand tightens.");
	}

	/// <summary>
	/// A multi-line pose survives the round trip as real line breaks.
	///
	/// <para>This is the case that nearly shipped broken. <c>%r</c> is expanded before the
	/// <c>$</c>-command pattern is matched, so the pose reaches the matcher already containing a real
	/// newline — and a wildcard has to span it. It did not: <c>*</c> compiled to a <c>.</c> that
	/// excluded <c>\n</c>, so a two-line pose matched no <c>$</c>-command at all and came back "Huh?"
	/// in a terminal the web player never sees.</para>
	/// </summary>
	[Test]
	public async Task WebCompose_MultiLinePose_ArrivesWithRealLineBreaks()
	{
		await PutLoggerInMasterRoomAsync();

		var (player, handle) = await CreatePlayerAsync($"Fenn{Tag}");
		await RunAs(handle, $"+scene/create Fenn Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(player)})");

		var sent = await RunAs(handle, $"+scene/emit {sceneId}=first line%rsecond line");

		await Assert.That(await LastPoseAsync(sceneId, "content")).IsEqualTo("first line\nsecond line");
		await Assert.That(sent.Any(m => m.Contains("Huh?", StringComparison.Ordinal)))
			.IsFalse()
			.Because("a wildcard must span the newline %r expands to, or the verb never matches");
	}

	/// <summary>
	/// Posing from the web leaves an existing member's focus alone.
	///
	/// <para><c>@scene/member</c> clears the target's focus as a side effect, and these verbs add the
	/// poser to the cast — so writing the membership on every pose silently de-focused anyone who used
	/// the compose box. That is not cosmetic: nearly every other owner verb
	/// (<c>+scene/public</c>, <c>/private</c>, <c>/finish</c>, <c>/pitch</c>, <c>/edit</c>) acts on
	/// <c>scenefocus(%#)</c> and does nothing at all without one, and the capture hooks need the same
	/// focus to record a pose typed in a MU* client. One pose on the web and a player's terminal
	/// quietly stopped being recorded.</para>
	/// </summary>
	[Test]
	public async Task WebCompose_LeavesAnExistingMembersFocusAlone()
	{
		await PutLoggerInMasterRoomAsync();

		var (player, handle) = await CreatePlayerAsync($"Gale{Tag}");
		await RunAs(handle, $"+scene/create Gale Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(player)})");
		await Assert.That(sceneId).DoesNotStartWith("#-1")
			.Because("+scene/create focuses its owner; that focus is the precondition here");

		await RunAs(handle, $"+scene/emit {sceneId}=the lamp swings.");

		await Assert.That(await Eval($"scenefocus({Num(player)})")).IsEqualTo(sceneId);
	}

	/// <summary>
	/// Posing does not re-role someone already in the cast. The verb adds the poser as a participant
	/// so the cast cannot omit an author, but an owner who poses into their own scene is still its
	/// owner — and every ownership verb (<c>+scene/public</c>, <c>/finish</c>, <c>/pitch</c>) is gated
	/// on <c>FUN`OWNS</c>, so a silent demotion would lock them out of the scene they started.
	/// </summary>
	[Test]
	public async Task WebCompose_DoesNotDemoteAnOwnerWhoPoses()
	{
		await PutLoggerInMasterRoomAsync();

		var (owner, handle) = await CreatePlayerAsync($"Juno{Tag}");
		await RunAs(handle, $"+scene/create Juno Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(owner)})");
		await Assert.That(await Eval($"scenemember({sceneId},{Num(owner)},role)")).IsEqualTo("owner");

		await RunAs(handle, $"+scene/emit {sceneId}=the gate closes.");

		await Assert.That(await Eval($"scenemember({sceneId},{Num(owner)},role)")).IsEqualTo("owner");
	}

	/// <summary>
	/// Staff finish a scene that is not theirs by id once the game allows them <c>scene.close</c>, which
	/// the scene package defines. Until then <c>+scene/finish &lt;id&gt;</c> refuses them, and the owner can still finish
	/// their own.
	/// </summary>
	[Test]
	public async Task FinishById_TakesTheSceneClosePermission()
	{
		await PutLoggerInMasterRoomAsync();
		var (owner, ownerHandle) = await CreatePlayerAsync($"Kai{Tag}");
		var (helper, helperHandle) = await CreatePlayerAsync($"Lark{Tag}");
		await RunAs(ownerHandle, $"+scene/create Kai Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(owner)})");
		await Assert.That(sceneId).DoesNotStartWith("#-1");

		var refused = await RunAs(helperHandle, $"+scene/finish {sceneId}");
		await Assert.That(refused.Any(m => m.Contains("not yours to finish", StringComparison.Ordinal)))
			.IsTrue().Because($"saw instead: [{string.Join(" // ", refused)}]");

		// A private scene the helper is not in reads as missing until they hold the permission.
		await RunAs(ownerHandle, "+scene/private");
		var hidden = await RunAs(helperHandle, $"+scene/finish {sceneId}");
		await Assert.That(hidden.Any(m => m.Contains($"No such scene: {sceneId}", StringComparison.Ordinal)))
			.IsTrue().Because($"saw instead: [{string.Join(" // ", hidden)}]");
		await Assert.That(await Eval($"scene({sceneId},status)")).IsNotEqualTo("finished");

		// The scene package defines scene.close; the helper needs it allowed.
		try
		{
			await God1($"@permission/allow {helper}=scene.close");

			var finished = await RunAs(helperHandle, $"+scene/finish {sceneId}");
			await Assert.That(finished.Any(m => m.Contains($"Scene {sceneId} finished.", StringComparison.Ordinal)))
				.IsTrue().Because($"saw instead: [{string.Join(" // ", finished)}]");
			await Assert.That(await Eval($"scene({sceneId},status)")).IsEqualTo("finished");
		}
		finally
		{
			await God1($"@permission/clear {helper}=scene.close");
		}
	}

	/// <summary>A pose still puts a poser who was NOT in the cast into it.</summary>
	[Test]
	public async Task WebCompose_StillAddsANewPoserToTheCast()
	{
		await PutLoggerInMasterRoomAsync();

		var (owner, ownerHandle) = await CreatePlayerAsync($"Hale{Tag}");
		var (newcomer, guestHandle) = await CreatePlayerAsync($"Ivy{Tag}");
		await RunAs(ownerHandle, $"+scene/create Hale Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(owner)})");
		await RunAs(ownerHandle, "+scene/public");

		await Assert.That(await Eval($"scenemember({sceneId},{Num(newcomer)},role)")).StartsWith("#-1");

		await RunAs(guestHandle, $"+scene/emit {sceneId}=a door opens.");

		await Assert.That(await Eval($"scenemember({sceneId},{Num(newcomer)},role)")).DoesNotStartWith("#-1");
	}

	[Test]
	public async Task WebCompose_RefusesAnUnknownScene()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, handle) = await CreatePlayerAsync($"Erin{Tag}");

		var said = await RunAs(handle, $"+scene/emit no-such-scene-{Tag}=into the void");

		await Assert.That(said.Any(m => m.Contains("No such scene", StringComparison.OrdinalIgnoreCase)))
			.IsTrue()
			.Because($"saw instead: [{string.Join(" // ", said)}]");
	}

	/// <summary>
	/// Colour typed into a pose survives into storage.
	///
	/// <para>Storage takes the content as a serialised MString and derives the plain column from it;
	/// the portal renders <c>Pose.Markup</c>. Only the command boundary was flattening it, which left
	/// <c>markup</c> holding the same bare sentence as <c>content</c>.</para>
	/// </summary>
	[Test]
	public async Task WebCompose_KeepsTheColourAPoseWasWrittenWith()
	{
		await PutLoggerInMasterRoomAsync();
		var (_, handle) = await CreatePlayerAsync($"Isolde{Tag}");

		await RunAs(handle, $"+scene/create Isolde Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(_actors[handle].ToString())})");
		await Assert.That(sceneId).DoesNotStartWith("#-1");

		// Storage serialises plain text as well, so markup never equals content; the markup an uncoloured
		// pose of the same words gets is the baseline the colour has to differ from.
		await RunAs(handle, $"+scene/emit {sceneId}=A red ember.");
		var plainMarkup = await LastPoseAsync(sceneId, "markup");

		await RunAs(handle, $"+scene/emit {sceneId}=A [ansi(hr,red)] ember.");

		var content = await LastPoseAsync(sceneId, "content");
		var markup = await LastPoseAsync(sceneId, "markup");

		await Assert.That(content).IsEqualTo("A red ember.")
			.Because("the plain projection is the words without the colour");
		await Assert.That(markup).IsNotEqualTo(plainMarkup)
			.Because("the markup of an uncoloured pose means the colour never reached storage");
	}

	/// <summary>
	/// The Play page's Join sends <c>+scene/join</c> to a viewer focused elsewhere, the scene's own owner
	/// included. Joining focuses them and must not demote them: <c>@scene/member</c> sets the role outright.
	/// </summary>
	[Test]
	public async Task Join_FocusesAnOwner_WithoutDemotingThem()
	{
		await PutLoggerInMasterRoomAsync();

		var (owner, ownerHandle) = await CreatePlayerAsync($"Corin{Tag}");
		await RunAs(ownerHandle, $"+scene/create First Scene {Tag}");
		var first = await Eval($"scenefocus({Num(owner)})");
		await Assert.That(first).DoesNotStartWith("#-1");

		// A second scene takes the owner's focus away from the first.
		await RunAs(ownerHandle, $"+scene/create Second Scene {Tag}");
		await Assert.That(await Eval($"scenefocus({Num(owner)})")).IsNotEqualTo(first);

		await RunAs(ownerHandle, $"+scene/join {first}");

		await Assert.That(await Eval($"scenefocus({Num(owner)})")).IsEqualTo(first);
		await Assert.That(await Eval($"scenemember({first},{Num(owner)},role)")).IsEqualTo("owner");
	}
}
