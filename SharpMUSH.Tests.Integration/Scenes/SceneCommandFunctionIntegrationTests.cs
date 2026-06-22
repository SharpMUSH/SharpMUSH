using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Contracts;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// Phase 3 integration tests for the softcode surface: the wizard-only @SCENE
/// command (writes, driven as God #1) and the scene…() read functions (reads).
/// Data is created via ISceneService, mutated via the @SCENE command, and read
/// back via the functions — proving the command→service and function→service
/// wiring end to end against the configured provider.
/// </summary>
[NotInParallel]
public class SceneCommandFunctionIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private IMUSHCodeParser CommandParser => WebAppFactory.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactory.FunctionParser;
	private IConnectionService Connection => WebAppFactory.Services.GetRequiredService<IConnectionService>();
	private ISceneService Scenes => WebAppFactory.Services.GetRequiredService<ISceneService>();

	private const string God = "#1";

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText();

	private async Task Cmd(string command) =>
		await CommandParser.CommandParse(1, Connection, MModule.single(command));

	[Test]
	public async Task SceneCommand_Set_MutatesStatus_ReadableViaFunction()
	{
		var scene = await Scenes.CreateSceneAsync(roomDbref: "", ownerDbref: God, title: "Phase3 set");
		// Make it public so the read functions' visibility check lets anyone read it.
		await Cmd($"@scene/set {scene.Id}/public=1");

		// Status starts at the create default.
		await Assert.That(await Eval($"scene({scene.Id}, status)")).IsEqualTo("new");

		// @scene/set drives the status to active...
		await Cmd($"@scene/set {scene.Id}/status=active");

		// ...and the read function sees it.
		await Assert.That(await Eval($"scene({scene.Id}, status)")).IsEqualTo("active");
	}

	[Test]
	public async Task SceneFunctions_ReadPosesAndContent()
	{
		var scene = await Scenes.CreateSceneAsync(roomDbref: "", ownerDbref: God, title: "Phase3 poses");
		await Cmd($"@scene/set {scene.Id}/public=1");

		var pose = await Scenes.AddPoseAsync(scene.Id, God, "", God, "pose", [], "hello phase3 pose");
		await Assert.That(pose.IsT0).IsTrue();
		var poseId = pose.AsT0.Id;

		// sceneposes lists the scene's pose ids (space-joined).
		var posesList = await Eval($"sceneposes({scene.Id})");
		await Assert.That(posesList).IsNotEmpty();
		await Assert.That(posesList).DoesNotStartWith("#-1");
		await Assert.That(posesList).Contains(poseId);

		// scenepose returns the requested field of one pose.
		await Assert.That(await Eval($"scenepose({scene.Id}, {poseId}, content)")).Contains("hello phase3 pose");
	}

	[Test]
	public async Task SceneFunctions_UnknownScene_ReturnsError()
	{
		var result = await Eval($"scene(does-not-exist-{Guid.NewGuid():N}, status)");
		await Assert.That(result).StartsWith("#-1");
	}

	[Test]
	public async Task SceneList_IncludesACreatedScene()
	{
		var scene = await Scenes.CreateSceneAsync(roomDbref: "", ownerDbref: God, title: "Phase3 list");
		await Cmd($"@scene/set {scene.Id}/public=1");

		var listed = await Eval("scenelist(recent)");
		await Assert.That(listed).DoesNotStartWith("#-1");
		await Assert.That(listed).Contains(scene.Id);
	}

	/// <summary>
	/// Service-level (runs on every provider, including SurrealDB): scene and pose ids come from 1-based
	/// incrementing counters, not GUIDs or ArangoDB's server-wide HLC key sequence. Providers format the
	/// id differently (Arango/Memgraph bare "N"; SurrealDB "scene:N"/"scene_pose:N") — the counter is the
	/// trailing numeric segment in all cases.
	/// </summary>
	[Test]
	public async Task SceneAndPoseIds_AreSequentialCounters_ViaService()
	{
		static int IdSeq(string id) => int.Parse(id.Split(':')[^1]);

		var a = await Scenes.CreateSceneAsync(roomDbref: "", ownerDbref: God, title: "SeqA");
		var b = await Scenes.CreateSceneAsync(roomDbref: "", ownerDbref: God, title: "SeqB");
		await Assert.That(int.TryParse(a.Id.Split(':')[^1], out _)).IsTrue()
			.Because("scene ids must be 1-based counter values, not GUIDs or large HLC keys");
		await Assert.That(IdSeq(b.Id)).IsEqualTo(IdSeq(a.Id) + 1)
			.Because("scene ids increment by a 1-based counter");

		var p1 = (await Scenes.AddPoseAsync(b.Id, God, "", God, "pose", [], "one")).AsT0;
		var p2 = (await Scenes.AddPoseAsync(b.Id, God, "", God, "pose", [], "two")).AsT0;
		await Assert.That(int.TryParse(p1.Id.Split(':')[^1], out _)).IsTrue()
			.Because("pose ids must be 1-based counter values");
		await Assert.That(IdSeq(p2.Id)).IsEqualTo(IdSeq(p1.Id) + 1)
			.Because("pose ids increment by a 1-based counter");
	}
}
