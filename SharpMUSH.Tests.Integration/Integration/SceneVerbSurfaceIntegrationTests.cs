using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// What the scene verbs say: the guards that refuse, and the card that informs.
///
/// <para>The first half is about being unfocused.
///
/// <para>Ten of them read <c>scenefocus(%#)</c> and act on the result without asking whether it is a
/// scene. It is not, for anyone who has not joined one: it is the string <c>#-1 NOT FOUND</c>, which
/// is truthy and non-empty, so the guards passed and the work went ahead against a scene that does
/// not exist. A wizard — whom <c>FUN`OWNS</c> deliberately treats as owning every scene — got the
/// full success path, and was told <c>Pitch set for scene #-1 NOT FOUND.</c> while nothing was set.
/// Reporting a write that did not happen as one that did is the serious half; leaking the sentinel
/// into a sentence aimed at a player is the visible half.</para>
///
/// <para>The other arm was as bad in the opposite direction: a player who is not the owner failed an
/// <c>@assert</c> that carries no message, so <c>+scene/public</c> typed by the wrong person produced
/// no output whatsoever — indistinguishable from a command that does not exist.</para>
/// </summary>
[NotInParallel]
public class SceneVerbSurfaceIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private static readonly string Tag = Guid.NewGuid().ToString("N")[..8];
	private readonly ConcurrentDictionary<long, DBRef> _actors = new();

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

	private async Task<IReadOnlyList<string>> RunAs(long handle, string command)
	{
		var actor = _actors[handle];
		var before = Notifications.CountFor(actor);
		await Parser.CommandParse(handle, ConnectionService, MModule.single(command));
		return [.. Notifications.For(actor).Skip(before)];
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
		_actors[handle] = parsed.Value;
		return dbref;
	}

	/// <summary>The Logger's $-commands only match from the master room; other suites move it.</summary>
	private async Task PutLoggerInMasterRoomAsync()
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var objects = await registry.GetPackageObjectsAsync("scene");
		var logger = PackageInstallService.ParseObjid(objects.Single().Objid)!.Value.ToString();
		await God1($"@teleport {logger}=#2");
	}

	/// <summary>
	/// Every verb that acts on the focused scene, run by someone who has no focus. None of them may
	/// claim to have done anything, and none may show the player a raw <c>#-1</c>.
	/// </summary>
	[Test]
	[Arguments("+scene/pitch A quiet night.")]
	[Arguments("+scene/public")]
	[Arguments("+scene/private")]
	[Arguments("+scene/start")]
	[Arguments("+scene/pause")]
	[Arguments("+scene/finish")]
	[Arguments("+scene/leave")]
	[Arguments("+scene/deactivate")]
	public async Task UnfocusedVerb_SaysSoInsteadOfActing(string command)
	{
		await PutLoggerInMasterRoomAsync();
		const long handle = 9500;
		await CreatePlayerAsync($"Lune{Tag}", handle);

		var said = await RunAs(handle, command);

		await Assert.That(said).IsNotEmpty()
			.Because("a verb that cannot run must say why; silence is indistinguishable from no such command");
		await Assert.That(said.Any(m => m.Contains("#-1", StringComparison.Ordinal))).IsFalse()
			.Because($"'{command}' leaked the not-found sentinel into a player-facing message");
		await Assert.That(said.Any(m =>
				m.Contains("set for scene", StringComparison.OrdinalIgnoreCase)
				|| m.Contains("is now", StringComparison.OrdinalIgnoreCase)
				|| m.Contains("Left the scene", StringComparison.OrdinalIgnoreCase)
				|| m.Contains("Deactivated scene", StringComparison.OrdinalIgnoreCase)))
			.IsFalse()
			.Because($"'{command}' reported success while there was no scene to act on");
	}

	/// <summary>
	/// A wizard is deliberately allowed to administer anyone's scene — FUN`OWNS grants it — so the
	/// refusal above must be about the missing focus, not about permission. Focused, the same command
	/// works on a scene the wizard does not own.
	/// </summary>
	[Test]
	public async Task AWizardFocusedOnAnotherPlayersScene_CanStillAdministerIt()
	{
		await PutLoggerInMasterRoomAsync();
		const long ownerHandle = 9501;
		await CreatePlayerAsync($"Perr{Tag}", ownerHandle);

		await RunAs(ownerHandle, $"+scene/create Perr Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(_actors[ownerHandle].ToString())})");
		await Assert.That(sceneId).DoesNotStartWith("#-1");

		await God1($"+scene/join {sceneId}");
		await God1("+scene/pitch A wizard was here.");

		await Assert.That(await Eval($"scene({sceneId},summary)")).IsEqualTo("A wizard was here.");
	}

	/// <summary>
	/// Focus is not ownership. A member who did not create the scene is refused the owner-only verbs,
	/// and told so — this is the arm that used to produce no output at all.
	/// </summary>
	[Test]
	public async Task AFocusedNonOwner_IsRefusedInWords()
	{
		await PutLoggerInMasterRoomAsync();
		const long ownerHandle = 9502;
		const long guestHandle = 9503;
		await CreatePlayerAsync($"Quill{Tag}", ownerHandle);
		await CreatePlayerAsync($"Rook{Tag}", guestHandle);

		await RunAs(ownerHandle, $"+scene/create Quill Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(_actors[ownerHandle].ToString())})");
		await RunAs(ownerHandle, "+scene/public");
		await RunAs(guestHandle, $"+scene/join {sceneId}");

		var said = await RunAs(guestHandle, "+scene/pitch Not mine to set.");

		await Assert.That(said).IsNotEmpty().Because("a refusal the player cannot see is not a refusal");
		await Assert.That(await Eval($"scene({sceneId},summary)")).IsNotEqualTo("Not mine to set.");
	}


	/// <summary>
	/// <c>+scene/info &lt;id&gt;</c> is the scene's card, and <c>+scene &lt;id&gt;</c> is the same
	/// thing — the id on its own has always meant "tell me about this scene".
	///
	/// <para>It replaces <c>+scene/who</c>, which could only ever answer one question. A player
	/// asking about a scene wants where it is, what it is about and whether anyone may watch, not
	/// just a list of names; splitting that across two verbs meant the roster was the only part with
	/// a home of its own.</para>
	/// </summary>
	[Test]
	[Arguments("+scene/info")]
	[Arguments("+scene")]
	public async Task SceneInfo_ShowsTheCastWithRolesAndWhereItIs(string verb)
	{
		await PutLoggerInMasterRoomAsync();
		const long ownerHandle = 9520;
		const long castHandle = 9521;
		await CreatePlayerAsync($"Ines{Tag}", ownerHandle);
		await CreatePlayerAsync($"Joss{Tag}", castHandle);

		await RunAs(ownerHandle, $"+scene/create Ines Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(_actors[ownerHandle].ToString())})");
		await RunAs(ownerHandle, "+scene/pitch A lantern, a gate, a wait.");
		await RunAs(castHandle, $"+scene/join {sceneId}");

		var card = string.Join(" ", await RunAs(ownerHandle, $"{verb} {sceneId}"));

		await Assert.That(card).Contains("Owner").Because("the card names who owns the scene");
		await Assert.That(card).Contains("Participant").Because("and who else is in it");
		await Assert.That(card).Contains("Players").Because("the cast is a table with its own heading");
		await Assert.That(card).Contains("A lantern, a gate, a wait.").Because("the pitch is the description");
		await Assert.That(card).Contains("Where").Because("where it is happening is part of asking about it");
	}

	/// <summary>Whether anyone may watch is now worth stating: private is the exception.</summary>
	[Test]
	public async Task SceneInfo_SaysWhetherAnyoneMayWatch()
	{
		await PutLoggerInMasterRoomAsync();
		const long ownerHandle = 9522;
		await CreatePlayerAsync($"Kite{Tag}", ownerHandle);

		await RunAs(ownerHandle, $"+scene/create Kite Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(_actors[ownerHandle].ToString())})");

		var open = string.Join(" ", await RunAs(ownerHandle, $"+scene/info {sceneId}"));
		await Assert.That(open).Contains("Anyone").Because("a new scene is watchable and should say so");

		await RunAs(ownerHandle, "+scene/private");
		var shut = string.Join(" ", await RunAs(ownerHandle, $"+scene/info {sceneId}"));
		await Assert.That(shut).Contains("Members").Because("the card must reflect the exception once it is made");
	}

	/// <summary>The verb it replaces is gone rather than left as a second way to ask.</summary>
	[Test]
	public async Task SceneWho_IsNoLongerACommand()
	{
		await PutLoggerInMasterRoomAsync();
		const long handle = 9523;
		await CreatePlayerAsync($"Lark{Tag}", handle);
		await RunAs(handle, $"+scene/create Lark Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(_actors[handle].ToString())})");

		var said = string.Join(" ", await RunAs(handle, $"+scene/who {sceneId}"));

		await Assert.That(said).Contains("Huh?").Because("+scene/who was replaced by +scene/info");
	}

	/// <summary>
	/// One cast line, carrying who is in the scene, what they are to it, and the name they pose
	/// under when that differs from their own.
	///
	/// <para>The card briefly had two: a Cast of personas and a Members list of characters with
	/// roles. Two lines describing the same people is a puzzle for the reader, who has to work out
	/// which name on the first line is which person on the second.</para>
	/// </summary>
	[Test]
	public async Task SceneInfo_ShowsOneCastLine_CarryingPersonaAndRole()
	{
		await PutLoggerInMasterRoomAsync();
		const long ownerHandle = 9530;
		const long castHandle = 9531;
		var owner = await CreatePlayerAsync($"Mira{Tag}", ownerHandle);
		await CreatePlayerAsync($"Nolan{Tag}", castHandle);

		await RunAs(ownerHandle, $"+scene/create Mira Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(owner)})");
		await RunAs(castHandle, $"+scene/join {sceneId}");
		await RunAs(castHandle, "+scene/as The Cloaked Stranger");

		var card = string.Join(" ", await RunAs(ownerHandle, $"+scene/info {sceneId}"));

		await Assert.That(card).DoesNotContain("Cast")
			.Because("one table describes the players; a second line of personas said it again");
		await Assert.That(card).Contains($"Mira{Tag}")
			.Because("a member with no persona is listed under their own name");
		await Assert.That(card).Contains("The Cloaked Stranger")
			.Because("the name a member poses under belongs in their row, not on a line of its own");
		await Assert.That(card).Contains("Owner");
		await Assert.That(card).Contains("Participant");
	}
}
