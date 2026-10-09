using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// A picture posed into a scene is kept. <c>@emit [box(figure(&lt;url&gt;,Test))]</c> was stored as a box
/// around "Test": capture kept the box, but <c>figure()</c> had dropped the address, because only a
/// wizard or a Send_OOB holder could show a picture. The approved role now holds Send_Image.
/// </summary>
[NotInParallel]
public class ScenePictureCaptureIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private static readonly string Tag = Guid.NewGuid().ToString("N")[..8];

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private async Task God1(string command) =>
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	/// <summary>An approved player in a room of its own with the Scene Logger, focused on a running scene it started.</summary>
	private async Task<(long Handle, string SceneId)> PlayerInSceneAsync(string name)
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var logger = DBRef.Parse((await registry.GetPackageObjectsAsync("scene")).Single(o => o.Ref == "logger").Objid).ToString();

		var room = (await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {name}Room"))).Message.ToPlainText().Trim();
		await God1($"@pcreate {name}=pw-{Tag}-1");
		var player = await Eval($"pmatch({name})");
		await God1($"@role/assign {player}=approved");
		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, DBRef.Parse(player));
		await God1($"@tel {player}={room}");
		await God1($"@tel {logger}={room}");

		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"+scene/create {name} scene"));
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain("+scene/start"));
		var sceneId = await Eval($"get({player}/MY.SID)");
		return (handle, sceneId);
	}

	private async Task<string> EmitAndReadMarkupAsync(long handle, string sceneId, string emit)
	{
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"@emit {emit}"));
		return await Eval($"scenepose({sceneId},last(sceneposes({sceneId})),markup)");
	}

	[Test]
	public async Task AnApprovedPlayersEmittedFigureKeepsItsPicture()
	{
		var (handle, sceneId) = await PlayerInSceneAsync($"Pic_{Tag}");

		var markup = await EmitAndReadMarkupAsync(handle, sceneId, "[box(figure(https://example.com/seidre.gif,Test))]");

		await Assert.That(markup).Contains("\"t\":\"frame\"")
			.Because("the box is stored as a box");
		await Assert.That(markup).Contains("https://example.com/seidre.gif")
			.Because("an approved player holds Send_Image, so the figure keeps its picture");
	}
}
