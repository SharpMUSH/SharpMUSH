using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Full-body narrative integration test of the SharpMUSH Scene System, driven from a
/// TEXT-BASED (in-game player) perspective — the Scene equivalent of
/// confirms the <c>SharpMUSH.Plugins.Scene</c> plugin + the bundled <c>scene</c> softcode
/// package are present, then runs a realistic multi-character roleplay scene end to end
/// purely through GAME commands (the <c>+scene/*</c> player verbs and native
/// <c>pose</c>/<c>say</c>/<c>semipose</c>), asserting the captured state at every beat.
///
/// What it proves cooperates:
///   • the plugin (commands/functions/storage/migration/flag/bridge),
///   • the bundled <c>scene</c> package (the @hook/override capture + the +scene/* verbs),
///   • the engine.
///
/// The whole story runs inside a single ordered <c>[Test]</c> so the beats share the scene
/// id and player handles, exactly like the Myrddin install→use chain. Each beat asserts
/// before the next begins.
///
/// Beats:
///   1. Setup       — boot, confirm plugin+package, create 3 players in a room.
///   2. Create+start— owner runs +scene/create then +scene/start (capture needs active).
///   3. Join        — the others +scene/join and +scene/as a persona; assert membership.
///   4. Capture     — each focused character pose/say/semiposes; assert in-order capture,
///                    correct author + showAs. A co-located but UNFOCUSED passer-by is NOT captured.
///   5. Edit/undo   — an author +scene/edit then +scene/undo; assert content versions.
///   6. Recap/info  — +scene/recall transcript and the +scene/info card.
///   7. Finish      — +scene/finish; assert status.
/// </summary>
[NotInParallel]
public class SceneRoleplayIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	// Unique suffix so re-runs in the same session never collide on names/titles.
	private static readonly string Tag = Guid.NewGuid().ToString("N")[..8];

	/// <summary>Evaluates a softcode expression as God and returns its plain text.</summary>
	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	/// <summary>Runs a command as God (#1, handle 1).</summary>
	private async Task<CallState> God1(string command) =>
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	/// <summary>Extracts the short "#N" dbref (drops any ":creation-timestamp") for like-for-like compares.</summary>
	private static string Num(string dbref)
	{
		var s = dbref.Trim();
		var colon = s.IndexOf(':');
		return colon < 0 ? s : s[..colon];
	}

	/// <summary>Evaluates an expression and returns its result as a short "#N" dbref.</summary>
	private async Task<string> EvalNum(string expression) => Num(await Eval(expression));

	private int NotificationCount() =>
		NotifyService.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

	private static string? ExtractMessageText(ICall call)
	{
		if (call.GetMethodInfo().Name != nameof(INotifyService.Notify))
			return null;
		var args = call.GetArguments();
		if (args.Length < 2)
			return null;
		return args[1] switch
		{
			SharpMessage message => message switch
			{
				MString markup => markup.ToString(),
				string text => text,
			},
			string s => s,
			MString m => m.ToString(),
			_ => null
		};
	}

	/// <summary>A single captured notification: who heard it, the text, and the short "#N" sender dbref (or null).</summary>
	private sealed record Notification(string Recipient, string Message, string? Sender);

	/// <summary>Extracts the short "#N" recipient dbref from a Notify call's first argument.</summary>
	private static string? ExtractRecipient(ICall call)
	{
		if (call.GetMethodInfo().Name != nameof(INotifyService.Notify))
			return null;
		var args = call.GetArguments();
		if (args.Length < 1)
			return null;
		return args[0] switch
		{
			DBRef dbref => dbref.ToString(),
			AnySharpObject obj => obj.Object().DBRef.ToString(),
			_ => args[0]?.ToString()
		};
	}

	/// <summary>Extracts the short "#N" sender dbref from a Notify call's third argument (the spoofed sender).</summary>
	private static string? ExtractSender(ICall call)
	{
		if (call.GetMethodInfo().Name != nameof(INotifyService.Notify))
			return null;
		var args = call.GetArguments();
		if (args.Length < 3)
			return null;
		return args[2] switch
		{
			AnySharpObject obj => obj.Object().DBRef.ToString(),
			DBRef dbref => dbref.ToString(),
			null => null,
			_ => args[2]?.ToString()
		};
	}

	private IReadOnlyList<Notification> NotificationsSince(int fromCount)
	{
		var calls = NotifyService.ReceivedCalls()
			.Where(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify))
			.ToList();
		return calls.Skip(fromCount)
			.Select(c => new Notification(
				Num(ExtractRecipient(c) ?? string.Empty),
				ExtractMessageText(c) ?? string.Empty,
				ExtractSender(c) is { } s ? Num(s) : null))
			.ToList();
	}

	/// <summary>
	/// Runs a command as a connection handle and returns what the player at that handle heard while it ran.
	/// The substitute is shared with every test running alongside, so everyone else's lines in that window
	/// (a blank <c>@pemit</c> to the Event Handler, another test's tracker) are someone else's output.
	/// </summary>
	private async Task<IReadOnlyList<string>> RunAndCollectAs(long handle, string command)
	{
		var player = ConnectionService.Get(handle)?.Ref
			?? throw new InvalidOperationException($"handle {handle} is not logged in");
		return await RunAndCollectHeardBy(handle, player.ToString(), command);
	}

	/// <summary>
	/// Runs a command as a handle and returns only what <paramref name="player"/> heard. The substitute is shared
	/// with every test running alongside, so an assertion that something is absent reads the receiver's own lines.
	/// </summary>
	private async Task<IReadOnlyList<string>> RunAndCollectHeardBy(long handle, string player, string command) =>
		(await RunAndCollectNotificationsAs(handle, command))
			.Where(n => n.Recipient == Num(player))
			.Select(n => n.Message)
			.ToList();

	/// <summary>Runs a command as a handle and returns the full (recipient, message, sender) notifications.</summary>
	private async Task<IReadOnlyList<Notification>> RunAndCollectNotificationsAs(long handle, string command)
	{
		var before = NotificationCount();
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
		return NotificationsSince(before);
	}

	/// <summary>
	/// Creates a non-God player, registers + binds a connection handle, returns its full objid.
	///
	/// <para>The player is given the approved role. Every character in this narrative is a full participant — they own,
	/// join, administer and pose into scenes — and association with a scene requires approval since the
	/// package's 1.6.0 guard. The refusal path is the subject of
	/// <see cref="Scenes.SceneApprovalIntegrationTests"/>; here approval is fixture, not subject.</para>
	/// </summary>
	private async Task<(string Dbref, long Handle)> CreatePlayerAsync(string name, string password)
	{
		await God1($"@pcreate {name}={password}");
		var dbref = (await God1($"think [pmatch({name})]")).Message.ToPlainText()?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(dbref) || dbref.StartsWith("#-") || !DBRef.TryParse(dbref, out var parsed))
			throw new InvalidOperationException($"Failed to create player {name}; pmatch returned '{dbref}'.");

		await God1($"@role/assign {dbref}=approved");

		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, parsed!.Value);

		// Return the full "#N:creation" objid — loc()/@tel resolve players reliably with it.
		// Scene functions and softcode %# emit the short "#N" form, so equality goes via Num().
		return (dbref, handle);
	}

	[Test]
	public async Task SceneRoleplay_FullNarrative_FromPlayerPerspective()
	{
		var output = new StringBuilder();
		void Log(string m) { output.AppendLine(m); TestDiagnostics.WriteLine(m); }

		Log(new string('=', 78));
		Log("SCENE SYSTEM — FULL NARRATIVE (TEXT-BASED PLAYER PERSPECTIVE)");
		Log(new string('=', 78));

		// God needs to be a wizard to create players / dig rooms cleanly.
		await God1("@set #1=WIZARD");

		// 1a. Confirm the Scene plugin surface is live: a scene…() read function resolves
		//     (an unknown id returns #-1 NOT FOUND, NOT a "no such function" error).
		var unknownScene = await Eval($"scene(nope-{Tag}, status)");
		await Assert.That(unknownScene).StartsWith("#-1")
			.Because("the scene() read function must be registered by the Scene plugin");

		// 1b. Confirm the bundled `scene` package is present: it is installed in the package
		//     registry and owns the single WIZARD 'Scene Logger' object that carries the capture
		//     hooks and +scene/* verbs (read the registry, like ScenePackageTests).
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		if (await registry.GetInstalledPackageAsync("scene") is not InstalledPackageRecord scenePackage)
			throw new InvalidOperationException("the bundled `scene` package must be installed at boot");
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		await Assert.That(packageObjects.Count).IsEqualTo(2)
			.Because("the `scene` package owns the Scene Logger and the plain object holding its +help topics");
		var loggerRef = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid);
		var loggerDbref = loggerRef.ToString(); // full "#N:creation" objid (reliable for @tel/loc)
		Log($"[SETUP] Scene Logger object: {loggerDbref} (package version {scenePackage.Version})");

		// 1c. Dig a dedicated room for the scene and co-locate three players in it.
		//     (Temp-room note: the shipped scene 1.0 package does NOT ship `+scene/create/temp` —
		//     docs/setup/scene-bootstrap.md §3 documents it as removed from 1.0 and §4 shows it as
		//     an optional softcode extension. So we use a normal dug room here, as the task
		//     permits, and assert capture against that room's active scene.)
		var roomName = $"SceneStage_{Tag}";
		var digOut = (await God1($"@dig {roomName}")).Message.ToPlainText().Trim();
		await Assert.That(digOut).DoesNotStartWith("#-1").Because("the scene room should have been dug");
		var roomDbref = Num(digOut); // short "#N" form for comparisons
		Log($"[SETUP] Scene room: {roomName} = {digOut}");

		var (alice, aliceHandle) = await CreatePlayerAsync($"Alice_{Tag}", "pw_alice_123");
		var (bob, bobHandle) = await CreatePlayerAsync($"Bob_{Tag}", "pw_bob_123");
		var (carol, carolHandle) = await CreatePlayerAsync($"Carol_{Tag}", "pw_carol_123");
		Log($"[SETUP] Players: Alice={alice} Bob={bob} Carol={carol}");

		// Teleport all three into the scene room so %L (their location) resolves to the scene room.
		foreach (var dbref in new[] { alice, bob, carol })
			await God1($"@tel {dbref}={digOut}");

		// Co-locate the Scene Logger with the players so its +scene/* $-commands match for them.
		// (AINSTALL parks the Logger in the master room #2 — proven by
		// PackageLifecycleHooksTests.Ainstall_TeleportSelfToMasterRoom_LandsObjectInRoom2 — so its
		// verbs ARE global from there. We co-locate it into this dug room anyway: this is the same
		// trick the Myrddin BBS test uses for mbboard's $-commands, and it keeps the beat independent
		// of the shared-session Logger location. The capture hooks fire regardless of locality.)
		await God1($"@tel {loggerDbref}={digOut}");
		await Assert.That(await EvalNum($"loc({loggerDbref})")).IsEqualTo(roomDbref)
			.Because("the Scene Logger must be co-located so its +scene/* verbs fire for the players");

		Log($"[SETUP] locs: alice={await Eval($"loc({alice})")} bob={await Eval($"loc({bob})")} carol={await Eval($"loc({carol})")}");
		await Assert.That(await EvalNum($"loc({alice})")).IsEqualTo(roomDbref)
			.Because("Alice must be in the scene room for capture to fire");
		await Assert.That(await EvalNum($"loc({bob})")).IsEqualTo(roomDbref);
		await Assert.That(await EvalNum($"loc({carol})")).IsEqualTo(roomDbref);

		// +scene/create binds the scene to %L (the room), makes Alice owner+focused, and sets status
		// to the package default — `active` (1.1.0) — so the scene is immediately the room's active
		// scene and capture fires without a separate +scene/start.
		var sceneTitle = $"The Tavern Meeting {Tag}";
		var createMsgs = await RunAndCollectAs(aliceHandle, $"+scene/create {sceneTitle}");
		Log($"[CREATE] {string.Join(" | ", createMsgs)}");

		// The verb stamps the new scene id onto Alice's MY.SID attribute. Read it directly
		// (a plain attribute read — no scene-visibility gate), since the scene…() read functions
		// are visibility-gated and the new scene is private (so even God, who is not a member,
		// gets #-1 PERMISSION until we make it public below).
		var sceneId = await Eval($"get({alice}/MY.SID)");
		Log($"[CREATE] scene id (Alice MY.SID): {sceneId}");
		await Assert.That(sceneId).DoesNotStartWith("#-1")
			.Because("+scene/create should create a scene and stamp its id on the creator");
		await Assert.That(sceneId).IsNotEmpty()
			.Because("+scene/create should focus Alice on the new scene and record its id");

		// Make it public so the (visibility-gated) read functions can be evaluated as God here.
		await God1($"@scene/set {sceneId}/public=1");

		// And confirm the verb really focused Alice on it (now readable post-public).
		await Assert.That(await Eval($"scenefocus({alice})")).IsEqualTo(sceneId)
			.Because("+scene/create focuses the creator on the new scene");

		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("active")
			.Because("the package default status is 'active' (created scenes are immediately live)");
		await Assert.That(await EvalNum($"scene({sceneId}, owner)")).IsEqualTo(Num(alice))
			.Because("the creator becomes the owner");
		await Assert.That(await EvalNum($"scene({sceneId}, room)")).IsEqualTo(roomDbref)
			.Because("+scene/create binds the scene to the creator's room (%L)");

		// Created active → it is immediately the room's active scene (the capture pre-req).
		await Assert.That(await Eval($"scenewhere({roomDbref})")).IsEqualTo(sceneId)
			.Because("a created-active scene is the room's active scene with no separate start");

		// +scene/start on an already-active scene says so and leaves it running (it also resumes a paused one).
		var startMsgs = await RunAndCollectAs(aliceHandle, "+scene/start");
		Log($"[START] {string.Join(" | ", startMsgs)}");
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("active")
			.Because("+scene/start keeps the scene active");
		await Assert.That(await Eval($"scenewhere({roomDbref})")).IsEqualTo(sceneId)
			.Because("the scene remains the room's active scene (capture pre-req)");

		var bobJoin = await RunAndCollectAs(bobHandle, $"+scene/join {sceneId}");
		Log($"[JOIN] Bob: {string.Join(" | ", bobJoin)}");
		var carolJoin = await RunAndCollectAs(carolHandle, $"+scene/join {sceneId}");
		Log($"[JOIN] Carol: {string.Join(" | ", carolJoin)}");

		await Assert.That(await Eval($"scenefocus({bob})")).IsEqualTo(sceneId)
			.Because("+scene/join focuses the joiner on the scene");
		await Assert.That(await Eval($"scenefocus({carol})")).IsEqualTo(sceneId);

		// Personas via +scene/as (recorded on the member edge and stamped onto future poses).
		await RunAndCollectAs(aliceHandle, "+scene/as Alice the Innkeeper");
		await RunAndCollectAs(bobHandle, "+scene/as Bob the Bard");
		await RunAndCollectAs(carolHandle, "+scene/as Carol the Cloaked");

		await Assert.That(await Eval($"scenemember({sceneId}, {alice}, showas)")).IsEqualTo("Alice the Innkeeper");
		await Assert.That(await Eval($"scenemember({sceneId}, {bob}, showas)")).IsEqualTo("Bob the Bard");
		await Assert.That(await Eval($"scenemember({sceneId}, {carol}, showas)")).IsEqualTo("Carol the Cloaked");

		var members = (await Eval($"scenemembers({sceneId})"))
			.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Num).ToList();
		Log($"[MEMBERS] {string.Join(" ", members)}");
		foreach (var dbref in new[] { alice, bob, carol })
			await Assert.That(members).Contains(Num(dbref)).Because($"{dbref} should be a scene member");
		await Assert.That(await Eval($"scenemember({sceneId}, {alice}, role)")).IsEqualTo("owner");
		await Assert.That(await Eval($"scenemember({sceneId}, {bob}, role)")).IsEqualTo("participant");
		await Assert.That(await Eval($"scenemember({sceneId}, {carol}, role)")).IsEqualTo("participant");

		// Each focused character poses natively; the @hook/override on POSE/SAY/SEMIPOSE
		// reproduces the room emit AND records the pose into the scene.
		await RunAndCollectAs(aliceHandle, "pose lights a candle on the bar.");
		await RunAndCollectAs(bobHandle, "say Well met, friends!");
		await RunAndCollectAs(carolHandle, ";slips into the corner booth.");
		await RunAndCollectAs(aliceHandle, "pose pours three ales.");

		var poseIds = (await Eval($"sceneposes({sceneId})"))
			.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		Log($"[CAPTURE] pose ids in order: {string.Join(",", poseIds)}");
		await Assert.That(poseIds.Length).IsEqualTo(4)
			.Because("all four focused-in-room poses (pose/say/semi/pose) should have been captured");

		// Pose 0 — Alice POSE: "<name> <message>".
		await Assert.That(await EvalNum($"scenepose({sceneId}, {poseIds[0]}, author)")).IsEqualTo(Num(alice));
		await Assert.That(await Eval($"scenepose({sceneId}, {poseIds[0]}, showas)")).IsEqualTo("Alice the Innkeeper");
		await Assert.That(await Eval($"scenepose({sceneId}, {poseIds[0]}, source)")).IsEqualTo("pose");
		await Assert.That(await Eval($"scenepose({sceneId}, {poseIds[0]}, content)")).Contains("lights a candle on the bar.");

		// Pose 1 — Bob SAY: "<name> says, \"<msg>\"".
		await Assert.That(await EvalNum($"scenepose({sceneId}, {poseIds[1]}, author)")).IsEqualTo(Num(bob));
		await Assert.That(await Eval($"scenepose({sceneId}, {poseIds[1]}, showas)")).IsEqualTo("Bob the Bard");
		await Assert.That(await Eval($"scenepose({sceneId}, {poseIds[1]}, source)")).IsEqualTo("say");
		await Assert.That(await Eval($"scenepose({sceneId}, {poseIds[1]}, content)")).Contains("Well met, friends!");

		// Pose 2 — Carol SEMIPOSE: "<name><message>" (no space).
		await Assert.That(await EvalNum($"scenepose({sceneId}, {poseIds[2]}, author)")).IsEqualTo(Num(carol));
		await Assert.That(await Eval($"scenepose({sceneId}, {poseIds[2]}, showas)")).IsEqualTo("Carol the Cloaked");
		await Assert.That(await Eval($"scenepose({sceneId}, {poseIds[2]}, source)")).IsEqualTo("semipose");
		await Assert.That(await Eval($"scenepose({sceneId}, {poseIds[2]}, content)")).Contains("slips into the corner booth.");

		// Pose 3 — Alice POSE again.
		await Assert.That(await EvalNum($"scenepose({sceneId}, {poseIds[3]}, author)")).IsEqualTo(Num(alice));
		await Assert.That(await Eval($"scenepose({sceneId}, {poseIds[3]}, content)")).Contains("pours three ales.");

		// sceneposes filtered to one author returns only that author's poses (Alice = 2).
		var alicePoses = (await Eval($"sceneposes({sceneId}, {alice})"))
			.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(alicePoses.Length).IsEqualTo(2)
			.Because("Alice authored exactly two poses");

		// Negative capture: a co-located passer-by who is NOT focused on the scene is NOT captured.
		var (dave, daveHandle) = await CreatePlayerAsync($"Dave_{Tag}", "pw_dave_123");
		await God1($"@tel {dave}={digOut}");
		await Assert.That(await EvalNum($"loc({dave})")).IsEqualTo(roomDbref)
			.Because("Dave is in the same room");
		await Assert.That(await Eval($"scenefocus({dave})")).StartsWith("#-1")
			.Because("Dave never joined/focused the scene");
		await RunAndCollectAs(daveHandle, "pose loiters by the door, eavesdropping.");
		var afterDave = (await Eval($"sceneposes({sceneId})"))
			.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(afterDave.Length).IsEqualTo(4)
			.Because("an unfocused passer-by's pose must NOT be captured into the scene");
		await Assert.That(await Eval($"sceneposes({sceneId}, {dave})"))
			.IsEqualTo(string.Empty)
			.Because("Dave authored no captured poses");

		// +scene/edit <poseId>=<find>^^^<replace>; author-only is enforced by @scene/editpose.
		var bobPoseId = poseIds[1];
		await RunAndCollectAs(bobHandle, $"+scene/edit {bobPoseId}=friends^^^companions");
		await Assert.That(await Eval($"scenepose({sceneId}, {bobPoseId}, content)")).Contains("companions")
			.Because("+scene/edit should rewrite the content (friends → companions)");
		await Assert.That(await Eval($"scenepose({sceneId}, {bobPoseId}, content)")).DoesNotContain("friends!")
			.Because("the original word should be gone after the edit");
		var editCountAfter = await Eval($"scenepose({sceneId}, {bobPoseId}, editcount)");
		Log($"[EDIT] editcount after edit: {editCountAfter}");

		await RunAndCollectAs(bobHandle, $"+scene/undo {bobPoseId}");
		await Assert.That(await Eval($"scenepose({sceneId}, {bobPoseId}, content)")).Contains("Well met, friends!")
			.Because("+scene/undo should restore the pre-edit content");

		// +scene/recall <count> prints the last <count> pose contents (Alice is focused).
		var recapMsgs = await RunAndCollectHeardBy(aliceHandle, alice, "+scene/recall 10");
		var recap = string.Join("\n", recapMsgs);
		Log($"[RECAP]\n{recap}");
		await Assert.That(recap).Contains("lights a candle on the bar.")
			.Because("recap should include Alice's opening pose");
		await Assert.That(recap).Contains("Well met, friends!")
			.Because("recap should include Bob's (undone) say");
		await Assert.That(recap).Contains("slips into the corner booth.")
			.Because("recap should include Carol's semipose");
		await Assert.That(recap).DoesNotContain("eavesdropping")
			.Because("Dave's uncaptured pose must never appear in the transcript");

		// +scene/rewrite <poseId>=<text>: the portal's Edit, the whole text at once. Commas survive (the text is
		// one field to @scene/editpose), the composer's %r becomes a line break, and only the author may.
		await RunAndCollectAs(aliceHandle, $"+scene/rewrite {bobPoseId}=Not Bob's words.");
		await RunAndCollectAs(aliceHandle, $"+scene/edit {bobPoseId}=friends^^^strangers");
		await Assert.That(await Eval($"scenepose({sceneId}, {bobPoseId}, content)")).Contains("Well met, friends!")
			.Because("only the author may rewrite or edit a pose");
		// The text is evaluated as its author, never as the WIZARD logger the command runs on.
		await RunAndCollectAs(bobHandle, $"+scene/rewrite {bobPoseId}=[name(%!)] was here.");
		await Assert.That(await Eval($"scenepose({sceneId}, {bobPoseId}, content)")).IsEqualTo($"Bob_{Tag} was here.")
			.Because("functions in a rewrite run as the player who sent it");
		await RunAndCollectAs(bobHandle, $"+scene/rewrite {bobPoseId}=Well met, all, and welcome.%rSit, please.");
		var rewritten = await Eval($"scenepose({sceneId}, {bobPoseId}, content)");
		Log($"[REWRITE] {rewritten}");
		await Assert.That(rewritten).IsEqualTo("Well met, all, and welcome.\nSit, please.")
			.Because("+scene/rewrite replaces the whole text, commas included, and the composer's %r is a line break");
		// The portal's Edit starts from decompose() of the pose. Sent back unchanged it is the same pose, colours
		// and escaped specials included. The command line is the one evaluation: a second would run the [OOC]
		// that the first left behind.
		const string decomposed = @"[ansi(hr,Well met)]\,%b[tagwrap(b,all)].%r[ansi(c,Sit)]\; please \[OOC\].";
		await RunAndCollectAs(bobHandle, $"+scene/rewrite {bobPoseId}={decomposed}");
		var markup = await Eval($"scenepose({sceneId}, {bobPoseId}, markup)");
		await Assert.That(SoftcodeDecomposer.Decompose(MarkupTextSerializer.Deserialize(markup))).IsEqualTo(decomposed)
			.Because("an edit saved without changes must not lose the pose's colours or tags");
		// An empty rewrite would blank the pose (#1514): it is refused, and removing a pose stays +scene/delete.
		var refusal = string.Join("\n", await RunAndCollectAs(bobHandle, $"+scene/rewrite {bobPoseId}="));
		await Assert.That(await Eval($"scenepose({sceneId}, {bobPoseId}, markup)")).IsEqualTo(markup)
			.Because("+scene/rewrite with no text must leave the pose as it was");
		await Assert.That(refusal).Contains("can't be left empty")
			.Because("the author is told why nothing changed");

		// The pose tracker and the scene browser are the two tables whose rows come out of an iter()
		// over a list — a nested one, in the tracker's case, sorting members by how long since each
		// last posed. Both name their element with %iL rather than ##; an empty table body is what a
		// substitution that resolved to nothing would look like.
		var potMsgs = await RunAndCollectHeardBy(aliceHandle, alice, "+pot");
		var pot = string.Join("\n", potMsgs);
		Log($"[POT]\n{pot}");
		await Assert.That(pot).Contains("Pose Tracker").Because("the tracker prints its header");
		await Assert.That(pot).Contains("Alice the Innkeeper")
			.Because("one row per member, named by the element the nested iter() yields");
		await Assert.That(pot).Contains("Bob the Bard");
		await Assert.That(pot).Contains("Carol the Cloaked");
		await Assert.That(pot).DoesNotContain("#-1")
			.Because("every Idle cell used to read '#-1 ARGUMENT MUST BE INTEGER': msecs() is an OBJECT's "
				+ "modification time in seconds, not the clock in milliseconds");
		await Assert.That(pot).DoesNotContain("I can't see that here")
			.Because("and msecs() with no argument tried to locate an object named '', once per member");

		var browseMsgs = await RunAndCollectAs(aliceHandle, "+scene");
		var browse = string.Join("\n", browseMsgs);
		Log($"[BROWSE]\n{browse}");
		await Assert.That(browse).Contains("Scenes:").Because("the browser prints its header");
		await Assert.That(browse).Contains(sceneId)
			.Because("the running scene is a row, built from the id the iter() yields");

		// +scene/info <id> is the scene's card. Its Players table is one row per member: what they are
		// to the scene, the name they answer to, and how much they have posed. The personas that used
		// to have a Cast line of their own now sit in the Name column beside the character.
		var whoMsgs = await RunAndCollectAs(aliceHandle, $"+scene/info {sceneId}");
		var who = string.Join("\n", whoMsgs);
		Log($"[INFO]\n{who}");
		await Assert.That(who).Contains("Players").Because("the card prints the players table");
		await Assert.That(who).Contains("Alice the Innkeeper");
		await Assert.That(who).Contains("Bob the Bard");
		await Assert.That(who).Contains("Carol the Cloaked");

		// scenecast() (the data behind the card's Cast line) carries exactly the three personas.
		var cast = await Eval($"scenecast({sceneId})");
		Log($"[CAST] {cast}");
		foreach (var persona in new[] { "Alice the Innkeeper", "Bob the Bard", "Carol the Cloaked" })
			await Assert.That(cast).Contains(persona);

		var finishMsgs = await RunAndCollectAs(aliceHandle, "+scene/finish");
		Log($"[FINISH] {string.Join(" | ", finishMsgs)}");
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("finished")
			.Because("+scene/finish drives status to finished");
		await Assert.That(await Eval($"scenefocus({alice})")).StartsWith("#-1")
			.Because("+scene/finish clears the owner's focus");
		await Assert.That(await Eval($"scenewhere({roomDbref})")).StartsWith("#-1")
			.Because("a finished scene is no longer the room's active scene");

		Log(new string('=', 78));
		Log("SCENE NARRATIVE COMPLETE — all beats asserted.");
		Log(new string('=', 78));

		var outPath = Path.Combine(AppContext.BaseDirectory, "Integration", "TestData", "SceneRoleplay_TestOutput.txt");
		Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
		await File.WriteAllTextAsync(outPath, output.ToString());
		TestDiagnostics.WriteLine($"[SCENE] Full narrative output written to: {outPath}");
	}

	/// <summary>
	/// +pot — the query-on-run Pose Tracker. Two members; one poses, one never does. The tracker
	/// must render (header + aligned rows), list both, and put the never-posed member (oldest) up next.
	/// </summary>
	[Test]
	public async Task ScenePot_OrdersMembersOldestPoseUpNext()
	{
		await God1("@set #1=WIZARD");

		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();

		var digOut = (await God1($"@dig PotRoom_{Tag}")).Message.ToPlainText().Trim();
		var (pat, patHandle) = await CreatePlayerAsync($"Pat_{Tag}", "pw_pat_123");
		var (quinn, quinnHandle) = await CreatePlayerAsync($"Quinn_{Tag}", "pw_quinn_123");
		foreach (var p in new[] { pat, quinn }) await God1($"@tel {p}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");

		await RunAndCollectAs(patHandle, $"+scene/create PotTest_{Tag}");
		await RunAndCollectAs(patHandle, "+scene/start");
		var sceneId = await Eval($"get({pat}/MY.SID)");   // CMD`CREATE records the id in MY.SID
		await Assert.That(sceneId).IsNotEmpty().Because("+scene/create should have recorded the scene id");
		await Assert.That(sceneId).DoesNotStartWith("#-1");

		await RunAndCollectAs(quinnHandle, $"+scene/join {sceneId}");
		// Quinn deliberately never poses → oldest (never) → up next.
		await RunAndCollectAs(patHandle, "pose stretches and yawns by the fire.");   // captured for Pat

		var potMsgs = await RunAndCollectHeardBy(patHandle, pat, "+pot");
		var lines = potMsgs.SelectMany(m => m.Split('\n')).Select(l => l.TrimEnd()).ToList();
		var table = string.Join("\n", lines);
		TestDiagnostics.WriteLine("=== +pot ===\n" + table);

		await Assert.That(table).Contains("Pose Tracker").Because("the +pot header should render");
		await Assert.That(table).Contains($"Pat_{Tag}").Because("Pat (a poser) should be listed");
		await Assert.That(table).Contains($"Quinn_{Tag}").Because("Quinn (a member) should be listed");

		var quinnLine = lines.First(l => l.Contains($"Quinn_{Tag}"));
		await Assert.That(quinnLine.ToLowerInvariant()).Contains("up")
			.Because("the never-posed member (Quinn) is oldest, so the up-next marker is on Quinn's row");
		await Assert.That(lines.Any(l => l.Contains("Last pose"))).IsTrue().Because("the column says how long ago each last posed");
		await Assert.That(table).DoesNotContain("Words").Because("the tracker does not count words");
		await Assert.That(lines.First(l => l.Contains($"Pat_{Tag}"))).Contains(" ago").Because("Pat posed, so the row says how long ago");
		await Assert.That(quinnLine).DoesNotContain(" ago").Because("Quinn never posed");
	}

	/// <summary>+scene (bare) — the align()'d scene browser (active list). A created+started scene must
	/// appear in the active table with its title and status.</summary>
	[Test]
	public async Task SceneList_RendersBrowserTableWithCreatedScene()
	{
		await God1("@set #1=WIZARD");

		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();

		var digOut = (await God1($"@dig ListRoom_{Tag}")).Message.ToPlainText().Trim();
		var (rob, robHandle) = await CreatePlayerAsync($"Rob_{Tag}", "pw_rob_123");
		await God1($"@tel {rob}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");

		await RunAndCollectAs(robHandle, $"+scene/create ListTest_{Tag}");
		await RunAndCollectAs(robHandle, "+scene/start");

		var listMsgs = await RunAndCollectAs(robHandle, "+scene");
		var table = string.Join("\n", listMsgs.SelectMany(m => m.Split('\n')).Select(l => l.TrimEnd()));
		TestDiagnostics.WriteLine("=== +scene ===\n" + table);

		await Assert.That(table).Contains("Scenes").Because("the list header should render");
		await Assert.That(table).Contains($"ListTest_{Tag}").Because("the created scene's title should appear in the table");
		await Assert.That(table).Contains("active").Because("the status column should show the started scene as active");
	}

	/// <summary>+scene/schedule — a roomless future scene. Sets scheduledfor (millis), lands in the
	/// 'scheduled' filter, and renders in the +scene/upcoming table.</summary>
	[Test]
	public async Task SceneSchedule_AddsRoomlessFutureScene()
	{
		await God1("@set #1=WIZARD");

		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();
		await God1($"@tel {loggerDbref}=#0");

		var (sam, samHandle) = await CreatePlayerAsync($"Sam_{Tag}", "pw_sam_123");
		await God1($"@tel {sam}=#0");

		// Schedule with raw epoch-seconds (convtime rejects it → SCHED_WHEN falls back to the number * 1000).
		var schedMsgs = await RunAndCollectAs(samHandle, $"+scene/schedule Gala_{Tag}=2524608000");
		var schedLine = schedMsgs.First(m => m.Contains("Scheduled scene"));
		var schedId = schedLine.Split("Scheduled scene ")[1].Split(' ')[0];
		await Assert.That(schedId).IsNotEmpty().Because("the schedule confirmation should carry the new scene id");

		await Assert.That(await Eval($"scene({schedId}, scheduledfor)")).IsEqualTo("2524608000000")
			.Because("scheduledfor should be the epoch-seconds value converted to millis");
		await Assert.That(await Eval($"scene({schedId}, status)")).IsEqualTo("scheduled");

		// scenelist() is exercised through the verb (command-parser context) rather than Eval (the
		// function-parser doesn't see the just-written scene in a collection scan; a key lookup does).
		// 2050 is beyond the default 30 days, so the whole schedule is asked for.
		var listMsgs = await RunAndCollectAs(samHandle, "+scene/upcoming all");
		var table = string.Join("\n", listMsgs.SelectMany(m => m.Split('\n')).Select(l => l.TrimEnd()));
		TestDiagnostics.WriteLine("=== +scene/upcoming ===\n" + table);
		await Assert.That(table).Contains("Scheduled Scenes").Because("the schedule header should render");
		await Assert.That(table).Contains($"Gala_{Tag}").Because("the scheduled scene's title should appear");

		// Every line fits the player's width.
		var width = int.Parse(await Eval($"width({sam})"));
		var tooWide = table.Split('\n').Where(l => l.Length > width).ToList();
		await Assert.That(tooWide).IsEmpty().Because($"no line of the schedule may be wider than {width}");
		var row = table.Split('\n').Single(l => l.Contains($"Gala_{Tag}"));
		await Assert.That(row).Contains(await Eval("timefmt($H:$M,2524608000)"))
			.Because("the Time column is the scene's time of day");
		await Assert.That(row).Contains("scheduled").Because("the Status column is wide enough for its longest word, so it does not wrap");

		// +scenes and +events are the names other games use for the same list.
		foreach (var alias in new[] { "+scenes all", "+events all", "+scenes/upcoming all", "+events/upcoming all" })
		{
			var aliased = string.Join("\n", (await RunAndCollectAs(samHandle, alias)).SelectMany(m => m.Split('\n')));
			await Assert.That(aliased).Contains($"Gala_{Tag}").Because($"{alias} lists the schedule");
		}
	}

	/// <summary>
	/// +scene/pause takes a running scene off the live list (scenewhere stops answering it, so nothing more is
	/// captured) and onto the schedule, with a new time when one is given and none when not;
	/// +scene/start &lt;id&gt; resumes it; +scene/reschedule of a running scene pauses it too.
	/// </summary>
	[Test]
	public async Task ScenePause_TakesARunningSceneOffTheLiveList_UntilItIsStarted()
	{
		await God1("@set #1=WIZARD");

		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();
		await God1($"@teleport {loggerDbref}=#2");

		var room = (await God1($"@dig PauseRoom_{Tag}")).Message.ToPlainText().Trim();
		var (una, unaHandle) = await CreatePlayerAsync($"Una_{Tag}", "pw_una_123");
		await God1($"@tel {una}={room}");

		await RunAndCollectAs(unaHandle, $"+scene/create PauseTest_{Tag}");
		var sceneId = await Eval($"get({una}/MY.SID)");
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("active");

		// Paused by id with a new time (epoch seconds, as the portal sends it).
		await RunAndCollectAs(unaHandle, $"+scene/pause {sceneId}=2524608000");
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("paused");
		await Assert.That(await Eval($"scene({sceneId}, scheduledfor)")).IsEqualTo("2524608000000");
		await Assert.That(await Eval($"scenewhere({room})")).StartsWith("#-1")
			.Because("a paused scene is not the room's live scene, so nothing more is captured into it");
		await RunAndCollectAs(unaHandle, $"+scene/pose {sceneId}=keeps talking.");
		await Assert.That(await Eval($"words(sceneposes({sceneId}))")).IsEqualTo("0")
			.Because("the portal's compose path names the scene, and a paused one records nothing");

		// Pausing a paused scene says so and changes nothing.
		var again = string.Join(" ", await RunAndCollectAs(unaHandle, "+scene/pause"));
		await Assert.That(again).Contains($"Scene {sceneId} is already paused.");
		await Assert.That(await Eval($"scene({sceneId}, scheduledfor)")).IsEqualTo("2524608000000");

		await RunAndCollectAs(unaHandle, $"+scene/start {sceneId}");
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("active");
		await Assert.That(await Eval($"scenewhere({room})")).IsEqualTo(sceneId)
			.Because("a resumed scene keeps its room and is live there again");

		// Paused again, focused and with no time: the earlier time goes, so the schedule shows no stale one.
		await RunAndCollectAs(unaHandle, "+scene/pause");
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("paused");
		await Assert.That(await Eval($"scene({sceneId}, scheduledfor)")).IsEqualTo(string.Empty);
		await RunAndCollectAs(unaHandle, $"+scene/start {sceneId}");

		// Rescheduling a running scene pauses it: a scene with a time to come is not live.
		await RunAndCollectAs(unaHandle, $"+scene/reschedule {sceneId}=2524608000");
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("paused");
		await Assert.That(await Eval($"scene({sceneId}, scheduledfor)")).IsEqualTo("2524608000000");

		// A finished scene stays finished.
		await RunAndCollectAs(unaHandle, $"+scene/finish {sceneId}");
		await RunAndCollectAs(unaHandle, $"+scene/pause {sceneId}");
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("finished");
		await RunAndCollectAs(unaHandle, $"+scene/start {sceneId}");
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("finished");
	}

	/// <summary>
	/// The names other scene systems use: +scene/list, +event &lt;id&gt;, +scene/rsvp and /unrsvp,
	/// +scene/cancel (which, like +scene/unschedule, takes a scene that has not started off the schedule);
	/// and +scene/title, which renames the focused scene.
	/// </summary>
	[Test]
	public async Task SceneAliases_AndTitle()
	{
		await God1("@set #1=WIZARD");

		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();
		await God1($"@teleport {loggerDbref}=#2");

		var room = (await God1($"@dig AliasRoom_{Tag}")).Message.ToPlainText().Trim();
		var (vic, vicHandle) = await CreatePlayerAsync($"Vic_{Tag}", "pw_vic_123");
		var (wes, wesHandle) = await CreatePlayerAsync($"Wes_{Tag}", "pw_wes_123");
		await God1($"@tel {vic}={room}");

		await RunAndCollectAs(vicHandle, $"+scene/create AliasTest_{Tag}");
		var sceneId = await Eval($"get({vic}/MY.SID)");

		await RunAndCollectAs(vicHandle, $"+scene/title Renamed_{Tag}");
		await Assert.That(await Eval($"scene({sceneId}, title)")).IsEqualTo($"Renamed_{Tag}");

		var listed = string.Join("\n", await RunAndCollectAs(vicHandle, "+scene/list"));
		await Assert.That(listed).Contains($"Renamed_{Tag}").Because("+scene/list is +scene");

		var card = string.Join("\n", await RunAndCollectAs(vicHandle, $"+event {sceneId}"));
		await Assert.That(card).Contains("Pitch").Because("+event <id> shows the scene's card");

		await RunAndCollectAs(wesHandle, $"+scene/rsvp {sceneId}");
		await Assert.That(await Eval($"scenemember({sceneId}, {wes}, role)")).IsEqualTo("attending");
		await RunAndCollectAs(wesHandle, $"+scene/unrsvp {sceneId}");
		await Assert.That(await Eval($"scenemember({sceneId}, {wes}, role)")).StartsWith("#-1");

		// A running scene is finished, not cancelled.
		await RunAndCollectAs(vicHandle, $"+scene/cancel {sceneId}");
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("active");

		await RunAndCollectAs(vicHandle, $"+scene/pause {sceneId}=2524608000");
		await RunAndCollectAs(vicHandle, $"+scene/cancel {sceneId}");
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("cancelled");
		await Assert.That(await Eval($"scene({sceneId}, scheduledfor)")).IsEqualTo(string.Empty)
			.Because("a cancelled scene leaves the schedule");
	}

	/// <summary>
	/// +scene/upcoming reads like a calendar: one table in time order, each day named on its first scene,
	/// and only the next 30 days unless a number of days (or all) is given. Every screen fits in no
	/// wider than width(%#), without a blank row or a side border, and its table has a heading row. The owner verbs that
	/// name a scene (+scene/pitch &lt;id&gt;=, +scene/title &lt;id&gt;=, +scene/private &lt;id&gt;) work without
	/// focusing it.
	/// </summary>
	[Test]
	public async Task SceneTables_FillTheWidth_AndTheScheduleIsGroupedByDay()
	{
		await God1("@set #1=WIZARD");

		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();
		await God1($"@teleport {loggerDbref}=#2");

		var (xan, xanHandle) = await CreatePlayerAsync($"Xan_{Tag}", "pw_xan_123");
		var width = int.Parse(await Eval($"width({xan})"));

		long At(int days, int hours) => new DateTimeOffset(DateTime.Today.AddDays(days).AddHours(hours)).ToUnixTimeSeconds();
		// +scene/schedule refuses a time already gone, so a scene that is late is scheduled ahead and then
		// moved back, as the clock would have done.
		async Task<string> Schedule(string title, long when)
		{
			var past = when <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			var said = await RunAndCollectHeardBy(xanHandle, xan, $"+scene/schedule {title}=" + (past ? "+1d" : when));
			var id = said.First(m => m.Contains("Scheduled scene")).Split("Scheduled scene ")[1].Split(' ')[0];
			if (past) await God1($"@scene/set {id}/scheduledfor={when * 1000}");
			return id;
		}

		await Schedule($"Evening_{Tag}", At(3, 18));
		var morning = await Schedule($"Morning_{Tag}", At(3, 9));
		var later = await Schedule($"Later_{Tag}", At(10, 12));
		await Schedule($"Far_{Tag}", At(60, 12));
		await Schedule($"Missed_{Tag}", DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds());
		await Schedule($"Gone_{Tag}", DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds());

		static List<string> Lines(IEnumerable<string> said) =>
			said.SelectMany(m => m.Split('\n')).Select(l => l.TrimEnd()).ToList();

		var table = Lines(await RunAndCollectHeardBy(xanHandle, xan, "+scene/upcoming"));
		TestDiagnostics.WriteLine("=== +scene/upcoming ===\n" + string.Join("\n", table));
		await Assert.That(table.Where(l => l.Length > width)).IsEmpty().Because($"no line may be wider than {width}");
		await Assert.That(table.Where(string.IsNullOrWhiteSpace)).IsEmpty().Because("a table has no blank rows");
		await Assert.That(table.Where(l => l.StartsWith('|'))).IsEmpty().Because("no side border is copied with a row");

		int Row(string title) => table.FindIndex(l => l.Contains(title));
		var day = await Eval($"u({loggerDbref}/FUN`SCHED_DAY,{morning})");
		await Assert.That(day).IsEqualTo(await Eval($"timefmt($a $b,{At(3, 9)})") + " " + DateTime.Today.AddDays(3).Day
			+ (DateTime.Today.AddDays(3).Year == DateTime.Today.Year ? "" : " " + DateTime.Today.AddDays(3).Year));
		var heading = table.FindIndex(l => l.Contains("Day") && l.Contains("Time") && l.Contains("Title") && l.Contains("RSVP"));
		await Assert.That(heading).IsGreaterThan(-1).Because("the table has a heading row");
		await Assert.That(Row($"Morning_{Tag}")).IsGreaterThan(heading);
		await Assert.That(table[Row($"Morning_{Tag}")]).Contains(day).Because("the first scene of a day names the day");
		await Assert.That(Row($"Evening_{Tag}")).IsGreaterThan(Row($"Morning_{Tag}")).Because("scenes run in time order within the day");
		await Assert.That(table[Row($"Evening_{Tag}")]).DoesNotContain(day).Because("two scenes on the same day name it once");
		await Assert.That(table[Row($"Morning_{Tag}")]).Contains(" 09:00 ").Because("a row carries the time of day");
		await Assert.That(Row($"Later_{Tag}")).IsGreaterThan(Row($"Evening_{Tag}"));
		await Assert.That(Row($"Far_{Tag}")).IsEqualTo(-1).Because("by default the schedule looks 30 days ahead");
		await Assert.That(table[Row($"Missed_{Tag}")]).Contains(" late ").Because("a scene past its time that never started reads late, as on the portal");
		await Assert.That(table[Row($"Morning_{Tag}")]).Contains(" scheduled ");
		await Assert.That(Row($"Gone_{Tag}")).IsEqualTo(-1).Because("a scene more than an hour past its time that never started leaves the schedule, as on the portal");
		var footer = table.Single(l => l.Contains("in the next 30 days"));
		await Assert.That(footer).Contains("later: +scenes <days>").Because("the footer says how far it looked, and how to see what it left out");

		var wider = Lines(await RunAndCollectAs(xanHandle, "+scenes 90"));
		await Assert.That(wider.Any(l => l.Contains($"Far_{Tag}"))).IsTrue().Because("+scenes <days> looks further ahead");
		var all = Lines(await RunAndCollectAs(xanHandle, "+events all"));
		await Assert.That(all.Any(l => l.Contains($"Far_{Tag}"))).IsTrue();
		await Assert.That(string.Join("\n", await RunAndCollectAs(xanHandle, "+scene/upcoming 0"))).Contains("How many days ahead?");

		// The owner names the scene instead of focusing it. Xan has joined none of them.
		await Assert.That(await Eval($"scenefocus({xan})")).StartsWith("#-1");
		await RunAndCollectAs(xanHandle, $"+scene/pitch {later}=Lanterns over the water. Bring a coat=or two.");
		await Assert.That(await Eval($"scene({later}, summary)")).IsEqualTo("Lanterns over the water. Bring a coat=or two.");
		await RunAndCollectAs(xanHandle, $"+scene/title {later}=Lantern Night_{Tag}");
		await Assert.That(await Eval($"scene({later}, title)")).IsEqualTo($"Lantern Night_{Tag}");
		var hidden = string.Join(" | ", await RunAndCollectAs(xanHandle, $"+scene/private {later}"));
		await Assert.That(await Eval($"scene({later}, public)")).IsEqualTo("0").Because(hidden);
		await RunAndCollectAs(xanHandle, $"+scene/public {later}");
		await Assert.That(await Eval($"scene({later}, public)")).IsEqualTo("1");

		var card = Lines(await RunAndCollectHeardBy(xanHandle, xan, $"+scene {later}"));
		TestDiagnostics.WriteLine("=== +scene <id> ===\n" + string.Join("\n", card));
		await Assert.That(card.Where(l => l.Length > width)).IsEmpty();
		await Assert.That(card.Where(string.IsNullOrWhiteSpace)).IsEmpty();
		await Assert.That(card.Any(l => l.Contains("Status") && l.Contains("Name") && l.Contains("Poses"))).IsTrue()
			.Because("the Players table has a heading row");
		await Assert.That(card.Single(l => l.Contains("< Pitch >")).Length).IsEqualTo(width)
			.Because("the Pitch rule spans the screen");
		await Assert.That(card).Contains("Lanterns over the water. Bring a coat=or two.")
			.Because("the pitch is a line of its own, with no border to trim off when it is copied");

		// +scene/list (and +scene/mine) fit too, with a status as long as "scheduled".
		var mine = Lines(await RunAndCollectHeardBy(xanHandle, xan, "+scene/mine"));
		TestDiagnostics.WriteLine("=== +scene/mine ===\n" + string.Join("\n", mine));
		await Assert.That(mine.Where(l => l.Length > width)).IsEmpty();
		await Assert.That(mine.Single(l => l.Contains($"Far_{Tag}"))).EndsWith(" scheduled").Because("a status is never broken across lines");

		// A list's page is a last switch.
		var firstPage = string.Join("\n", await RunAndCollectAs(xanHandle, "+scene/mine/1"));
		await Assert.That(firstPage).Contains($"Far_{Tag}");
		await Assert.That(string.Join("\n", await RunAndCollectAs(xanHandle, "+scene/mine/999"))).Contains("you asked for page 999.");
		await Assert.That(string.Join("\n", await RunAndCollectAs(xanHandle, "+scene/old/0"))).Contains("+scene/old[/<page>]");
	}

	/// <summary>+scene/deactivate keeps membership but clears focus; +scene/activate restores it.
	/// +scene/pitch sets the pitch; +scene &lt;id&gt; renders the card with it.</summary>
	[Test]
	public async Task SceneParticipation_DeactivateActivate_SummaryAndInfoCard()
	{
		await God1("@set #1=WIZARD");

		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();

		var digOut = (await God1($"@dig PartRoom_{Tag}")).Message.ToPlainText().Trim();
		var (tom, tomHandle) = await CreatePlayerAsync($"Tom_{Tag}", "pw_tom_123");
		await God1($"@tel {tom}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");

		await RunAndCollectAs(tomHandle, $"+scene/create PartTest_{Tag}");
		await RunAndCollectAs(tomHandle, "+scene/start");
		var sceneId = await Eval($"get({tom}/MY.SID)");
		await Assert.That(sceneId).IsNotEmpty();

		// Pitch (set while focused, owner-gated).
		await RunAndCollectAs(tomHandle, "+scene/pitch A tense standoff at dawn.");
		await Assert.That(await Eval($"scene({sceneId}, summary)")).IsEqualTo("A tense standoff at dawn.");

		// Deactivate: focus cleared, membership retained.
		await RunAndCollectAs(tomHandle, "+scene/deactivate");
		await Assert.That(await Eval($"scenefocus({tom})")).StartsWith("#-1")
			.Because("deactivate clears the player's focus");
		await Assert.That(await Eval($"scenemember({sceneId}, {tom}, role)")).DoesNotStartWith("#-1")
			.Because("deactivate keeps membership");

		// Activate: focus restored.
		await RunAndCollectAs(tomHandle, $"+scene/activate {sceneId}");
		await Assert.That(await Eval($"scenefocus({tom})")).IsEqualTo(sceneId)
			.Because("activate re-focuses the player");

		// Details card renders the fields (Volund-style `+scene <id>`).
		var infoMsgs = await RunAndCollectAs(tomHandle, $"+scene {sceneId}");
		var card = string.Join("\n", infoMsgs.SelectMany(m => m.Split('\n')).Select(l => l.TrimEnd()));
		TestDiagnostics.WriteLine("=== +scene <id> ===\n" + card);
		await Assert.That(card).Contains("Pitch").Because("the details card should have a Pitch row");
		await Assert.That(card).Contains("A tense standoff").Because("the pitch text should render in the card");
		await Assert.That(card).Contains("active").Because("the Status row should show the scene is active");
	}

	/// <summary>
	/// Proves #2 (AINSTALL `leave` lands the logger in the master room #2) and #4 (capture keys off the
	/// poser's loc(%#), not the logger's %L): with the logger NOT co-located, a remote player's +scene/create
	/// still works (global $-command from #2) and their pose both OUTPUTS to their room and is CAPTURED.
	/// </summary>
	[Test]
	public async Task SceneCapture_LoggerInMasterRoom_CapturesRemotePose()
	{
		await God1("@set #1=WIZARD");

		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();

		// #2: the logger must live in the master room (#2) so the +scene/* $-commands are global.
		// AINSTALL @teleports it there at install; this shared-session logger may have been moved into
		// another test's room since, so re-establish the precondition before asserting + testing.
		await God1($"@teleport {loggerDbref}=#2");
		await Assert.That(Num(await Eval($"loc({loggerDbref})"))).IsEqualTo("#2")
			.Because("the Scene Logger must be in the master room (#2) for +scene/* to match globally");

		// A player in a SEPARATE dug room — the logger is NOT co-located with them.
		var digOut = (await God1($"@dig CapRoom_{Tag}")).Message.ToPlainText().Trim();
		var (zed, zedHandle) = await CreatePlayerAsync($"Zed_{Tag}", "pw_zed_123");
		await God1($"@tel {zed}={digOut}");

		// +scene/create must work globally (logger $-commands live in #2) even though Zed isn't there.
		await RunAndCollectAs(zedHandle, $"+scene/create CapTest_{Tag}");
		await RunAndCollectAs(zedHandle, "+scene/start");
		var sceneId = await Eval($"get({zed}/MY.SID)");
		await Assert.That(sceneId).IsNotEmpty()
			.Because("+scene/create should work for a remote player when the logger is global in #2");

		// #4: pose must OUTPUT to Zed's room and be CAPTURED — using loc(%#), not the logger's %L.
		var poseMsgs = await RunAndCollectAs(zedHandle, "pose waves a banner");
		var poseOut = string.Join("\n", poseMsgs.SelectMany(m => m.Split('\n')));
		TestDiagnostics.WriteLine("=== remote pose output ===\n" + poseOut);
		await Assert.That(poseOut).Contains("waves a banner")
			.Because("the pose must emit to the poser's room (loc(%#)), not the logger's room in #2");
		await Assert.That(await Eval($"words(sceneposes({sceneId}))")).IsEqualTo("1")
			.Because("the pose must be captured into the active scene in the poser's room");
	}

	/// <summary>
	/// #1: the REGEXP capture patterns match every input form — "pose "/":" (pose), "semipose "/";"
	/// (semipose), "say "/'"' (say) — plus @emit. All five are captured with correct rendering.
	/// </summary>
	[Test]
	public async Task SceneCapture_AllInputFormsCaptured()
	{
		await God1("@set #1=WIZARD");
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();

		var digOut = (await God1($"@dig FormRoom_{Tag}")).Message.ToPlainText().Trim();
		var (ada, adaHandle) = await CreatePlayerAsync($"Ada_{Tag}", "pw_ada_123");
		await God1($"@tel {ada}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");   // co-located, to isolate FORM matching from #4

		await RunAndCollectAs(adaHandle, $"+scene/create FormTest_{Tag}");
		await RunAndCollectAs(adaHandle, "+scene/start");
		var sceneId = await Eval($"get({ada}/MY.SID)");

		await RunAndCollectAs(adaHandle, "pose waves.");          // "pose " form
		await RunAndCollectAs(adaHandle, ":nods.");               // ':' pose shortcut
		await RunAndCollectAs(adaHandle, ";grins.");              // ';' semipose shortcut
		await RunAndCollectAs(adaHandle, "\"hello there");        // '\"' say shortcut
		await RunAndCollectAs(adaHandle, "@emit The wind howls."); // @emit (currently unhooked)

		var captured = await Eval($"words(sceneposes({sceneId}))");
		var contents = await Eval($"iter(sceneposes({sceneId}),scenepose({sceneId},##,content),,|)");
		await Assert.That(captured).IsEqualTo("5")
			.Because("all five input forms (pose / : / ; / \" / @emit) must be captured");
		await Assert.That(contents).Contains($"Ada_{Tag} waves.").Because("'pose ' form, name + space");
		await Assert.That(contents).Contains($"Ada_{Tag} nods.").Because("':' pose shortcut, name + space");
		await Assert.That(contents).Contains($"Ada_{Tag}grins.").Because("';' semipose shortcut, name + no space");
		await Assert.That(contents).Contains("says, \"hello there\"").Because("'\"' say shortcut");
		await Assert.That(contents).Contains("The wind howls.").Because("@emit captured verbatim");
	}

	/// <summary>
	/// Attribution: capture re-broadcasts via @message/spoof, so the captured say/pose/@emit
	/// notification's SENDER is the real speaker (%#), NOT the WIZARD Scene Logger that runs the hook.
	/// (This is the regression that previously forced @emit out of the unit run.)
	/// </summary>
	[Test]
	public async Task SceneCapture_NotificationSenderIsSpeakerNotLogger()
	{
		await God1("@set #1=WIZARD");
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();

		var digOut = (await God1($"@dig SenderRoom_{Tag}")).Message.ToPlainText().Trim();
		var (eve, eveHandle) = await CreatePlayerAsync($"Eve_{Tag}", "pw_eve_123");
		await God1($"@tel {eve}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");

		await RunAndCollectAs(eveHandle, $"+scene/create SenderTest_{Tag}");
		await RunAndCollectAs(eveHandle, "+scene/start");

		// SAY — Eve has no FORMAT`SAY, so the built-in default literals are used ("You say…" / "Name says…").
		var sayNotes = await RunAndCollectNotificationsAs(eveHandle, "say I am the speaker.");
		var sayHeard = sayNotes.Where(n => n.Message.Contains("I am the speaker.")).ToList();
		await Assert.That(sayHeard).IsNotEmpty().Because("the say must be broadcast to the room");
		foreach (var n in sayHeard)
			await Assert.That(n.Sender).IsEqualTo(Num(eve))
				.Because("the captured say's sender must be the speaker, not the Scene Logger");
		await Assert.That(sayHeard.All(n => n.Sender != Num(loggerDbref))).IsTrue()
			.Because("the Scene Logger must never be the sender of a captured say");

		var poseNotes = await RunAndCollectNotificationsAs(eveHandle, "pose stands up.");
		var poseHeard = poseNotes.Where(n => n.Message.Contains("stands up.")).ToList();
		await Assert.That(poseHeard).IsNotEmpty();
		foreach (var n in poseHeard)
			await Assert.That(n.Sender).IsEqualTo(Num(eve))
				.Because("the captured pose's sender must be the speaker");

		var emitNotes = await RunAndCollectNotificationsAs(eveHandle, "@emit A bell tolls.");
		var emitHeard = emitNotes.Where(n => n.Message.Contains("A bell tolls.")).ToList();
		await Assert.That(emitHeard).IsNotEmpty();
		foreach (var n in emitHeard)
			await Assert.That(n.Sender).IsEqualTo(Num(eve))
				.Because("the captured @emit's sender must be the speaker (the @emit attribution regression)");
	}

	/// <summary>
	/// Speaker-vs-observer rendering from a single capture: with a per-player FORMAT`SAY set, the
	/// speaker hears the "You say…" first-person form and a co-located observer hears the "Name says…"
	/// third-person form — both produced by one say, evaluated per recipient through @message.
	/// </summary>
	[Test]
	public async Task SceneCapture_SpeakerVsObserverRendering_FromSingleSay()
	{
		await God1("@set #1=WIZARD");
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();

		var digOut = (await God1($"@dig SplitRoom_{Tag}")).Message.ToPlainText().Trim();
		var (fred, fredHandle) = await CreatePlayerAsync($"Fred_{Tag}", "pw_fred_123");
		var (gwen, gwenHandle) = await CreatePlayerAsync($"Gwen_{Tag}", "pw_gwen_123");
		foreach (var p in new[] { fred, gwen }) await God1($"@tel {p}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");

		await RunAndCollectAs(fredHandle, $"+scene/create SplitTest_{Tag}");
		await RunAndCollectAs(fredHandle, "+scene/start");

		// Fred sets a FORMAT`SAY that splits speaker (You) vs observer (Name) per recipient.
		// %0 = message, %1 = the recipient's short #N (the @message ## token), %# = the speaker.
		// Literal commas inside the format use chr(44) (raw `,`/`\,` is unreliable here).
		await God1($"&FORMAT`SAY {fred}=if(strmatch(%1,%#),You say[chr(44)] \"%0\",[name(%#)] says[chr(44)] \"%0\")");

		var sayNotes = await RunAndCollectNotificationsAs(fredHandle, "say hi all");
		var speakerLine = sayNotes.FirstOrDefault(n => n.Recipient == Num(fred) && n.Message.Contains("hi all"));
		var observerLine = sayNotes.FirstOrDefault(n => n.Recipient == Num(gwen) && n.Message.Contains("hi all"));

		await Assert.That(speakerLine).IsNotNull().Because("the speaker must hear the say");
		await Assert.That(observerLine).IsNotNull().Because("the co-located observer must hear the say");
		await Assert.That(speakerLine!.Message).Contains("You say")
			.Because("the speaker sees the first-person 'You say…' form");
		await Assert.That(observerLine!.Message).Contains($"Fred_{Tag} says")
			.Because("the observer sees the third-person 'Name says…' form");
	}

	/// <summary>
	/// Without a FORMAT`SAY, the hooked say still matches PennMUSH's do_say (src/speech.c): the speaker
	/// hears <c>You say, "…"</c> and everyone else in the room hears <c>Name says, "…"</c>, for both the
	/// <c>say</c> command and the <c>"</c> shortcut. The say is still captured in the scene.
	/// </summary>
	[Test]
	public async Task SceneCapture_SayWithoutFormat_SpeakerHearsYouSay()
	{
		await God1("@set #1=WIZARD");
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();

		var digOut = (await God1($"@dig YouSayRoom_{Tag}")).Message.ToPlainText().Trim();
		var (jane, janeHandle) = await CreatePlayerAsync($"Jane_{Tag}", "pw_jane_123");
		var (kurt, kurtHandle) = await CreatePlayerAsync($"Kurt_{Tag}", "pw_kurt_123");
		foreach (var p in new[] { jane, kurt }) await God1($"@tel {p}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");

		await RunAndCollectAs(janeHandle, $"+scene/create YouSayTest_{Tag}");
		await RunAndCollectAs(janeHandle, "+scene/start");
		var sceneId = await Eval($"get({jane}/MY.SID)");

		foreach (var (command, text) in new[] { ("say hello there", "hello there"), ("\"quoted shortcut", "quoted shortcut") })
		{
			var notes = await RunAndCollectNotificationsAs(janeHandle, command);
			// Each hearer gets the pose's rule and the line in one message; the line is its last.
			var speakerLines = notes.Where(n => n.Recipient == Num(jane) && n.Message.Contains(text)).Select(n => n.Message.Split('\n')[^1]).ToList();
			var observerLines = notes.Where(n => n.Recipient == Num(kurt) && n.Message.Contains(text)).Select(n => n.Message.Split('\n')[^1]).ToList();

			await Assert.That(speakerLines).IsEquivalentTo(new[] { $"You say, \"{text}\"" })
				.Because($"PennMUSH do_say sends the speaker the first-person line only ({command})");
			await Assert.That(observerLines).IsEquivalentTo(new[] { $"Jane_{Tag} says, \"{text}\"" })
				.Because($"PennMUSH do_say sends everyone else the third-person line ({command})");
		}

		var contents = await Eval($"iter(sceneposes({sceneId}),scenepose({sceneId},##,content),,|)");
		await Assert.That(contents).IsEqualTo($"Jane_{Tag} says, \"hello there\"|Jane_{Tag} says, \"quoted shortcut\"")
			.Because("the scene still records both says in the third-person form");
	}

	/// <summary>
	/// Per-player FORMAT override: a player who sets FORMAT`SAY changes their own rendered say; a player
	/// without it falls back to the built-in default literal baked into the capture attribute.
	/// </summary>
	[Test]
	public async Task SceneCapture_PlayerFormatOverridesDefault()
	{
		await God1("@set #1=WIZARD");
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();

		var digOut = (await God1($"@dig FmtRoom_{Tag}")).Message.ToPlainText().Trim();
		var (hugo, hugoHandle) = await CreatePlayerAsync($"Hugo_{Tag}", "pw_hugo_123");
		var (iris, irisHandle) = await CreatePlayerAsync($"Iris_{Tag}", "pw_iris_123");
		foreach (var p in new[] { hugo, iris }) await God1($"@tel {p}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");

		await RunAndCollectAs(hugoHandle, $"+scene/create FmtTest_{Tag}");
		await RunAndCollectAs(hugoHandle, "+scene/start");
		await RunAndCollectAs(irisHandle, $"+scene/join [get({hugo}/MY.SID)]");

		// Hugo sets a custom FORMAT`SAY. %0 = message, %1 = recipient dbref, %# = speaker.
		await God1($"&FORMAT`SAY {hugo}=CUSTOMFMT[name(%#)]: %0");

		var hugoSay = await RunAndCollectNotificationsAs(hugoHandle, "say with format");
		var hugoObserver = hugoSay.FirstOrDefault(n => n.Recipient == Num(iris) && n.Message.Contains("with format"));
		await Assert.That(hugoObserver).IsNotNull();
		await Assert.That(hugoObserver!.Message).Contains("CUSTOMFMT")
			.Because("a player-set FORMAT`SAY must override the default render");
		await Assert.That(hugoObserver.Message).Contains($"Hugo_{Tag}: with format")
			.Because("the custom format renders name + message");

		// Iris has no FORMAT`SAY → default literal third-person "Name says, \"…\"".
		var irisSay = await RunAndCollectNotificationsAs(irisHandle, "say no format");
		var irisObserver = irisSay.FirstOrDefault(n => n.Recipient == Num(hugo) && n.Message.Contains("no format"));
		await Assert.That(irisObserver).IsNotNull();
		await Assert.That(irisObserver!.Message).DoesNotContain("CUSTOMFMT")
			.Because("Iris set no FORMAT`SAY, so the custom format must not leak across players");
		await Assert.That(irisObserver.Message).Contains($"Iris_{Tag} says, \"no format\"")
			.Because("a player without FORMAT`SAY gets the built-in default literal");
	}

	/// <summary>
	/// server-wide HLC key sequence): two scenes created back-to-back get consecutive numeric ids,
	/// and two poses in a scene likewise.
	/// </summary>
	[Test]
	public async Task SceneAndPoseIds_AreSequentialCounters()
	{
		await God1("@set #1=WIZARD");
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = DBRef.Parse(packageObjects.Single(o => o.Ref == "logger").Objid).ToString();
		await God1($"@teleport {loggerDbref}=#2");

		var digOut = (await God1($"@dig SeqRoom_{Tag}")).Message.ToPlainText().Trim();
		var (bea, beaHandle) = await CreatePlayerAsync($"Bea_{Tag}", "pw_bea_123");
		await God1($"@tel {bea}={digOut}");

		// Two scenes back-to-back → consecutive numeric ids.
		await RunAndCollectAs(beaHandle, $"+scene/create SeqA_{Tag}");
		var idA = await Eval($"get({bea}/MY.SID)");
		await RunAndCollectAs(beaHandle, $"+scene/create SeqB_{Tag}");
		var idB = await Eval($"get({bea}/MY.SID)");
		// the 1-based counter is the trailing numeric segment in all cases.
		static int IdSeq(string id) => int.Parse(id.Split(':')[^1]);
		await Assert.That(int.TryParse(idA.Split(':')[^1], out _)).IsTrue()
			.Because("scene ids must be 1-based counter values, not GUIDs or large HLC keys");
		await Assert.That(IdSeq(idB)).IsEqualTo(IdSeq(idA) + 1)
			.Because("scene ids increment by a 1-based counter");

		// Two poses in scene B → consecutive numeric pose ids.
		await RunAndCollectAs(beaHandle, "+scene/start");
		await RunAndCollectAs(beaHandle, "pose one.");
		await RunAndCollectAs(beaHandle, "pose two.");
		var poseIds = (await Eval($"sceneposes({idB})")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(poseIds.Length).IsEqualTo(2).Because("both poses should be captured");
		await Assert.That(IdSeq(poseIds[1])).IsEqualTo(IdSeq(poseIds[0]) + 1)
			.Because("pose ids increment by a 1-based counter");
	}
}
