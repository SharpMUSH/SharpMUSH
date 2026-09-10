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
public class SceneLocateArgumentTests
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
		var actor = (await objects.GetObjectNodeAsync(_player.DbRef)).AsPlayer;
		_playerName = actor.Object.Name;
		var roomId = await mediator.Send(new CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("SceneReferenceRoom"), actor));
		_roomId = roomId;
		var room = (await objects.GetObjectNodeAsync(roomId)).AsRoom;
		await mediator.Send(new MoveObjectCommand(actor, room, IsSilent: true));
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

	[Test]
	public async Task SceneCreate_ResolvesOwner_FromMeKeyword()
	{
		var id = await Eval($"scenecreate(,me,Locate owner {Guid.NewGuid():N})");
		await Assert.That(id).DoesNotStartWith("#-1");
		await Eval($"sceneset({id},public,1)");

		await Assert.That(await Eval($"scene({id}, owner)")).IsEqualTo(PlayerDbref);
	}

	[Test]
	public async Task SceneAddMember_ResolvesPlayer_FromMeKeyword()
	{
		var id = await Eval($"scenecreate(,{PlayerDbref},Locate member {Guid.NewGuid():N})");
		await Eval($"sceneset({id},public,1)");

		await Assert.That(await Eval($"sceneaddmember({id},me,participant)")).IsEqualTo(PlayerDbref);
		await Assert.That(await Eval($"scenemembers({id})")).Contains(PlayerDbref);
	}

	[Test]
	public async Task SceneFocus_ResolvesPlayer_FromMeKeyword()
	{
		var id = await Eval($"scenecreate(,{PlayerDbref},Locate focus {Guid.NewGuid():N})");
		await Eval($"sceneset({id},public,1)");

		await Eval($"sceneaddmember({id},me,participant)");
		await Eval($"scenesetfocus(me,{id})");
		await Assert.That(await Eval($"scenefocus(me)")).IsEqualTo(id);
	}

	[Test]
	public async Task SceneCreate_ResolvesOwner_FromPlayerName()
	{
		var id = await Eval($"scenecreate(,{_playerName},Locate name {Guid.NewGuid():N})");
		await Assert.That(id).DoesNotStartWith("#-1");
		await Eval($"sceneset({id},public,1)");

		await Assert.That(await Eval($"scene({id}, owner)")).IsEqualTo(PlayerDbref);
	}

	[Test]
	public async Task SceneWhere_ResolvesRoom_FromHereKeyword()
	{
		var id = await Eval($"scenecreate(here,{PlayerDbref},Locate here {Guid.NewGuid():N})");
		await Assert.That(id).DoesNotStartWith("#-1");
		await Eval($"sceneset({id},public,1)");
		await Eval($"sceneset({id},status,active)");

		await Assert.That(await Eval($"scenewhere(here)")).IsEqualTo(id);
	}
}
