using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using OneOf;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Full-body narrative integration test of the SharpMUSH Scene System, driven from a
/// TEXT-BASED (in-game player) perspective — the Scene equivalent of
/// <see cref="MyrddinBBSIntegrationTests"/>. It boots the real server (default Arango),
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
///   6. Recap/who   — +scene/recall transcript and +scene/who cast.
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
		(await FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText().Trim();

	/// <summary>Runs a command as God (#1, handle 1).</summary>
	private async Task<CallState> God1(string command) =>
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

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
			OneOf<MString, string> oneOf => oneOf.Match(m => m.ToString(), s => s),
			string s => s,
			MString m => m.ToString(),
			_ => null
		};
	}

	private IReadOnlyList<string> MessagesSince(int fromCount)
	{
		var all = NotifyService.ReceivedCalls().Select(ExtractMessageText).OfType<string>().ToList();
		return all.Skip(fromCount).ToList();
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

	/// <summary>Runs a command as a connection handle and returns every notification it produced.</summary>
	private async Task<IReadOnlyList<string>> RunAndCollectAs(long handle, string command)
	{
		var before = NotificationCount();
		await Parser.CommandParse(handle, ConnectionService, MModule.single(command));
		return MessagesSince(before);
	}

	/// <summary>Runs a command as a handle and returns the full (recipient, message, sender) notifications.</summary>
	private async Task<IReadOnlyList<Notification>> RunAndCollectNotificationsAs(long handle, string command)
	{
		var before = NotificationCount();
		await Parser.CommandParse(handle, ConnectionService, MModule.single(command));
		return NotificationsSince(before);
	}

	/// <summary>Creates a non-God player, registers + binds a connection handle, returns its full objid.</summary>
	private async Task<string> CreatePlayerAsync(string name, string password, long handle)
	{
		await God1($"@pcreate {name}={password}");
		var dbref = (await God1($"think [pmatch({name})]")).Message?.ToPlainText()?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(dbref) || dbref.StartsWith("#-") || !DBRef.TryParse(dbref, out var parsed))
			throw new InvalidOperationException($"Failed to create player {name}; pmatch returned '{dbref}'.");

		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		await ConnectionService.Bind(handle, parsed!.Value);

		// Return the full "#N:creation" objid — loc()/@tel resolve players reliably with it.
		// Scene functions and softcode %# emit the short "#N" form, so equality goes via Num().
		return dbref;
	}

	[Test]
	public async Task SceneRoleplay_FullNarrative_FromPlayerPerspective()
	{
		var output = new StringBuilder();
		void Log(string m) { output.AppendLine(m); Console.WriteLine(m); }

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
		var scenePackage = await registry.GetInstalledPackageAsync("scene");
		await Assert.That(scenePackage.IsT0).IsTrue()
			.Because("the bundled `scene` package must be installed at boot");
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		await Assert.That(packageObjects.Count).IsEqualTo(1)
			.Because("the `scene` package owns exactly one object (the Scene Logger)");
		var loggerRef = PackageInstallService.ParseObjid(packageObjects.Single().Objid)!.Value;
		var loggerDbref = loggerRef.ToString(); // full "#N:creation" objid (reliable for @tel/loc)
		Log($"[SETUP] Scene Logger object: {loggerDbref} (package version {scenePackage.AsT0.Version})");

		// 1c. Dig a dedicated room for the scene and co-locate three players in it.
		//     (Temp-room note: the shipped scene 1.0 package does NOT ship `+scene/create/temp` —
		//     docs/setup/scene-bootstrap.md §3 documents it as removed from 1.0 and §4 shows it as
		//     an optional softcode extension. So we use a normal dug room here, as the task
		//     permits, and assert capture against that room's active scene.)
		var roomName = $"SceneStage_{Tag}";
		var digOut = (await God1($"@dig {roomName}")).Message!.ToPlainText().Trim();
		await Assert.That(digOut).DoesNotStartWith("#-1").Because("the scene room should have been dug");
		var roomDbref = Num(digOut); // short "#N" form for comparisons
		Log($"[SETUP] Scene room: {roomName} = {digOut}");

		var alice = await CreatePlayerAsync($"Alice_{Tag}", "pw_alice_123", 11L);
		var bob = await CreatePlayerAsync($"Bob_{Tag}", "pw_bob_123", 12L);
		var carol = await CreatePlayerAsync($"Carol_{Tag}", "pw_carol_123", 13L);
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
		var createMsgs = await RunAndCollectAs(11L, $"+scene/create {sceneTitle}");
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

		// +scene/start is idempotent on an already-active scene (it also resumes a paused one).
		var startMsgs = await RunAndCollectAs(11L, "+scene/start");
		Log($"[START] {string.Join(" | ", startMsgs)}");
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("active")
			.Because("+scene/start keeps the scene active");
		await Assert.That(await Eval($"scenewhere({roomDbref})")).IsEqualTo(sceneId)
			.Because("the scene remains the room's active scene (capture pre-req)");

		var bobJoin = await RunAndCollectAs(12L, $"+scene/join {sceneId}");
		Log($"[JOIN] Bob: {string.Join(" | ", bobJoin)}");
		var carolJoin = await RunAndCollectAs(13L, $"+scene/join {sceneId}");
		Log($"[JOIN] Carol: {string.Join(" | ", carolJoin)}");

		await Assert.That(await Eval($"scenefocus({bob})")).IsEqualTo(sceneId)
			.Because("+scene/join focuses the joiner on the scene");
		await Assert.That(await Eval($"scenefocus({carol})")).IsEqualTo(sceneId);

		// Personas via +scene/as (recorded on the member edge and stamped onto future poses).
		await RunAndCollectAs(11L, "+scene/as Alice the Innkeeper");
		await RunAndCollectAs(12L, "+scene/as Bob the Bard");
		await RunAndCollectAs(13L, "+scene/as Carol the Cloaked");

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
		await RunAndCollectAs(11L, "pose lights a candle on the bar.");
		await RunAndCollectAs(12L, "say Well met, friends!");
		await RunAndCollectAs(13L, ";slips into the corner booth.");
		await RunAndCollectAs(11L, "pose pours three ales.");

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
		var dave = await CreatePlayerAsync($"Dave_{Tag}", "pw_dave_123", 14L);
		await God1($"@tel {dave}={digOut}");
		await Assert.That(await EvalNum($"loc({dave})")).IsEqualTo(roomDbref)
			.Because("Dave is in the same room");
		await Assert.That(await Eval($"scenefocus({dave})")).StartsWith("#-1")
			.Because("Dave never joined/focused the scene");
		await RunAndCollectAs(14L, "pose loiters by the door, eavesdropping.");
		var afterDave = (await Eval($"sceneposes({sceneId})"))
			.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(afterDave.Length).IsEqualTo(4)
			.Because("an unfocused passer-by's pose must NOT be captured into the scene");
		await Assert.That(await Eval($"sceneposes({sceneId}, {dave})"))
			.IsEqualTo(string.Empty)
			.Because("Dave authored no captured poses");

		// +scene/edit <poseId>=<find>^^^<replace>; author-only is enforced by @scene/editpose.
		var bobPoseId = poseIds[1];
		await RunAndCollectAs(12L, $"+scene/edit {bobPoseId}=friends^^^companions");
		await Assert.That(await Eval($"scenepose({sceneId}, {bobPoseId}, content)")).Contains("companions")
			.Because("+scene/edit should rewrite the content (friends → companions)");
		await Assert.That(await Eval($"scenepose({sceneId}, {bobPoseId}, content)")).DoesNotContain("friends!")
			.Because("the original word should be gone after the edit");
		var editCountAfter = await Eval($"scenepose({sceneId}, {bobPoseId}, editcount)");
		Log($"[EDIT] editcount after edit: {editCountAfter}");

		await RunAndCollectAs(12L, $"+scene/undo {bobPoseId}");
		await Assert.That(await Eval($"scenepose({sceneId}, {bobPoseId}, content)")).Contains("Well met, friends!")
			.Because("+scene/undo should restore the pre-edit content");

		// +scene/recall <count> prints the last <count> pose contents (Alice is focused).
		var recapMsgs = await RunAndCollectAs(11L, "+scene/recall 10");
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

		// +scene/who <id> lists the cast (distinct personas) and the members with roles.
		var whoMsgs = await RunAndCollectAs(11L, $"+scene/who {sceneId}");
		var who = string.Join("\n", whoMsgs);
		Log($"[WHO]\n{who}");
		await Assert.That(who).Contains("Cast:").Because("+scene/who prints a Cast line");
		await Assert.That(who).Contains("Alice the Innkeeper");
		await Assert.That(who).Contains("Bob the Bard");
		await Assert.That(who).Contains("Carol the Cloaked");

		// scenecast() (the data behind +scene/who) carries exactly the three personas.
		var cast = await Eval($"scenecast({sceneId})");
		Log($"[CAST] {cast}");
		foreach (var persona in new[] { "Alice the Innkeeper", "Bob the Bard", "Carol the Cloaked" })
			await Assert.That(cast).Contains(persona);

		var finishMsgs = await RunAndCollectAs(11L, "+scene/finish");
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
		Console.WriteLine($"[SCENE] Full narrative output written to: {outPath}");
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
		var loggerDbref = PackageInstallService.ParseObjid(packageObjects.Single().Objid)!.Value.ToString();

		var digOut = (await God1($"@dig PotRoom_{Tag}")).Message!.ToPlainText().Trim();
		var pat = await CreatePlayerAsync($"Pat_{Tag}", "pw_pat_123", 21L);
		var quinn = await CreatePlayerAsync($"Quinn_{Tag}", "pw_quinn_123", 22L);
		foreach (var p in new[] { pat, quinn }) await God1($"@tel {p}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");

		await RunAndCollectAs(21L, $"+scene/create PotTest_{Tag}");
		await RunAndCollectAs(21L, "+scene/start");
		var sceneId = await Eval($"get({pat}/MY.SID)");   // CMD`CREATE records the id in MY.SID
		await Assert.That(sceneId).IsNotEmpty().Because("+scene/create should have recorded the scene id");
		await Assert.That(sceneId).DoesNotStartWith("#-1");

		await RunAndCollectAs(22L, $"+scene/join {sceneId}");
		await RunAndCollectAs(21L, "pose stretches and yawns by the fire.");   // captured for Pat
		// Quinn deliberately never poses → oldest (never) → up next.

		var potMsgs = await RunAndCollectAs(21L, "+pot");
		var lines = potMsgs.SelectMany(m => m.Split('\n')).Select(l => l.TrimEnd()).ToList();
		var table = string.Join("\n", lines);
		Console.WriteLine("=== +pot ===\n" + table);

		await Assert.That(table).Contains("Pose Tracker").Because("the +pot header should render");
		await Assert.That(table).Contains($"Pat_{Tag}").Because("Pat (a poser) should be listed");
		await Assert.That(table).Contains($"Quinn_{Tag}").Because("Quinn (a member) should be listed");

		var quinnLine = lines.First(l => l.Contains($"Quinn_{Tag}"));
		await Assert.That(quinnLine.ToLowerInvariant()).Contains("up")
			.Because("the never-posed member (Quinn) is oldest, so the up-next marker is on Quinn's row");
	}

	/// <summary>+scene (bare) — the align()'d scene browser (active list). A created+started scene must
	/// appear in the active table with its title and status.</summary>
	[Test]
	public async Task SceneList_RendersBrowserTableWithCreatedScene()
	{
		await God1("@set #1=WIZARD");

		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = PackageInstallService.ParseObjid(packageObjects.Single().Objid)!.Value.ToString();

		var digOut = (await God1($"@dig ListRoom_{Tag}")).Message!.ToPlainText().Trim();
		var rob = await CreatePlayerAsync($"Rob_{Tag}", "pw_rob_123", 31L);
		await God1($"@tel {rob}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");

		await RunAndCollectAs(31L, $"+scene/create ListTest_{Tag}");
		await RunAndCollectAs(31L, "+scene/start");

		var listMsgs = await RunAndCollectAs(31L, "+scene");
		var table = string.Join("\n", listMsgs.SelectMany(m => m.Split('\n')).Select(l => l.TrimEnd()));
		Console.WriteLine("=== +scene ===\n" + table);

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
		var loggerDbref = PackageInstallService.ParseObjid(packageObjects.Single().Objid)!.Value.ToString();
		await God1($"@tel {loggerDbref}=#0");

		var sam = await CreatePlayerAsync($"Sam_{Tag}", "pw_sam_123", 41L);
		await God1($"@tel {sam}=#0");

		// Schedule with raw epoch-seconds (convtime rejects it → SCHED_WHEN falls back to the number * 1000).
		var schedMsgs = await RunAndCollectAs(41L, $"+scene/schedule Gala_{Tag}=2524608000");
		var schedLine = schedMsgs.First(m => m.Contains("Scheduled scene"));
		var schedId = schedLine.Replace("Scheduled scene ", "").Split(' ')[0];
		await Assert.That(schedId).IsNotEmpty().Because("the schedule confirmation should carry the new scene id");

		await Assert.That(await Eval($"scene({schedId}, scheduledfor)")).IsEqualTo("2524608000000")
			.Because("scheduledfor should be the epoch-seconds value converted to millis");
		await Assert.That(await Eval($"scene({schedId}, status)")).IsEqualTo("scheduled");

		// scenelist() is exercised through the verb (command-parser context) rather than Eval (the
		// function-parser doesn't see the just-written scene in a collection scan; a key lookup does).
		var listMsgs = await RunAndCollectAs(41L, "+scene/upcoming");
		var table = string.Join("\n", listMsgs.SelectMany(m => m.Split('\n')).Select(l => l.TrimEnd()));
		Console.WriteLine("=== +scene/upcoming ===\n" + table);
		await Assert.That(table).Contains("Scheduled Scenes").Because("the schedule header should render");
		await Assert.That(table).Contains($"Gala_{Tag}").Because("the scheduled scene's title should appear");
	}

	/// <summary>+scene/deactivate keeps membership but clears focus; +scene/activate restores it.
	/// +scene/pitch sets the pitch; +scene &lt;id&gt; renders the card with it.</summary>
	[Test]
	public async Task SceneParticipation_DeactivateActivate_SummaryAndInfoCard()
	{
		await God1("@set #1=WIZARD");

		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = PackageInstallService.ParseObjid(packageObjects.Single().Objid)!.Value.ToString();

		var digOut = (await God1($"@dig PartRoom_{Tag}")).Message!.ToPlainText().Trim();
		var tom = await CreatePlayerAsync($"Tom_{Tag}", "pw_tom_123", 51L);
		await God1($"@tel {tom}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");

		await RunAndCollectAs(51L, $"+scene/create PartTest_{Tag}");
		await RunAndCollectAs(51L, "+scene/start");
		var sceneId = await Eval($"get({tom}/MY.SID)");
		await Assert.That(sceneId).IsNotEmpty();

		// Pitch (set while focused, owner-gated).
		await RunAndCollectAs(51L, "+scene/pitch A tense standoff at dawn.");
		await Assert.That(await Eval($"scene({sceneId}, summary)")).IsEqualTo("A tense standoff at dawn.");

		// Deactivate: focus cleared, membership retained.
		await RunAndCollectAs(51L, "+scene/deactivate");
		await Assert.That(await Eval($"scenefocus({tom})")).StartsWith("#-1")
			.Because("deactivate clears the player's focus");
		await Assert.That(await Eval($"scenemember({sceneId}, {tom}, role)")).DoesNotStartWith("#-1")
			.Because("deactivate keeps membership");

		// Activate: focus restored.
		await RunAndCollectAs(51L, $"+scene/activate {sceneId}");
		await Assert.That(await Eval($"scenefocus({tom})")).IsEqualTo(sceneId)
			.Because("activate re-focuses the player");

		// Details card renders the fields (Volund-style `+scene <id>`).
		var infoMsgs = await RunAndCollectAs(51L, $"+scene {sceneId}");
		var card = string.Join("\n", infoMsgs.SelectMany(m => m.Split('\n')).Select(l => l.TrimEnd()));
		Console.WriteLine("=== +scene <id> ===\n" + card);
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
		var loggerDbref = PackageInstallService.ParseObjid(packageObjects.Single().Objid)!.Value.ToString();

		// #2: the logger must live in the master room (#2) so the +scene/* $-commands are global.
		// AINSTALL @teleports it there at install; this shared-session logger may have been moved into
		// another test's room since, so re-establish the precondition before asserting + testing.
		await God1($"@teleport {loggerDbref}=#2");
		await Assert.That(Num(await Eval($"loc({loggerDbref})"))).IsEqualTo("#2")
			.Because("the Scene Logger must be in the master room (#2) for +scene/* to match globally");

		// A player in a SEPARATE dug room — the logger is NOT co-located with them.
		var digOut = (await God1($"@dig CapRoom_{Tag}")).Message!.ToPlainText().Trim();
		var zed = await CreatePlayerAsync($"Zed_{Tag}", "pw_zed_123", 61L);
		await God1($"@tel {zed}={digOut}");

		// +scene/create must work globally (logger $-commands live in #2) even though Zed isn't there.
		await RunAndCollectAs(61L, $"+scene/create CapTest_{Tag}");
		await RunAndCollectAs(61L, "+scene/start");
		var sceneId = await Eval($"get({zed}/MY.SID)");
		await Assert.That(sceneId).IsNotEmpty()
			.Because("+scene/create should work for a remote player when the logger is global in #2");

		// #4: pose must OUTPUT to Zed's room and be CAPTURED — using loc(%#), not the logger's %L.
		var poseMsgs = await RunAndCollectAs(61L, "pose waves a banner");
		var poseOut = string.Join("\n", poseMsgs.SelectMany(m => m.Split('\n')));
		Console.WriteLine("=== remote pose output ===\n" + poseOut);
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
		var loggerDbref = PackageInstallService.ParseObjid(packageObjects.Single().Objid)!.Value.ToString();

		var digOut = (await God1($"@dig FormRoom_{Tag}")).Message!.ToPlainText().Trim();
		var ada = await CreatePlayerAsync($"Ada_{Tag}", "pw_ada_123", 71L);
		await God1($"@tel {ada}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");   // co-located, to isolate FORM matching from #4

		await RunAndCollectAs(71L, $"+scene/create FormTest_{Tag}");
		await RunAndCollectAs(71L, "+scene/start");
		var sceneId = await Eval($"get({ada}/MY.SID)");

		await RunAndCollectAs(71L, "pose waves.");          // "pose " form
		await RunAndCollectAs(71L, ":nods.");               // ':' pose shortcut
		await RunAndCollectAs(71L, ";grins.");              // ';' semipose shortcut
		await RunAndCollectAs(71L, "\"hello there");        // '\"' say shortcut
		await RunAndCollectAs(71L, "@emit The wind howls."); // @emit (currently unhooked)

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
		var loggerDbref = PackageInstallService.ParseObjid(packageObjects.Single().Objid)!.Value.ToString();

		var digOut = (await God1($"@dig SenderRoom_{Tag}")).Message!.ToPlainText().Trim();
		var eve = await CreatePlayerAsync($"Eve_{Tag}", "pw_eve_123", 91L);
		await God1($"@tel {eve}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");

		await RunAndCollectAs(91L, $"+scene/create SenderTest_{Tag}");
		await RunAndCollectAs(91L, "+scene/start");

		// SAY — Eve has no FORMAT`SAY, so the built-in default literal is used for all recipients.
		var sayNotes = await RunAndCollectNotificationsAs(91L, "say I am the speaker.");
		var sayHeard = sayNotes.Where(n => n.Message.Contains("I am the speaker.")).ToList();
		await Assert.That(sayHeard).IsNotEmpty().Because("the say must be broadcast to the room");
		foreach (var n in sayHeard)
			await Assert.That(n.Sender).IsEqualTo(Num(eve))
				.Because("the captured say's sender must be the speaker, not the Scene Logger");
		await Assert.That(sayHeard.All(n => n.Sender != Num(loggerDbref))).IsTrue()
			.Because("the Scene Logger must never be the sender of a captured say");

		var poseNotes = await RunAndCollectNotificationsAs(91L, "pose stands up.");
		var poseHeard = poseNotes.Where(n => n.Message.Contains("stands up.")).ToList();
		await Assert.That(poseHeard).IsNotEmpty();
		foreach (var n in poseHeard)
			await Assert.That(n.Sender).IsEqualTo(Num(eve))
				.Because("the captured pose's sender must be the speaker");

		var emitNotes = await RunAndCollectNotificationsAs(91L, "@emit A bell tolls.");
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
		var loggerDbref = PackageInstallService.ParseObjid(packageObjects.Single().Objid)!.Value.ToString();

		var digOut = (await God1($"@dig SplitRoom_{Tag}")).Message!.ToPlainText().Trim();
		var fred = await CreatePlayerAsync($"Fred_{Tag}", "pw_fred_123", 92L);
		var gwen = await CreatePlayerAsync($"Gwen_{Tag}", "pw_gwen_123", 93L);
		foreach (var p in new[] { fred, gwen }) await God1($"@tel {p}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");

		await RunAndCollectAs(92L, $"+scene/create SplitTest_{Tag}");
		await RunAndCollectAs(92L, "+scene/start");

		// Fred sets a FORMAT`SAY that splits speaker (You) vs observer (Name) per recipient.
		// %0 = message, %1 = the recipient's FULL objid (#N:creation via the @message ## token),
		// %# = the speaker (short #N). Compare the dbref NUMBER (before the ':') so the speaker
		// matches. Literal commas inside the format use chr(44) (raw `,`/`\,` is unreliable here).
		await God1($"&FORMAT`SAY {fred}=if(strmatch(before(%1,:),%#),You say[chr(44)] \"%0\",[name(%#)] says[chr(44)] \"%0\")");

		var sayNotes = await RunAndCollectNotificationsAs(92L, "say hi all");
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
	/// Per-player FORMAT override: a player who sets FORMAT`SAY changes their own rendered say; a player
	/// without it falls back to the built-in default literal baked into the capture attribute.
	/// </summary>
	[Test]
	public async Task SceneCapture_PlayerFormatOverridesDefault()
	{
		await God1("@set #1=WIZARD");
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = PackageInstallService.ParseObjid(packageObjects.Single().Objid)!.Value.ToString();

		var digOut = (await God1($"@dig FmtRoom_{Tag}")).Message!.ToPlainText().Trim();
		var hugo = await CreatePlayerAsync($"Hugo_{Tag}", "pw_hugo_123", 94L);
		var iris = await CreatePlayerAsync($"Iris_{Tag}", "pw_iris_123", 95L);
		foreach (var p in new[] { hugo, iris }) await God1($"@tel {p}={digOut}");
		await God1($"@tel {loggerDbref}={digOut}");

		await RunAndCollectAs(94L, $"+scene/create FmtTest_{Tag}");
		await RunAndCollectAs(94L, "+scene/start");
		await RunAndCollectAs(95L, $"+scene/join [get({hugo}/MY.SID)]");

		// Hugo sets a custom FORMAT`SAY. %0 = message, %1 = recipient dbref, %# = speaker.
		await God1($"&FORMAT`SAY {hugo}=CUSTOMFMT[name(%#)]: %0");

		var hugoSay = await RunAndCollectNotificationsAs(94L, "say with format");
		var hugoObserver = hugoSay.FirstOrDefault(n => n.Recipient == Num(iris) && n.Message.Contains("with format"));
		await Assert.That(hugoObserver).IsNotNull();
		await Assert.That(hugoObserver!.Message).Contains("CUSTOMFMT")
			.Because("a player-set FORMAT`SAY must override the default render");
		await Assert.That(hugoObserver.Message).Contains($"Hugo_{Tag}: with format")
			.Because("the custom format renders name + message");

		// Iris has no FORMAT`SAY → default literal third-person "Name says, \"…\"".
		var irisSay = await RunAndCollectNotificationsAs(95L, "say no format");
		var irisObserver = irisSay.FirstOrDefault(n => n.Recipient == Num(hugo) && n.Message.Contains("no format"));
		await Assert.That(irisObserver).IsNotNull();
		await Assert.That(irisObserver!.Message).DoesNotContain("CUSTOMFMT")
			.Because("Iris set no FORMAT`SAY, so the custom format must not leak across players");
		await Assert.That(irisObserver.Message).Contains($"Iris_{Tag} says, \"no format\"")
			.Because("a player without FORMAT`SAY gets the built-in default literal");
	}

	/// <summary>
	/// Scene IDs and pose IDs come from 1-based incrementing counters (not GUIDs or ArangoDB's
	/// server-wide HLC key sequence): two scenes created back-to-back get consecutive numeric ids,
	/// and two poses in a scene likewise.
	/// </summary>
	[Test]
	public async Task SceneAndPoseIds_AreSequentialCounters()
	{
		await God1("@set #1=WIZARD");
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var packageObjects = await registry.GetPackageObjectsAsync("scene");
		var loggerDbref = PackageInstallService.ParseObjid(packageObjects.Single().Objid)!.Value.ToString();
		await God1($"@teleport {loggerDbref}=#2");

		var digOut = (await God1($"@dig SeqRoom_{Tag}")).Message!.ToPlainText().Trim();
		var bea = await CreatePlayerAsync($"Bea_{Tag}", "pw_bea_123", 81L);
		await God1($"@tel {bea}={digOut}");

		// Two scenes back-to-back → consecutive numeric ids.
		await RunAndCollectAs(81L, $"+scene/create SeqA_{Tag}");
		var idA = await Eval($"get({bea}/MY.SID)");
		await RunAndCollectAs(81L, $"+scene/create SeqB_{Tag}");
		var idB = await Eval($"get({bea}/MY.SID)");
		// Providers format the id differently (Arango/Memgraph bare "N"; SurrealDB "scene:N"/"scene_pose:N");
		// the 1-based counter is the trailing numeric segment in all cases.
		static int IdSeq(string id) => int.Parse(id.Split(':')[^1]);
		await Assert.That(int.TryParse(idA.Split(':')[^1], out _)).IsTrue()
			.Because("scene ids must be 1-based counter values, not GUIDs or large HLC keys");
		await Assert.That(IdSeq(idB)).IsEqualTo(IdSeq(idA) + 1)
			.Because("scene ids increment by a 1-based counter");

		// Two poses in scene B → consecutive numeric pose ids.
		await RunAndCollectAs(81L, "+scene/start");
		await RunAndCollectAs(81L, "pose one.");
		await RunAndCollectAs(81L, "pose two.");
		var poseIds = (await Eval($"sceneposes({idB})")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(poseIds.Length).IsEqualTo(2).Because("both poses should be captured");
		await Assert.That(IdSeq(poseIds[1])).IsEqualTo(IdSeq(poseIds[0]) + 1)
			.Because("pose ids increment by a 1-based counter");
	}
}
