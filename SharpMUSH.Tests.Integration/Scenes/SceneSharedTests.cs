using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// sceneshared(): the scenes two players both belong to, which the relationships card uses for "Last scene
/// together". Each test makes its own two players and its own scenes.
/// </summary>
public class SceneSharedTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private IMUSHCodeParser FunctionParser => WebAppFactory.FunctionParserFor(_first.DbRef);

	private TestIsolationHelpers.TestPlayer _first = null!;
	private TestIsolationHelpers.TestPlayer _second = null!;
	private IConnectionService Connections => WebAppFactory.Services.GetRequiredService<IConnectionService>();

	private string First => $"#{_first.DbRef.Number}";
	private string Second => $"#{_second.DbRef.Number}";

	[Before(Test)]
	public async Task CreatePlayers()
	{
		var mediator = WebAppFactory.Services.GetRequiredService<IMediator>();
		_first = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactory.Services, mediator, Connections, "SharedFirst");
		_second = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactory.Services, mediator, Connections, "SharedSecond");
		await WebAppFactory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@set {_first.DbRef}=WIZARD"));
	}

	[After(Test)]
	public async Task CleanUp()
	{
		var mediator = WebAppFactory.Services.GetRequiredService<IMediator>();
		foreach (var player in new[] { _first, _second }.Where(p => p is not null))
		{
			await Connections.Disconnect(player.Handle);
			await mediator.Send(new DeleteObjectCommand(player.DbRef));
		}
	}

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	/// <summary>A public scene owned by the first player, with <paramref name="members"/> joined.</summary>
	private async Task<string> SceneWith(params string[] members)
	{
		var id = await Eval($"scenecreate(,{First},Shared {Guid.NewGuid():N})");
		await Eval($"sceneset({id},public,1)");
		foreach (var member in members)
		{
			await Eval($"sceneaddmember({id},{member},participant)");
		}

		return id;
	}

	[Test]
	public async Task It_lists_only_the_scenes_both_belong_to()
	{
		var both = await SceneWith(First, Second);
		await SceneWith(First);
		var again = await SceneWith(First, Second);

		var shared = (await Eval($"sceneshared({First},{Second},10)")).Split(' ', StringSplitOptions.RemoveEmptyEntries);

		await Assert.That(shared).IsEquivalentTo(new[] { both, again });
	}

	[Test]
	public async Task It_gives_one_scene_unless_asked_for_more()
	{
		await SceneWith(First, Second);
		await SceneWith(First, Second);

		var one = await Eval($"sceneshared({First},{Second})");

		await Assert.That(one.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length).IsEqualTo(1);
	}

	[Test]
	public async Task It_is_empty_for_two_players_who_share_none()
	{
		await SceneWith(First);

		await Assert.That(await Eval($"sceneshared({First},{Second})")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task A_bad_count_or_an_unknown_player_is_an_error()
	{
		await Assert.That(await Eval($"sceneshared({First},{Second},0)")).IsEqualTo("#-1 BAD ARGUMENT FORMAT TO SCENESHARED");
		await Assert.That(await Eval($"sceneshared({First},*NoSuchPlayer{Guid.NewGuid():N})")).IsEqualTo("#-1 NO SUCH PLAYER");
	}
}
