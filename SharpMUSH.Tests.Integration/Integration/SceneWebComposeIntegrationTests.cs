using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
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
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private static readonly string Tag = Guid.NewGuid().ToString("N")[..8];

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText().Trim();

	private async Task<CallState> God1(string command) =>
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

	private static string Num(string dbref)
	{
		var s = dbref.Trim();
		var colon = s.IndexOf(':');
		return colon < 0 ? s : s[..colon];
	}

	private int NotificationCount() =>
		NotifyService.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

	private static string? MessageText(ICall call)
	{
		var args = call.GetArguments();
		return args.Length < 2 ? null : args[1]?.ToString();
	}

	private IReadOnlyList<string> MessagesSince(int fromCount) =>
		NotifyService.ReceivedCalls()
			.Where(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify))
			.Skip(fromCount)
			.Select(c => MessageText(c) ?? string.Empty)
			.ToList();

	private async Task<IReadOnlyList<string>> RunAs(long handle, string command)
	{
		var before = NotificationCount();
		await Parser.CommandParse(handle, ConnectionService, MModule.single(command));
		return MessagesSince(before);
	}

	private async Task<string> CreatePlayerAsync(string name, long handle)
	{
		await God1($"@pcreate {name}=pw-{Tag}-1");
		var dbref = (await God1($"think [pmatch({name})]")).Message?.ToPlainText()?.Trim() ?? string.Empty;
		if (!DBRef.TryParse(dbref, out var parsed) || parsed is null)
			throw new InvalidOperationException($"Failed to create player {name}; pmatch returned '{dbref}'.");

		await God1($"@set {dbref}=APPROVED");
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		await ConnectionService.Bind(handle, parsed.Value);
		return dbref;
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
		var logger = PackageInstallService.ParseObjid(objects.Single().Objid)!.Value.ToString();
		await God1($"@teleport {logger}=#2");
	}

	/// <summary>Last recorded pose's field on a scene.</summary>
	private async Task<string> LastPoseAsync(string sceneId, string field) =>
		await Eval($"scenepose({sceneId},[last(sceneposes({sceneId}))],{field})");

	[Test]
	public async Task WebCompose_RecordsIntoTheNamedScene_FromOutsideItsRoom_AndDoesNotEmitThere()
	{
		await PutLoggerInMasterRoomAsync();
		const long witnessHandle = 9401;
		const long remoteHandle = 9402;

		var witness = await CreatePlayerAsync($"Brin{Tag}", witnessHandle);
		var remote = await CreatePlayerAsync($"Aster{Tag}", remoteHandle);

		// A room with the scene in it; the witness stands there, the remote poser does not.
		var yard = (await God1($"@dig Well Yard {Tag}")).Message?.ToPlainText()?.Trim() ?? string.Empty;
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

		var heard = await RunAs(remoteHandle, $"+scene/emit {sceneId}=A raven settles on the well.");

		// Recorded, verbatim — an emit carries no name prefix, which is why it is the portal's default.
		await Assert.That(await Eval($"words(sceneposes({sceneId}))"))
			.IsEqualTo((int.Parse(posesBefore) + 1).ToString());
		await Assert.That(await LastPoseAsync(sceneId, "content")).IsEqualTo("A raven settles on the well.");
		await Assert.That(Num(await LastPoseAsync(sceneId, "author"))).IsEqualTo(Num(remote));

		// ...and the poser, who was never focused and never joined, is now in the cast.
		await Assert.That(await Eval($"scenemember({sceneId},{Num(remote)},role)")).IsNotEmpty();

		// But nothing was said in a room the poser is not standing in.
		await Assert.That(heard.Any(m => m.Contains("A raven settles on the well.", StringComparison.Ordinal)))
			.IsFalse()
			.Because("posing into a scene from elsewhere must not put words in front of the people in its room");
	}

	[Test]
	public async Task WebCompose_EmitsToTheRoom_WhenThePoserIsStandingInIt()
	{
		await PutLoggerInMasterRoomAsync();
		const long ownerHandle = 9411;

		var owner = await CreatePlayerAsync($"Cass{Tag}", ownerHandle);
		var yard = (await God1($"@dig Cass Yard {Tag}")).Message?.ToPlainText()?.Trim() ?? string.Empty;
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
		const long handle = 9421;

		var player = await CreatePlayerAsync($"Dree{Tag}", handle);
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
		const long handle = 9441;
		await PutLoggerInMasterRoomAsync();

		var player = await CreatePlayerAsync($"Fenn{Tag}", handle);
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
		const long handle = 9451;
		await PutLoggerInMasterRoomAsync();

		var player = await CreatePlayerAsync($"Gale{Tag}", handle);
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
		const long handle = 9471;
		await PutLoggerInMasterRoomAsync();

		var owner = await CreatePlayerAsync($"Juno{Tag}", handle);
		await RunAs(handle, $"+scene/create Juno Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(owner)})");
		await Assert.That(await Eval($"scenemember({sceneId},{Num(owner)},role)")).IsEqualTo("owner");

		await RunAs(handle, $"+scene/emit {sceneId}=the gate closes.");

		await Assert.That(await Eval($"scenemember({sceneId},{Num(owner)},role)")).IsEqualTo("owner");
	}

	/// <summary>A pose still puts a poser who was NOT in the cast into it.</summary>
	[Test]
	public async Task WebCompose_StillAddsANewPoserToTheCast()
	{
		const long ownerHandle = 9461;
		const long guestHandle = 9462;
		await PutLoggerInMasterRoomAsync();

		var owner = await CreatePlayerAsync($"Hale{Tag}", ownerHandle);
		var newcomer = await CreatePlayerAsync($"Ivy{Tag}", guestHandle);
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
		const long handle = 9431;
		await CreatePlayerAsync($"Erin{Tag}", handle);

		var said = await RunAs(handle, $"+scene/emit no-such-scene-{Tag}=into the void");

		await Assert.That(said.Any(m => m.Contains("No such scene", StringComparison.OrdinalIgnoreCase)))
			.IsTrue()
			.Because($"saw instead: [{string.Join(" // ", said)}]");
	}
}
