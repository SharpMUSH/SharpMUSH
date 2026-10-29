using Mediator;
using Microsoft.Extensions.DependencyInjection;
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
/// The scene permission boundary, end to end: the bundled <c>scene</c> package's <c>scene.view</c>,
/// <c>scene.join</c>, <c>scene.pose</c> and <c>scene.create</c>, the <c>+scene</c> verbs that ask for them, the
/// grants that give them to the <c>approved</c> role by default, and the wizard lockdown on the primitive
/// <c>@scene</c> surface underneath.
///
/// <para>The rule under test: a CHARACTER is required for any scene participation, viewing is open to everyone
/// by default, and ASSOCIATION — joining, owning, administering, posing into a scene — needs the permission each
/// verb names. A character without them has no scenes and must not be able to acquire one by any route the
/// package exposes. A game moves the permissions with the role tools, and the verbs follow.</para>
///
/// <para>The negative assertions are the point. Each refused verb is checked twice: the player is told no,
/// AND the membership list is re-read to prove nothing was written anyway.</para>
/// </summary>
[NotInParallel]
public class ScenePermissionIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private static readonly string Tag = Guid.NewGuid().ToString("N")[..8];

	/// <summary>The refusal the package's FUN`DENY gives for a missing permission.</summary>
	private static string Needs(string permission) => $"Permission denied. You need the {permission} permission.";

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private async Task<CallState> God1(string command) =>
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private static string Num(string dbref)
	{
		var s = dbref.Trim();
		var colon = s.IndexOf(':');
		return colon < 0 ? s : s[..colon];
	}

	private async Task<string> EvalNum(string expression) => Num(await Eval(expression));

	/// <summary>Runs a command and returns only what its character was told, after queued work completes.</summary>
	private async Task<string> RunAs(long handle, string command)
	{
		var recipient = ConnectionService.Get(handle)?.Ref
			?? throw new InvalidOperationException($"Handle {handle} is not bound to a character.");
		await WebAppFactoryArg.QueueBarrierAsync();
		var before = Notifications.CountFor(recipient);
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(recipient).Skip(before));
	}

	/// <summary>Evaluates an expression AS a player, by making them <c>think</c> it — real mortal softcode.</summary>
	private async Task<string> EvalAs(long handle, string expression) =>
		(await RunAs(handle, $"think {expression}")).Trim();

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
		if (string.IsNullOrEmpty(dbref) || dbref.StartsWith("#-") || !DBRef.TryParse(dbref, out var parsed))
			throw new InvalidOperationException($"Failed to create player {name}; pmatch returned '{dbref}'.");

		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, parsed!.Value);
		return (dbref, handle);
	}

	/// <summary>
	/// Grants the Guest power. <c>@power</c> only administers power DEFINITIONS — it has no branch that sets
	/// one on an object — and the <c>power()</c> side-effect function depends on a config switch, so the test
	/// goes straight at the service the function would have called.
	/// </summary>
	private async Task GrantGuestPowerAsync(string playerDbref)
	{
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var powers = WebAppFactoryArg.Services.GetRequiredService<IFlagAndPowerService>();
		DBRef.TryParse(playerDbref, out var parsed);
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		var target = (await mediator.Send(new GetObjectNodeQuery(parsed!.Value))).Expect<AnySharpObject>();
		await powers.SetPower(god, target, "Guest", false);
	}

	private async Task<IReadOnlyList<string>> MembersAsync(string sceneId) =>
		(await Eval($"scenemembers({sceneId})"))
			.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Num).ToList();

	[Test]
	public async Task CommandCapture_ExcludesOutputAddressedToAnotherCharacter()
	{
		var (_, callerHandle) = await CreatePlayerAsync($"CaptureCaller_{Tag}");
		var (recipient, _) = await CreatePlayerAsync($"CaptureRecipient_{Tag}");
		var recipientRef = DBRef.Parse(recipient);
		var otherRecipientOutput = $"OtherRecipient_{Tag}";
		var before = WebAppFactoryArg.Notifications.CountFor(recipientRef);

		var captured = await RunAs(callerHandle, $"@pemit/silent {recipient}={otherRecipientOutput}");

		await Assert.That(WebAppFactoryArg.Notifications.For(recipientRef).Skip(before))
			.Contains(otherRecipientOutput);
		await Assert.That(captured).IsEmpty()
			.Because("output delivered to another character does not answer the caller's command");
	}

	[Test]
	public async Task ScenePermissionBoundary_FullMatrix()
	{
		static void Log(string m) => TestDiagnostics.WriteLine(m);

		await God1("@set #1=WIZARD");

		// ---- 1. The default grants ----------------------------------------------------------------
		var (approved, approvedHandle) = await CreatePlayerAsync($"App_{Tag}");
		var (unapproved, unapprovedHandle) = await CreatePlayerAsync($"Unapp_{Tag}");
		var (guest, guestHandle) = await CreatePlayerAsync($"Guesty_{Tag}");
		var (reader, readerHandle) = await CreatePlayerAsync($"Reader_{Tag}");

		await God1($"@role/assign {approved}=approved");
		await GrantGuestPowerAsync(guest);
		await Assert.That(await Eval($"haspower({guest}, Guest)")).IsEqualTo("1")
			.Because("the rest of this beat is meaningless if the Guest power did not take");

		foreach (var permission in new[] { "scene.join", "scene.pose", "scene.create" })
		{
			await Assert.That(await Eval($"permission({approved},{permission})")).IsEqualTo("1")
				.Because($"the package grants {permission} to the approved role");
			await Assert.That(await Eval($"permission(#1,{permission})")).IsEqualTo("1")
				.Because($"and to wizards");
			await Assert.That(await Eval($"permission({unapproved},{permission})")).IsEqualTo("0")
				.Because($"a character without the approved role does not hold {permission}");
			await Assert.That(await Eval($"permission({guest},{permission})")).IsEqualTo("0")
				.Because($"nor does a guest");
		}

		await Assert.That(await Eval($"permission({unapproved},scene.view)")).IsEqualTo("1")
			.Because("the package grants scene.view to everyone");
		await Assert.That(await Eval($"permission({guest},scene.view)")).IsEqualTo("1");
		Log($"[GRANTS] approved={approved} unapproved={unapproved} guest={guest} — the defaults hold");

		// ---- 2. Stage: a room, the players, and the Scene Logger co-located ----------------------
		var digOut = (await God1($"@dig ApprovalStage_{Tag}")).Message.ToPlainText().Trim();
		var roomDbref = Num(digOut);
		foreach (var who in new[] { approved, unapproved, guest, reader })
			await God1($"@tel {who}={digOut}");

		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();
		// The Logger is game-wide, and a $-command only matches for objects in the caller's room — so
		// moving it here takes +scene/* away from every OTHER test's players for the rest of the run.
		// It was left in this room, and later suites saw their +scene commands silently do nothing:
		// no match, no "Huh?", no error, because an unmatched $-command on an absent object produces
		// no output at all. Note where it came from and put it back at the end.
		var loggerHome = await Eval($"loc({loggerDbref})");
		try
		{
			await God1($"@tel {loggerDbref}={digOut}");
			await Assert.That(await EvalNum($"loc({loggerDbref})")).IsEqualTo(roomDbref);

			// ---- 3. A character holding scene.create may own a scene ----------------------------------
			var createOut = await RunAs(approvedHandle, $"+scene/create Approval Test {Tag}");
			Log($"[CREATE approved] {createOut}");
			var sceneId = await Eval($"scenefocus({approved})");
			await Assert.That(sceneId).IsNotEmpty().Because("a character holding scene.create may create and own a scene");
			await Assert.That(sceneId).DoesNotStartWith("#-1");
			await God1($"@scene/set {sceneId}/public=1");
			await Assert.That(await EvalNum($"scene({sceneId}, owner)")).IsEqualTo(Num(approved));

			// ---- 4. Every ASSOCIATING route is closed without its permission --------------------------
			// Each of these is a distinct way the package could hand out a membership edge.
			var associatingVerbs = new (string Command, string Permission)[]
			{
				($"+scene/join {sceneId}", "scene.join"),
				($"+scene/tag {sceneId}", "scene.join"),
				($"+scene/activate {sceneId}", "scene.join"),
				("+scene/as The Interloper", "scene.pose"),
				($"+scene/pose {sceneId}=intrudes.", "scene.join"),
				($"+scene/create Sneaky {Tag}", "scene.create"),
				($"+scene/schedule 1893456000/Sneaky Later {Tag}=A pitch", "scene.create")
			};

			foreach (var (command, permission) in associatingVerbs)
			{
				var output = await RunAs(unapprovedHandle, command);
				Log($"[REFUSED unapproved] {command} -> {output.Replace("\n", " | ")}");
				await Assert.That(output).Contains(Needs(permission))
					.Because($"{command} needs {permission}, and the refusal names it");
				await Assert.That(await MembersAsync(sceneId)).DoesNotContain(Num(unapproved))
					.Because($"the refusal of {command} must also mean nothing was written");
			}

			await Assert.That(await Eval($"scenefocus({unapproved})")).StartsWith("#-1")
				.Because("a character without scene.join is focused on nothing");
			await Assert.That(await EvalAs(unapprovedHandle, "scenelist(mine)")).IsEmpty()
				.Because("a character without scene.join has no scenes");

			// ---- 5. A GUEST is refused too -----------------------------------------------------------
			var guestJoin = await RunAs(guestHandle, $"+scene/join {sceneId}");
			Log($"[REFUSED guest] +scene/join -> {guestJoin.Replace("\n", " | ")}");
			await Assert.That(guestJoin).Contains(Needs("scene.join"))
				.Because("guests do not hold scene.join by default");
			await Assert.That(await MembersAsync(sceneId)).DoesNotContain(Num(guest));

			// ---- 6. Viewing stays OPEN to a character that may not take part -------------------------
			var browse = await RunAs(unapprovedHandle, "+scene");
			Log($"[VIEW unapproved] +scene -> {browse.Replace("\n", " | ")[..Math.Min(160, browse.Replace("\n", " | ").Length)]}");
			await Assert.That(browse).DoesNotContain("Permission denied")
				.Because("browsing the scene list needs only scene.view");
			await Assert.That(browse).Contains("Scenes:");

			var upcoming = await RunAs(unapprovedHandle, "+scene/upcoming");
			await Assert.That(upcoming).DoesNotContain("Permission denied")
				.Because("viewing the schedule needs only scene.view");
			await Assert.That(upcoming).Contains("Scheduled Scenes");

			var info = await RunAs(unapprovedHandle, $"+scene {sceneId}");
			Log($"[VIEW unapproved] +scene {sceneId} -> {info.Replace("\n", " | ")[..Math.Min(160, info.Replace("\n", " | ").Length)]}");
			await Assert.That(info).DoesNotContain("Permission denied")
				.Because("reading scene information needs only scene.view");
			await Assert.That(info).Contains($"Scene {sceneId}");

			// ---- 7. A game changes the bar: the permissions move and the verbs follow -----------------
			await God1($"@permission/allow {unapproved}=scene.join");
			var allowedJoin = await RunAs(unapprovedHandle, $"+scene/join {sceneId}");
			Log($"[ALLOWED] +scene/join -> {allowedJoin.Replace("\n", " | ")}");
			await Assert.That(await MembersAsync(sceneId)).Contains(Num(unapproved))
				.Because("scene.join allowed on the character lets it join without the approved role");
			await RunAs(unapprovedHandle, "pose tries to pose with scene.join alone.");
			await Assert.That(await EvalAs(unapprovedHandle, $"sceneposes({sceneId},{unapproved})")).IsEmpty()
				.Because("joining is not posing: without scene.pose nothing it says is recorded");

			await God1($"@permission/deny {reader}=scene.view");
			var deniedBrowse = await RunAs(readerHandle, "+scene");
			await Assert.That(deniedBrowse).Contains(Needs("scene.view"))
				.Because("without scene.view the list says why instead of listing nothing");
			await Assert.That(await EvalAs(readerHandle, $"scene({sceneId}, status)")).StartsWith("#-1")
				.Because("and the scene functions hide a public scene from a reader without scene.view");
			await Assert.That(await EvalAs(readerHandle, "scenelist(active)")).DoesNotContain(sceneId);

			// ---- 8. The permission is checked at POSE time, so revoking it stops capture immediately ---
			var posesBefore = await Eval($"scene({sceneId}, posecount)");
			await RunAs(approvedHandle, "pose tests that capture works while approved.");
			var posesWhileApproved = await Eval($"scene({sceneId}, posecount)");
			await Assert.That(int.Parse(posesWhileApproved)).IsGreaterThan(int.Parse(posesBefore))
				.Because("a focused character holding scene.pose has its pose captured");

			await God1($"@role/unassign {approved}=approved");
			await Assert.That(await Eval($"permission({approved},scene.pose)")).IsEqualTo("0");
			await RunAs(approvedHandle, "pose tests that capture stops the moment the permission goes.");
			var posesAfterRevoke = await Eval($"scene({sceneId}, posecount)");
			Log($"[REVOKE] posecount before={posesBefore} approved={posesWhileApproved} after-revoke={posesAfterRevoke}");
			await Assert.That(posesAfterRevoke).IsEqualTo(posesWhileApproved)
				.Because("membership and focus survive revocation, so capture itself has to re-check scene.pose");
		}
		finally
		{
			await God1($"@tel {loggerDbref}={loggerHome}");
			await WebAppFactoryArg.QueueBarrierAsync();
		}
	}

	[Test]
	public async Task AtScene_Command_IsWizardOnly_ForEverySwitch()
	{
		await God1("@set #1=WIZARD");
		var (mortal, mortalHandle) = await CreatePlayerAsync($"Mortal_{Tag}");
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
			var result = await Parser.CommandParse(mortalHandle, ConnectionService, MarkupText.Plain($"@scene/{name} something=else"));
			await Assert.That(result.Message.ToPlainText()).IsEqualTo("#-1 PERMISSION DENIED")
				.Because($"@scene/{name} is wizard-only — players drive the system through +scene");
		}

		var bareResult = await Parser.CommandParse(mortalHandle, ConnectionService, MarkupText.Plain("@scene something"));
		await Assert.That(bareResult.Message.ToPlainText()).IsEqualTo("#-1 PERMISSION DENIED")
			.Because("the bare display form is gated by the same check");

		// And the player is told, not silently ignored. The FLAG^WIZARD CommandLock refuses first, so the
		// message is the engine's localized PermissionDenied rather than the plugin's own SCENE: prefix —
		// the plugin's explicit IsWizard() gate is the second line of defence behind it, not the first.
		var refusalOutput = await RunAs(mortalHandle, "@scene/list");
		await Assert.That(refusalOutput).Contains(WebAppFactoryArg.Services.GetRequiredService<ILocalizationService>()
				.Get(nameof(ErrorMessages.Notifications.PermissionDenied)))
			.Because("a refused command has to say so");
	}

	[Test]
	public async Task SceneWriteFunctions_AreUnreachableFromMortalSoftcode()
	{
		await God1("@set #1=WIZARD");
		var (mortal, mortalHandle) = await CreatePlayerAsync($"FnMor_{Tag}");
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
			$"sceneaddpose({sceneId},{mortal},,{mortal},ic,pose,,intruding)",
			$"sceneplot(create,Mortal Plot {Tag}|desc|{mortal})"
		};

		foreach (var expression in writes)
		{
			var result = await EvalAs(mortalHandle, expression);
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
		var (_, listerHandle) = await CreatePlayerAsync($"Lister_{Tag}");

		// Explicitly private: scenes are created watchable, so this is the case that must be asked for.
		var privateScene = await Eval($"scenecreate(,#1,Private {Tag})");
		await Eval($"sceneset({privateScene},public,0)");
		await God1($"@scene/set {privateScene}/status=active");
		var publicScene = await Eval($"scenecreate(,#1,Public {Tag})");
		await God1($"@scene/set {publicScene}/status=active");
		await God1($"@scene/set {publicScene}/public=1");

		var listed = await EvalAs(listerHandle, "scenelist(active)");

		await Assert.That(listed).Contains(publicScene)
			.Because("a public scene is listable by anyone");
		await Assert.That(listed).DoesNotContain(privateScene)
			.Because("the storage filters carry no visibility clause, so the function layer must");
		await Assert.That(await EvalAs(listerHandle, $"scene({privateScene}, status)")).StartsWith("#-1")
			.Because("and reading its fields stays refused, as it always was");

	}

	[Test]
	public async Task ApprovedRole_CannotBeAssignedOrUnassignedByAnUnprivilegedPlayer()
	{
		await God1("@set #1=WIZARD");
		var (setter, setterHandle) = await CreatePlayerAsync($"Setter_{Tag}");
		var (target, targetHandle) = await CreatePlayerAsync($"Target_{Tag}");

		await RunAs(setterHandle, $"@role/assign {target}=approved");
		await Assert.That(await Eval($"isapproved({target})")).IsEqualTo("0")
			.Because("assigning a role needs roles.admin; an ordinary player cannot approve anyone");

		await God1($"@role/assign {target}=approved");
		await Assert.That(await Eval($"isapproved({target})")).IsEqualTo("1");

		await RunAs(setterHandle, $"@role/unassign {target}=approved");
		await Assert.That(await Eval($"isapproved({target})")).IsEqualTo("1")
			.Because("nor can an ordinary player revoke someone else's approval");

		await Assert.That(await Eval($"isapproved({setter})")).IsEqualTo("0");
	}
}
