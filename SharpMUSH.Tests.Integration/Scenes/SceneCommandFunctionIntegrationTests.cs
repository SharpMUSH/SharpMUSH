using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
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
		(await FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message.ToPlainText().Trim();

	private async Task<MString> EvalMarkup(string expression) =>
		(await FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message;

	private async Task Cmd(string command) =>
		await CommandParser.CommandParse(1, Connection, MarkupText.Plain(command));

	/// <summary>Creates a fresh scene owned by God via the wizard-only side-effect function; returns its id.</summary>
	private async Task<string> CreateSceneAsync(string title) =>
		await Eval($"scenecreate(,{God},{title} {Guid.NewGuid():N})");

	[Test]
	public async Task SceneCommand_Set_MutatesStatus_ReadableViaFunction()
	{
		var sceneId = await CreateSceneAsync("Phase3 set");
		// Make it public so the read functions' visibility check lets anyone read it.
		await Cmd($"@scene/set {sceneId}/public=1");

		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("new");

		await Cmd($"@scene/set {sceneId}/status=active");

		await Assert.That(await Eval($"scene({sceneId}, status)")).IsEqualTo("active");
	}

	[Test]
	public async Task SceneFunctions_ReadPosesAndContent()
	{
		var sceneId = await CreateSceneAsync("Phase3 poses");
		await Cmd($"@scene/set {sceneId}/public=1");

		var poseId = await Eval($"sceneaddpose({sceneId},{God},,{God},ic,pose,,hello phase3 pose)");
		await Assert.That(poseId).DoesNotStartWith("#-1");

		var posesList = await Eval($"sceneposes({sceneId})");
		await Assert.That(posesList).IsNotEmpty();
		await Assert.That(posesList).DoesNotStartWith("#-1");
		await Assert.That(posesList).Contains(poseId);

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
	/// Service-level: scene and pose ids come from 1-based sequential counters, and are bare numbers in
	/// all cases.
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

		var p1 = await Eval($"sceneaddpose({b},{God},,{God},ic,pose,,one)");
		var p2 = await Eval($"sceneaddpose({b},{God},,{God},ic,pose,,two)");
		await Assert.That(int.TryParse(p1.Split(':')[^1], out _)).IsTrue()
			.Because("pose ids must be 1-based counter values");
		await Assert.That(IdSeq(p2)).IsEqualTo(IdSeq(p1) + 1)
			.Because("pose ids increment by a 1-based counter");
	}

	/// <summary>
	/// A pose written through the side-effect function keeps its colour, as one written through
	/// <c>@scene/addpose</c> does: storage takes the content as a serialised MString, and handing it the
	/// plain text stored the same markup an uncoloured pose has.
	///
	/// <para>Compared with an uncoloured pose's markup, not with <c>content</c>: storage serialises plain
	/// text too, so markup never equals content and that comparison proves nothing.</para>
	/// </summary>
	[Test]
	public async Task SceneAddPose_And_SceneEditPose_KeepTheirColour()
	{
		var sceneId = await CreateSceneAsync("Colour");
		await Cmd($"@scene/set {sceneId}/public=1");
		var plainId = await Eval($"sceneaddpose({sceneId},{God},,{God},ic,pose,,A red ember.)");
		var plainMarkup = await Eval($"scenepose({sceneId},{plainId},markup)");

		var poseId = await Eval($"sceneaddpose({sceneId},{God},,{God},ic,pose,,A [ansi(hr,red)] ember.)");
		await Assert.That(await Eval($"scenepose({sceneId},{poseId},content)")).IsEqualTo("A red ember.");
		await Assert.That(await Eval($"scenepose({sceneId},{poseId},markup)")).IsNotEqualTo(plainMarkup)
			.Because("the markup of an uncoloured pose means the colour never reached storage");

		await Eval($"sceneeditpose({plainId},{God},A [ansi(hr,red)] ember.)");
		await Assert.That(await Eval($"scenepose({sceneId},{plainId},content)")).IsEqualTo("A red ember.");
		await Assert.That(await Eval($"scenepose({sceneId},{plainId},markup)")).IsNotEqualTo(plainMarkup)
			.Because("an edit through the function must keep its colour too");
	}

	/// <summary>
	/// <c>scenepose(..., content)</c> is the pose as it was written, not its plain text, so a recall prints
	/// the colour, the box and the picture the room saw. It used to answer the plain column, and
	/// <c>+scene/recall</c> showed a <c>box(figure())</c> pose as box art around the picture's description.
	/// </summary>
	[Test]
	public async Task ScenePoseContent_KeepsColourBoxesAndPictures()
	{
		var sceneId = await CreateSceneAsync("Written");
		await Cmd($"@scene/set {sceneId}/public=1");

		var colourId = await Eval($"sceneaddpose({sceneId},{God},,{God},ic,pose,,A [ansi(hr,red)] ember.)");
		var colour = await EvalMarkup($"scenepose({sceneId},{colourId},content)");
		await Assert.That(colour.ToPlainText()).IsEqualTo("A red ember.");
		await Assert.That(colour.Render(MarkupFormat.Ansi)).IsNotEqualTo("A red ember.")
			.Because("the content a recall prints must keep the pose's colour");

		var pictureId = await Eval($"sceneaddpose({sceneId},{God},,{God},ic,emit,,[box(figure(https://example.com/cat.png,A cat))])");
		var picture = MarkupTextSerializer.Serialize(await EvalMarkup($"scenepose({sceneId},{pictureId},content)"));
		await Assert.That(picture).Contains("\"t\":\"frame\"")
			.Because("the box must come back as a box, not as the text art drawn from it");
		await Assert.That(picture).Contains("https://example.com/cat.png")
			.Because("the picture must come back with its address");
	}

	/// <summary>
	/// The side-effect functions broadcast their pose writes on <c>game.scene.{id}</c>, as the
	/// <c>@scene</c> switches do — which is what the design promises, and what makes a pose that softcode
	/// records or edits through these functions appear live.
	/// </summary>
	[Test]
	public async Task SceneFunctions_BroadcastTheirPoseWrites()
	{
		var sceneId = await CreateSceneAsync("Broadcast");
		var godObjid = await Eval($"objid({God})");

		await using var nats = new NatsConnection(new NatsOpts
		{
			Url = $"nats://localhost:{WebAppFactory.NatsTestServer.Instance.GetMappedPublicPort(4222)}"
		});
		await using var events = await nats.SubscribeCoreAsync<string>($"game.scene.{sceneId}");
		await nats.PingAsync();

		var first = await Eval($"sceneaddpose({sceneId},{God},,{God},ic,pose,,first)");
		var second = await Eval($"sceneaddpose({sceneId},{God},,{God},ic,pose,,second)");
		await Eval($"sceneeditpose({first},{God},first again)");
		await Eval($"scenemovepose({second})");
		await Eval($"scenedelpose({first})");

		var seen = new List<(string EventType, string PoseId, string? ActorObjId)>();
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await foreach (var message in events.Msgs.ReadAllAsync(timeout.Token))
		{
			if (message.Data is not { } data) continue;
			var root = JsonDocument.Parse(data).RootElement;
			seen.Add((root.GetProperty("EventType").GetString()!, root.GetProperty("PoseId").GetString()!,
				root.GetProperty("ActorObjId").GetString()));
			if (seen.Count == 5) break;
		}

		await Assert.That(seen.Select(e => (e.EventType, e.PoseId))).IsEquivalentTo(
			[("pose", first), ("pose", second), ("edit", first), ("move", second), ("delete", first)]);
		await Assert.That(seen.All(e => e.ActorObjId == godObjid)).IsTrue();
	}

	/// <summary>
	/// Undo and redo change which edit a pose shows, so a live view has to hear them: both paths — the
	/// <c>sceneundo</c>/<c>sceneredo</c> functions and the <c>@scene/undo</c>/<c>/redo</c> switches —
	/// broadcast an <c>edit</c> carrying the pose's now-current text.
	/// </summary>
	[Test]
	public async Task SceneUndoAndRedo_BroadcastAnEdit_InBothPaths()
	{
		var sceneId = await CreateSceneAsync("UndoRedo");
		var poseId = await Eval($"sceneaddpose({sceneId},{God},,{God},ic,pose,,before)");
		await Eval($"sceneeditpose({poseId},{God},after)");

		await using var nats = new NatsConnection(new NatsOpts
		{
			Url = $"nats://localhost:{WebAppFactory.NatsTestServer.Instance.GetMappedPublicPort(4222)}"
		});
		await using var events = await nats.SubscribeCoreAsync<string>($"game.scene.{sceneId}");
		await nats.PingAsync();

		await Eval($"sceneundo({poseId})");
		await Eval($"sceneredo({poseId})");
		await Cmd($"@scene/undo {poseId}");
		await Cmd($"@scene/redo {poseId}");

		var seen = new List<(string EventType, string PoseId, string Content)>();
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await foreach (var message in events.Msgs.ReadAllAsync(timeout.Token))
		{
			if (message.Data is not { } data) continue;
			var root = JsonDocument.Parse(data).RootElement;
			seen.Add((root.GetProperty("EventType").GetString()!, root.GetProperty("PoseId").GetString()!,
				root.GetProperty("Content").GetString()!));
			if (seen.Count == 4) break;
		}

		await Assert.That(seen).IsEquivalentTo(
			[("edit", poseId, "before"), ("edit", poseId, "after"), ("edit", poseId, "before"), ("edit", poseId, "after")]);
	}
}
