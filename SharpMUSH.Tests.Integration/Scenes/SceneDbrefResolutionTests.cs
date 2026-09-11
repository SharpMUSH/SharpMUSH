using SharpMUSH.Library.Extensions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>Scene object references round-trip through the engine using a private player and room.</summary>
public class SceneDbrefResolutionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private IMUSHCodeParser FunctionParser => WebAppFactory.FunctionParserFor(_player.DbRef);

	private TestIsolationHelpers.TestPlayer _player = null!;
	private string _playerName = null!;
	private DBRef? _roomId;
	private string PlayerDbref => $"#{_player.DbRef.Number}";
	private IConnectionService Connections => WebAppFactory.Services.GetRequiredService<IConnectionService>();

	[Before(Test)]
	public async Task CreatePlayerAndRoom()
	{
		var mediator = WebAppFactory.Services.GetRequiredService<IMediator>();
		var objects = WebAppFactory.Services.GetRequiredService<IObjectStore>();
		_player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactory.Services, mediator, Connections, "SceneReference");
		var actor = (await objects.GetObjectNodeAsync(_player.DbRef)).Expect<SharpPlayer>();
		_playerName = actor.Object.Name;
		var roomId = await mediator.Send(new CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("SceneReferenceRoom"), actor));
		_roomId = roomId;
		var room = (await objects.GetObjectNodeAsync(roomId)).Expect<SharpRoom>();
		var origin = await actor.Location.WithCancellation(default);
		await mediator.Send(new MoveObjectCommand(actor, room, origin.Object().DBRef, IsSilent: true));
		await WebAppFactory.CommandParser.CommandParse(1, Connections,
			MarkupText.Plain($"@set {_player.DbRef}=WIZARD"));
	}

	[After(Test)]
	public async Task CleanUpPrivateObjects()
	{
		if (_player is null) return;
		await Connections.Disconnect(_player.Handle);
		var mediator = WebAppFactory.Services.GetRequiredService<IMediator>();
		// These private fixture objects have no remaining consumers after this test.
		if (_roomId is { } roomId) await mediator.Send(new DeleteObjectCommand(roomId));
		await mediator.Send(new DeleteObjectCommand(_player.DbRef));
	}

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText().Trim();

	private async Task<string> NewPublicSceneAsync(string title)
	{
		var id = await Eval($"scenecreate(,{PlayerDbref},{title} {Guid.NewGuid():N})");
		await Eval($"sceneset({id},public,1)");
		return id;
	}

	[Test]
	public async Task CreateScene_ResolvesOwnerDbref_BackToOwner()
	{
		var id = await NewPublicSceneAsync("Dbref owner");

		await Assert.That(await Eval($"scene({id}, ownername)")).IsNotEmpty();
		await Assert.That(await Eval($"scene({id}, owner)")).IsEqualTo(PlayerDbref);
	}

	[Test]
	public async Task AddPose_ResolvesAuthorDbref_BackToOwner()
	{
		var id = await NewPublicSceneAsync("Dbref author");
		var poseId = await Eval($"sceneaddpose({id},{PlayerDbref},,{PlayerDbref},pose,,dbref author check)");
		await Assert.That(poseId).DoesNotStartWith("#-1");

		await Assert.That(await Eval($"scenepose({id}, {poseId}, authorname)")).IsNotEmpty();
		await Assert.That(await Eval($"scenepose({id}, {poseId}, author)")).IsEqualTo(PlayerDbref);
	}

	[Test]
	public async Task AddMember_ResolvesMemberDbref_BackToOwner()
	{
		var id = await NewPublicSceneAsync("Dbref member");

		await Assert.That(await Eval($"sceneaddmember({id},{PlayerDbref},participant)")).IsEqualTo(PlayerDbref);

		await Assert.That(await Eval($"scenemembers({id})")).Contains(PlayerDbref);
	}
}
