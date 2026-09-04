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
/// What the scene verbs say when the player is not focused on a scene.
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
public class SceneFocusGuardIntegrationTests
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
	/// The cast roster renders each member as "Name (role)".
	///
	/// <para>It rendered <c>Wren (owner Thorne (participant)</c> — every closing bracket missing. The
	/// body was <c>[name(##)] ([scenemember(...)])</c>, and in SharpMUSH a bare <c>)</c> closes the
	/// enclosing function by design rather than nesting, so that paren terminated the <c>iter()</c>
	/// itself and took the rest of the line with it. It has to be escaped.</para>
	/// </summary>
	[Test]
	public async Task SceneWho_ClosesTheParenthesisAroundEachRole()
	{
		await PutLoggerInMasterRoomAsync();
		const long ownerHandle = 9504;
		const long castHandle = 9505;
		await CreatePlayerAsync($"Sable{Tag}", ownerHandle);
		await CreatePlayerAsync($"Tam{Tag}", castHandle);

		await RunAs(ownerHandle, $"+scene/create Sable Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(_actors[ownerHandle].ToString())})");
		await RunAs(ownerHandle, "+scene/public");
		await RunAs(castHandle, $"+scene/join {sceneId}");

		var said = await RunAs(ownerHandle, $"+scene/who {sceneId}");
		var roster = string.Join(" ", said);

		// Two members, deliberately. The stray paren closes iter() and then lands at the END of the
		// whole rendered list, so a one-member roster reads "Sable (owner)" and looks perfect — the
		// defect only shows once there is a second entry to strand the first one's bracket.
		await Assert.That(roster).Contains("(owner)")
			.Because("the role belongs in closed parentheses; a bare ) ended the iter() instead");
		await Assert.That(roster).Contains("(participant)")
			.Because("every entry needs its own closing paren, not one shared at the end of the line");
	}
}
