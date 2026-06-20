using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using OneOf;
using SharpMUSH.Library;
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
///   3. Join        — the others +scene/join and +scene/showas a persona; assert membership.
///   4. Capture     — each focused character pose/say/semiposes; assert in-order capture,
///                    correct author + showAs. A co-located but UNFOCUSED passer-by is NOT captured.
///   5. Edit/undo   — an author +scene/edit then +scene/undo; assert content versions.
///   6. Recap/who   — +scene/recap transcript and +scene/who cast.
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

	/// <summary>Runs a command as a connection handle and returns every notification it produced.</summary>
	private async Task<IReadOnlyList<string>> RunAndCollectAs(long handle, string command)
	{
		var before = NotificationCount();
		await Parser.CommandParse(handle, ConnectionService, MModule.single(command));
		return MessagesSince(before);
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

		// ── Beat 1: Setup ─────────────────────────────────────────────────────────
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
		// (The bundled package parks the Logger in its own package room — it is NOT placed in the
		// master room, so its verbs are not global. This is the same co-location trick the Myrddin
		// BBS test uses for mbboard's $-commands. The capture hooks fire regardless of locality.)
		await God1($"@tel {loggerDbref}={digOut}");
		await Assert.That(await EvalNum($"loc({loggerDbref})")).IsEqualTo(roomDbref)
			.Because("the Scene Logger must be co-located so its +scene/* verbs fire for the players");

		Log($"[SETUP] locs: alice={await Eval($"loc({alice})")} bob={await Eval($"loc({bob})")} carol={await Eval($"loc({carol})")}");
		await Assert.That(await EvalNum($"loc({alice})")).IsEqualTo(roomDbref)
			.Because("Alice must be in the scene room for capture to fire");
		await Assert.That(await EvalNum($"loc({bob})")).IsEqualTo(roomDbref);
		await Assert.That(await EvalNum($"loc({carol})")).IsEqualTo(roomDbref);

		// ── Beat 2: Create + start (run as the owner, Alice) ──────────────────────
		// +scene/create binds the scene to %L (the room), makes Alice owner+focused, and sets
		// status to the package default ("new"). Capture requires the scene be ACTIVE in the
		// room (GetActiveSceneInRoomAsync filters Status=='active'), so +scene/start is needed
		// before any pose is captured.
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

		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("new")
			.Because("the package default status is 'new'");
		await Assert.That(await EvalNum($"scene({sceneId}, owner)")).IsEqualTo(Num(alice))
			.Because("the creator becomes the owner");
		await Assert.That(await EvalNum($"scene({sceneId}, room)")).IsEqualTo(roomDbref)
			.Because("+scene/create binds the scene to the creator's room (%L)");

		// Before start, the room has no ACTIVE scene → scenewhere is NOT FOUND.
		await Assert.That(await Eval($"scenewhere({roomDbref})")).StartsWith("#-1")
			.Because("a 'new' scene is not yet the room's active scene");

		// Start it (owner-only). Now scenewhere resolves to our scene.
		var startMsgs = await RunAndCollectAs(11L, "+scene/start");
		Log($"[START] {string.Join(" | ", startMsgs)}");
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("active")
			.Because("+scene/start drives status to active");
		await Assert.That(await Eval($"scenewhere({roomDbref})")).IsEqualTo(sceneId)
			.Because("an active scene is now the room's active scene (capture pre-req)");

		// ── Beat 3: Join + showas (Bob and Carol) ─────────────────────────────────
		var bobJoin = await RunAndCollectAs(12L, $"+scene/join {sceneId}");
		Log($"[JOIN] Bob: {string.Join(" | ", bobJoin)}");
		var carolJoin = await RunAndCollectAs(13L, $"+scene/join {sceneId}");
		Log($"[JOIN] Carol: {string.Join(" | ", carolJoin)}");

		await Assert.That(await Eval($"scenefocus({bob})")).IsEqualTo(sceneId)
			.Because("+scene/join focuses the joiner on the scene");
		await Assert.That(await Eval($"scenefocus({carol})")).IsEqualTo(sceneId);

		// Personas via +scene/showas (recorded on the member edge and stamped onto future poses).
		await RunAndCollectAs(11L, "+scene/showas Alice the Innkeeper");
		await RunAndCollectAs(12L, "+scene/showas Bob the Bard");
		await RunAndCollectAs(13L, "+scene/showas Carol the Cloaked");

		await Assert.That(await Eval($"scenemember({sceneId}, {alice}, showas)")).IsEqualTo("Alice the Innkeeper");
		await Assert.That(await Eval($"scenemember({sceneId}, {bob}, showas)")).IsEqualTo("Bob the Bard");
		await Assert.That(await Eval($"scenemember({sceneId}, {carol}, showas)")).IsEqualTo("Carol the Cloaked");

		// Membership: all three are members; roles reflect owner vs participant.
		var members = (await Eval($"scenemembers({sceneId})"))
			.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Num).ToList();
		Log($"[MEMBERS] {string.Join(" ", members)}");
		foreach (var dbref in new[] { alice, bob, carol })
			await Assert.That(members).Contains(Num(dbref)).Because($"{dbref} should be a scene member");
		await Assert.That(await Eval($"scenemember({sceneId}, {alice}, role)")).IsEqualTo("owner");
		await Assert.That(await Eval($"scenemember({sceneId}, {bob}, role)")).IsEqualTo("participant");
		await Assert.That(await Eval($"scenemember({sceneId}, {carol}, role)")).IsEqualTo("participant");

		// ── Beat 4: Pose capture from multiple characters (native pose/say/semipose) ─
		// Each focused character poses natively; the @hook/override on POSE/SAY/SEMIPOSE
		// reproduces the room emit AND records the pose into the scene.
		await RunAndCollectAs(11L, "pose lights a candle on the bar.");          // POSE  (Alice)
		await RunAndCollectAs(12L, "say Well met, friends!");                    // SAY   (Bob)
		await RunAndCollectAs(13L, ";slips into the corner booth.");             // SEMI  (Carol)
		await RunAndCollectAs(11L, "pose pours three ales.");                    // POSE  (Alice again)

		var poseIds = (await Eval($"sceneposes({sceneId})"))
			.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		Log($"[CAPTURE] pose ids in order: {string.Join(",", poseIds)}");
		await Assert.That(poseIds.Length).IsEqualTo(4)
			.Because("all four focused-in-room poses (pose/say/semi/pose) should have been captured");

		// In-order, attributed to the right author with the right showAs and source.
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

		// ── Beat 5: Edit + undo your own pose (Bob edits his SAY pose) ────────────
		// +scene/edit <poseId>=<find>^^^<replace>; author-only is enforced by @scene/editpose.
		var bobPoseId = poseIds[1];
		await RunAndCollectAs(12L, $"+scene/edit {bobPoseId}=friends^^^companions");
		await Assert.That(await Eval($"scenepose({sceneId}, {bobPoseId}, content)")).Contains("companions")
			.Because("+scene/edit should rewrite the content (friends → companions)");
		await Assert.That(await Eval($"scenepose({sceneId}, {bobPoseId}, content)")).DoesNotContain("friends!")
			.Because("the original word should be gone after the edit");
		var editCountAfter = await Eval($"scenepose({sceneId}, {bobPoseId}, editcount)");
		Log($"[EDIT] editcount after edit: {editCountAfter}");

		// Undo restores the previous version.
		await RunAndCollectAs(12L, $"+scene/undo {bobPoseId}");
		await Assert.That(await Eval($"scenepose({sceneId}, {bobPoseId}, content)")).Contains("Well met, friends!")
			.Because("+scene/undo should restore the pre-edit content");

		// ── Beat 6: Recap + who ───────────────────────────────────────────────────
		// +scene/recap <count> prints the last <count> pose contents (Alice is focused).
		var recapMsgs = await RunAndCollectAs(11L, "+scene/recap 10");
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

		// ── Beat 7: Finish ────────────────────────────────────────────────────────
		var finishMsgs = await RunAndCollectAs(11L, "+scene/finish");
		Log($"[FINISH] {string.Join(" | ", finishMsgs)}");
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("finished")
			.Because("+scene/finish drives status to finished");
		// Finishing also clears the owner's focus.
		await Assert.That(await Eval($"scenefocus({alice})")).StartsWith("#-1")
			.Because("+scene/finish clears the owner's focus");
		// A finished scene is no longer the room's active scene.
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
	/// +scene/pot — the query-on-run Pose Tracker. Two members; one poses, one never does. The tracker
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

		var potMsgs = await RunAndCollectAs(21L, "+scene/pot");
		var lines = potMsgs.SelectMany(m => m.Split('\n')).Select(l => l.TrimEnd()).ToList();
		var table = string.Join("\n", lines);
		Console.WriteLine("=== +scene/pot ===\n" + table);

		await Assert.That(table).Contains("Pose Tracker").Because("the +pot header should render");
		await Assert.That(table).Contains($"Pat_{Tag}").Because("Pat (a poser) should be listed");
		await Assert.That(table).Contains($"Quinn_{Tag}").Because("Quinn (a member) should be listed");

		var quinnLine = lines.First(l => l.Contains($"Quinn_{Tag}"));
		await Assert.That(quinnLine.ToLowerInvariant()).Contains("up")
			.Because("the never-posed member (Quinn) is oldest, so the up-next marker is on Quinn's row");
	}

	/// <summary>+scene/list — the align()'d scene browser. A created+started scene must appear in the
	/// 'recent' table with its title and status.</summary>
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

		var listMsgs = await RunAndCollectAs(31L, "+scene/list");
		var table = string.Join("\n", listMsgs.SelectMany(m => m.Split('\n')).Select(l => l.TrimEnd()));
		Console.WriteLine("=== +scene/list ===\n" + table);

		await Assert.That(table).Contains("Scenes").Because("the list header should render");
		await Assert.That(table).Contains($"ListTest_{Tag}").Because("the created scene's title should appear in the table");
		await Assert.That(table).Contains("active").Because("the status column should show the started scene as active");
	}
}
