using System.Text;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using OneOf;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// The approval boundary, end to end: the engine's <c>APPROVED</c> flag and <c>isapproved()</c> predicate,
/// the bundled <c>scene</c> package's <c>+scene</c> verbs that consume them, and the wizard lockdown on the
/// primitive <c>@scene</c> surface underneath.
///
/// <para>The rule under test: a CHARACTER is required for any scene participation, viewing is open to every
/// character approved or not, and ASSOCIATION — joining, owning, administering, posing into a scene — needs
/// approval. An unapproved character has no scenes and must not be able to acquire one by any route the
/// package exposes. Guests are never approved, whatever else is set on them.</para>
///
/// <para>The negative assertions are the point. Each refused verb is checked twice: the player is told no,
/// AND the membership list is re-read to prove nothing was written anyway.</para>
/// </summary>
[NotInParallel]
public class SceneApprovalIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private static readonly string Tag = Guid.NewGuid().ToString("N")[..8];

	/// <summary>The refusal the package's INC`REQUIRE`APPROVED guard emits.</summary>
	private const string NotApproved = "You are not approved to take part in scenes.";

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

	private async Task<string> EvalNum(string expression) => Num(await Eval(expression));

	private static bool IsNotification(ICall call) =>
		call.GetMethodInfo().Name is nameof(INotifyService.Notify) or nameof(INotifyService.NotifyLocalized);

	private int NotificationCount() => NotifyService.ReceivedCalls().Count(IsNotification);

	/// <summary>
	/// One line of what a command said. Refusals travel by two different routes — a literal string through
	/// <c>Notify</c>, or a resource KEY through <c>NotifyLocalized</c> (the engine's CommandLock refusal is
	/// localized) — and a test that watches only one of them reads a real refusal as silence.
	/// </summary>
	private static string? ExtractMessageText(ICall call)
	{
		var args = call.GetArguments();
		switch (call.GetMethodInfo().Name)
		{
			case nameof(INotifyService.Notify) when args.Length >= 2:
				return args[1] switch
				{
					OneOf<MString, string> oneOf => oneOf.Match(m => m.ToPlainText(), s => s),
					string s => s,
					MString m => m.ToPlainText(),
					_ => null
				};
			case nameof(INotifyService.NotifyLocalized) when args.Length >= 2:
				return args[1] as string;
			default:
				return null;
		}
	}

	/// <summary>Runs a command as a connection handle and returns everything it said, joined.</summary>
	private async Task<string> RunAs(long handle, string command)
	{
		var before = NotificationCount();
		await Parser.CommandParse(handle, ConnectionService, MModule.single(command));
		var all = NotifyService.ReceivedCalls().Where(IsNotification).ToList();
		return string.Join("\n", all.Skip(before).Select(ExtractMessageText).OfType<string>());
	}

	/// <summary>Evaluates an expression AS a player, by making them <c>think</c> it — real mortal softcode.</summary>
	private async Task<string> EvalAs(long handle, string expression) =>
		(await RunAs(handle, $"think {expression}")).Trim();

	private async Task<string> CreatePlayerAsync(string name, long handle)
	{
		await God1($"@pcreate {name}=pw_{Tag}_123");
		var dbref = (await God1($"think [pmatch({name})]")).Message?.ToPlainText()?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(dbref) || dbref.StartsWith("#-") || !DBRef.TryParse(dbref, out var parsed))
			throw new InvalidOperationException($"Failed to create player {name}; pmatch returned '{dbref}'.");

		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		await ConnectionService.Bind(handle, parsed!.Value);
		return dbref;
	}

	/// <summary>
	/// Grants the Guest power. <c>@power</c> only administers power DEFINITIONS — it has no branch that sets
	/// one on an object — and the <c>power()</c> side-effect function depends on a config switch, so the test
	/// goes straight at the service the function would have called.
	/// </summary>
	private async Task GrantGuestPowerAsync(string playerDbref)
	{
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var manipulate = WebAppFactoryArg.Services.GetRequiredService<IManipulateSharpObjectService>();
		DBRef.TryParse(playerDbref, out var parsed);
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Known;
		var target = (await mediator.Send(new GetObjectNodeQuery(parsed!.Value))).Known;
		await manipulate.SetPower(god, target, "Guest", false);
	}

	private async Task<IReadOnlyList<string>> MembersAsync(string sceneId) =>
		(await Eval($"scenemembers({sceneId})"))
			.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Num).ToList();

	[Test]
	public async Task ApprovalBoundary_FullMatrix()
	{
		static void Log(string m) => Console.WriteLine(m);

		await God1("@set #1=WIZARD");

		// ---- 1. The predicate itself ------------------------------------------------------------
		var approved = await CreatePlayerAsync($"App_{Tag}", 51L);
		var unapproved = await CreatePlayerAsync($"Unapp_{Tag}", 52L);
		var guest = await CreatePlayerAsync($"Guesty_{Tag}", 53L);

		await God1($"@set {approved}=APPROVED");
		await GrantGuestPowerAsync(guest);
		// Deliberately ALSO flag the guest APPROVED: the guest term must win regardless.
		await God1($"@set {guest}=APPROVED");
		await Assert.That(await Eval($"haspower({guest}, Guest)")).IsEqualTo("1")
			.Because("the rest of this beat is meaningless if the Guest power did not take");

		await Assert.That(await Eval($"isapproved({approved})")).IsEqualTo("1")
			.Because("the APPROVED flag makes an ordinary character approved");
		await Assert.That(await Eval($"isapproved({unapproved})")).IsEqualTo("0")
			.Because("a character with neither staff bit nor the flag is not approved");
		await Assert.That(await Eval("isapproved(#1)")).IsEqualTo("1")
			.Because("staff are implicitly approved — 'royalty or above, or approved'");
		await Assert.That(await Eval($"isapproved({guest})")).IsEqualTo("0")
			.Because("a guest is never approved even when the flag is set on it");
		Log($"[PREDICATE] approved={approved} unapproved={unapproved} guest={guest} — all four answers correct");

		// ---- 2. Stage: a room, the three players, and the Scene Logger co-located ----------------
		var digOut = (await God1($"@dig ApprovalStage_{Tag}")).Message!.ToPlainText().Trim();
		var roomDbref = Num(digOut);
		foreach (var who in new[] { approved, unapproved, guest })
			await God1($"@tel {who}={digOut}");

		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = PackageInstallService.ParseObjid(packageObjects.Single().Objid)!.Value.ToString();
		// The Logger is game-wide, and a $-command only matches for objects in the caller's room — so
		// moving it here takes +scene/* away from every OTHER test's players for the rest of the run.
		// It was left in this room, and later suites saw their +scene commands silently do nothing:
		// no match, no "Huh?", no error, because an unmatched $-command on an absent object produces
		// no output at all. Note where it came from and put it back at the end.
		var loggerHome = await Eval($"loc({loggerDbref})");
		await God1($"@tel {loggerDbref}={digOut}");
		await Assert.That(await EvalNum($"loc({loggerDbref})")).IsEqualTo(roomDbref);

		// ---- 3. An APPROVED character may own a scene -------------------------------------------
		var createOut = await RunAs(51L, $"+scene/create Approval Test {Tag}");
		Log($"[CREATE approved] {createOut}");
		var sceneId = await Eval($"get({approved}/MY.SID)");
		await Assert.That(sceneId).IsNotEmpty().Because("an approved character may create and own a scene");
		await Assert.That(sceneId).DoesNotStartWith("#-1");
		await God1($"@scene/set {sceneId}/public=1");
		await Assert.That(await EvalNum($"scene({sceneId}, owner)")).IsEqualTo(Num(approved));

		// ---- 4. Every ASSOCIATING route is closed to an unapproved character ---------------------
		// Each of these is a distinct way the package could hand out a membership edge.
		var associatingVerbs = new (string Command, string What)[]
		{
			($"+scene/join {sceneId}", "join"),
			($"+scene/tag {sceneId}", "RSVP"),
			($"+scene/activate {sceneId}", "re-activate"),
			("+scene/as The Interloper", "set a persona"),
			($"+scene/create Sneaky {Tag}", "create/own"),
			($"+scene/schedule Sneaky Later {Tag}=1893456000", "schedule/own")
		};

		foreach (var (command, what) in associatingVerbs)
		{
			var output = await RunAs(52L, command);
			Log($"[REFUSED unapproved] {command} -> {output.Replace("\n", " | ")}");
			await Assert.That(output).Contains(NotApproved)
				.Because($"an unapproved character must be refused when trying to {what}");
			await Assert.That(await MembersAsync(sceneId)).DoesNotContain(Num(unapproved))
				.Because($"the refusal of {what} must also mean nothing was written");
		}

		await Assert.That(await MembersAsync(sceneId)).DoesNotContain(Num(unapproved))
			.Because("no refused verb may leave a membership edge behind");
		await Assert.That(await Eval($"scenefocus({unapproved})")).StartsWith("#-1")
			.Because("an unapproved character is focused on nothing");
		await Assert.That(await EvalAs(52L, "scenelist(mine)")).IsEmpty()
			.Because("an unapproved character has no scenes");

		// ---- 5. A GUEST is refused too, flag or no flag ------------------------------------------
		var guestJoin = await RunAs(53L, $"+scene/join {sceneId}");
		Log($"[REFUSED guest] +scene/join -> {guestJoin.Replace("\n", " | ")}");
		await Assert.That(guestJoin).Contains(NotApproved)
			.Because("guests never participate in scenes");
		await Assert.That(await MembersAsync(sceneId)).DoesNotContain(Num(guest));

		// ---- 6. Viewing stays OPEN to an unapproved character ------------------------------------
		var browse = await RunAs(52L, "+scene");
		Log($"[VIEW unapproved] +scene -> {browse.Replace("\n", " | ")[..Math.Min(160, browse.Replace("\n", " | ").Length)]}");
		await Assert.That(browse).DoesNotContain(NotApproved)
			.Because("browsing the scene list needs no approval");
		await Assert.That(browse).Contains("Scenes:");

		var upcoming = await RunAs(52L, "+scene/upcoming");
		await Assert.That(upcoming).DoesNotContain(NotApproved)
			.Because("viewing the schedule needs no approval");
		await Assert.That(upcoming).Contains("Scheduled Scenes");

		var info = await RunAs(52L, $"+scene {sceneId}");
		Log($"[VIEW unapproved] +scene {sceneId} -> {info.Replace("\n", " | ")[..Math.Min(160, info.Replace("\n", " | ").Length)]}");
		await Assert.That(info).DoesNotContain(NotApproved)
			.Because("reading scene information needs no approval");
		await Assert.That(info).Contains($"Scene {sceneId}");

		// ---- 7. Approval is checked at POSE time, so revoking it stops capture immediately --------
		var posesBefore = await Eval($"scene({sceneId}, posecount)");
		await RunAs(51L, "pose tests that capture works while approved.");
		var posesWhileApproved = await Eval($"scene({sceneId}, posecount)");
		await Assert.That(int.Parse(posesWhileApproved)).IsGreaterThan(int.Parse(posesBefore))
			.Because("an approved, focused character's pose is captured");

		await God1($"@set {approved}=!APPROVED");
		await Assert.That(await Eval($"isapproved({approved})")).IsEqualTo("0");
		await RunAs(51L, "pose tests that capture stops the moment approval is revoked.");
		var posesAfterRevoke = await Eval($"scene({sceneId}, posecount)");
		Log($"[REVOKE] posecount before={posesBefore} approved={posesWhileApproved} after-revoke={posesAfterRevoke}");
		await Assert.That(posesAfterRevoke).IsEqualTo(posesWhileApproved)
			.Because("membership and focus survive revocation, so capture itself has to re-check approval");

		// Hand the Logger back where it was, so the suites that run after this one still have
		// +scene/* at all.
		await God1($"@tel {loggerDbref}={loggerHome}");
	}

	[Test]
	public async Task AtScene_Command_IsWizardOnly_ForEverySwitch()
	{
		await God1("@set #1=WIZARD");
		var mortal = await CreatePlayerAsync($"Mortal_{Tag}", 61L);
		await Assert.That(mortal).IsNotEmpty();

		// The 18 action switches plus the bare (display) form. All 19 go through one wizard gate at the
		// top of SceneCommandModule.Scene, so this pins that none of them grew a path around it.
		string[] switches =
		[
			"list", "get", "create", "set", "addpose", "setpose", "editpose", "undo", "redo",
			"move", "delete", "member", "unmember", "focus", "showas", "plot", "link", "unlink"
		];

		foreach (var name in switches)
		{
			var result = await Parser.CommandParse(61L, ConnectionService, MModule.single($"@scene/{name} something=else"));
			await Assert.That(result.Message!.ToPlainText()).IsEqualTo("#-1 PERMISSION DENIED")
				.Because($"@scene/{name} is wizard-only — players drive the system through +scene");
		}

		var bareResult = await Parser.CommandParse(61L, ConnectionService, MModule.single("@scene something"));
		await Assert.That(bareResult.Message!.ToPlainText()).IsEqualTo("#-1 PERMISSION DENIED")
			.Because("the bare display form is gated by the same check");

		// And the player is told, not silently ignored. The FLAG^WIZARD CommandLock refuses first, so the
		// message is the engine's localized PermissionDenied rather than the plugin's own SCENE: prefix —
		// the plugin's explicit IsWizard() gate is the second line of defence behind it, not the first.
		var refusalOutput = await RunAs(61L, "@scene/list");
		await Assert.That(refusalOutput).Contains(nameof(ErrorMessages.Notifications.PermissionDenied))
			.Because("a refused command has to say so");
	}

	[Test]
	public async Task SceneWriteFunctions_AreUnreachableFromMortalSoftcode()
	{
		await God1("@set #1=WIZARD");
		var mortal = await CreatePlayerAsync($"FnMortal_{Tag}", 62L);
		var sceneId = await Eval($"scenecreate(,#1,Function Surface {Tag})");
		await Assert.That(sceneId).DoesNotStartWith("#-1");

		// Every write function carries FunctionFlags.WizardOnly, which the parser checks against the
		// EXECUTOR. A player evaluating one in their own softcode is the executor, so all of these
		// refuse — the command lock on @scene would be worthless if the functions did not.
		var writes = new[]
		{
			$"scenecreate(,{mortal},Mortal Scene {Tag})",
			$"sceneset({sceneId},status,active)",
			$"sceneaddmember({sceneId},{mortal},participant)",
			$"sceneunmember({sceneId},{mortal})",
			$"scenesetfocus({mortal},{sceneId})",
			$"sceneshowas({sceneId},{mortal},Sneaky)",
			$"sceneaddpose({sceneId},{mortal},,{mortal},pose,,intruding)",
			$"sceneplot(create,Mortal Plot {Tag}|desc|{mortal})"
		};

		foreach (var expression in writes)
		{
			var result = await EvalAs(62L, expression);
			await Assert.That(result).StartsWith("#-1")
				.Because($"{expression} is a wizard-only side-effect function and a player is not a wizard");
		}

		await Assert.That(await MembersAsync(sceneId)).DoesNotContain(Num(mortal))
			.Because("no refused write function may have taken effect anyway");
	}

	[Test]
	public async Task SceneList_DoesNotLeakPrivateSceneIdsToNonMembers()
	{
		await God1("@set #1=WIZARD");
		await CreatePlayerAsync($"Lister_{Tag}", 63L);

		// Explicitly private: scenes are created watchable, so this is the case that must be asked for.
		var privateScene = await Eval($"scenecreate(,#1,Private {Tag})");
		await Eval($"sceneset({privateScene},public,0)");
		await God1($"@scene/set {privateScene}/status=active");
		var publicScene = await Eval($"scenecreate(,#1,Public {Tag})");
		await God1($"@scene/set {publicScene}/status=active");
		await God1($"@scene/set {publicScene}/public=1");

		var listed = await EvalAs(63L, "scenelist(active)");

		await Assert.That(listed).Contains(publicScene)
			.Because("a public scene is listable by anyone");
		await Assert.That(listed).DoesNotContain(privateScene)
			.Because("the storage filters carry no visibility clause, so the function layer must");
		await Assert.That(await EvalAs(63L, $"scene({privateScene}, status)")).StartsWith("#-1")
			.Because("and reading its fields stays refused, as it always was");

	}

	[Test]
	public async Task ApprovedFlag_CannotBeSetOrUnsetByAnUnprivilegedPlayer()
	{
		await God1("@set #1=WIZARD");
		var setter = await CreatePlayerAsync($"Setter_{Tag}", 64L);
		var target = await CreatePlayerAsync($"Target_{Tag}", 65L);

		await RunAs(64L, $"@set {target}=APPROVED");
		await Assert.That(await Eval($"isapproved({target})")).IsEqualTo("0")
			.Because("APPROVED is royalty-settable; an ordinary player cannot approve anyone");

		await God1($"@set {target}=APPROVED");
		await Assert.That(await Eval($"isapproved({target})")).IsEqualTo("1");

		await RunAs(64L, $"@set {target}=!APPROVED");
		await Assert.That(await Eval($"isapproved({target})")).IsEqualTo("1")
			.Because("nor can an ordinary player revoke someone else's approval");

		await Assert.That(await Eval($"isapproved({setter})")).IsEqualTo("0");
	}
}
