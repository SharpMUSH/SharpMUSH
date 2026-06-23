using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// Integration tests for the softcode surface: the wizard-only @SCENE command (writes, driven as God #1)
/// and the scene…() read functions (reads). Everything goes over the WIRE — the host no longer references
/// <c>ISceneService</c> (it now lives inside the Scene plugin's ALC). Data is seeded via the wizard-only
/// <c>scene…()</c> side-effect functions / the @SCENE command, then read back via the read functions —
/// proving the command→service and function→service wiring end to end against the configured provider.
/// </summary>
[NotInParallel]
public class SceneCommandFunctionIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private IMUSHCodeParser CommandParser => WebAppFactory.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactory.FunctionParser;
	private IConnectionService Connection => WebAppFactory.Services.GetRequiredService<IConnectionService>();

	private const string God = "#1";

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText().Trim();

	private async Task Cmd(string command) =>
		await CommandParser.CommandParse(1, Connection, MModule.single(command));

	/// <summary>Creates a fresh scene owned by God via the wizard-only side-effect function; returns its id.</summary>
	private async Task<string> CreateSceneAsync(string title) =>
		await Eval($"scenecreate(,{God},{title} {Guid.NewGuid():N})");

	[Test]
	public async Task SceneCommand_Set_MutatesStatus_ReadableViaFunction()
	{
		var sceneId = await CreateSceneAsync("Phase3 set");
		// Make it public so the read functions' visibility check lets anyone read it.
		await Cmd($"@scene/set {sceneId}/public=1");

		// Status starts at the create default.
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("new");

		// @scene/set drives the status to active...
		await Cmd($"@scene/set {sceneId}/status=active");

		// ...and the read function sees it.
		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("active");
	}

	[Test]
	public async Task SceneFunctions_ReadPosesAndContent()
	{
		var sceneId = await CreateSceneAsync("Phase3 poses");
		await Cmd($"@scene/set {sceneId}/public=1");

		var poseId = await Eval($"sceneaddpose({sceneId},{God},,{God},pose,,hello phase3 pose)");
		await Assert.That(poseId).DoesNotStartWith("#-1");

		// sceneposes lists the scene's pose ids (space-joined).
		var posesList = await Eval($"sceneposes({sceneId})");
		await Assert.That(posesList).IsNotEmpty();
		await Assert.That(posesList).DoesNotStartWith("#-1");
		await Assert.That(posesList).Contains(poseId);

		// scenepose returns the requested field of one pose.
		await Assert.That(await Eval($"scenepose({sceneId}, {poseId}, content)")).Contains("hello phase3 pose");
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
		var sceneId = await CreateSceneAsync("Phase3 list");
		await Cmd($"@scene/set {sceneId}/public=1");

		var listed = await Eval("scenelist(recent)");
		await Assert.That(listed).DoesNotStartWith("#-1");
		await Assert.That(listed).Contains(sceneId);
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

		var a = await CreateSceneAsync("SeqA");
		var b = await CreateSceneAsync("SeqB");
		await Assert.That(int.TryParse(a.Split(':')[^1], out _)).IsTrue()
			.Because("scene ids must be 1-based counter values, not GUIDs or large HLC keys");
		await Assert.That(IdSeq(b)).IsEqualTo(IdSeq(a) + 1)
			.Because("scene ids increment by a 1-based counter");

		var p1 = await Eval($"sceneaddpose({b},{God},,{God},pose,,one)");
		var p2 = await Eval($"sceneaddpose({b},{God},,{God},pose,,two)");
		await Assert.That(int.TryParse(p1.Split(':')[^1], out _)).IsTrue()
			.Because("pose ids must be 1-based counter values");
		await Assert.That(IdSeq(p2)).IsEqualTo(IdSeq(p1) + 1)
			.Because("pose ids increment by a 1-based counter");
	}
}
