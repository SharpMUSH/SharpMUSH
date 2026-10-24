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
/// scenelist()'s player arguments, above all <c>shared</c>: the scenes two players both belong to, which the relationships
/// card uses for "Last scene together". Each test makes its own two players and its own scenes.
/// </summary>
public class SceneListPlayerTests
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

	/// <summary>The scenes the first player and the second both belong to, as scenelist(shared) lists them.</summary>
	private Task<string> Shared() => Eval($"scenelist(shared,{First},{Second})");

	[Test]
	public async Task Shared_lists_only_the_scenes_both_belong_to()
	{
		var both = await SceneWith(First, Second);
		await SceneWith(First);
		var again = await SceneWith(First, Second);

		var shared = (await Shared()).Split(' ', StringSplitOptions.RemoveEmptyEntries);

		await Assert.That(shared).IsEquivalentTo(new[] { both, again });
	}

	[Test]
	public async Task Shared_with_one_name_starts_from_the_reader()
	{
		var both = await SceneWith(First, Second);

		await Assert.That(await Eval($"scenelist(shared,{Second})")).IsEqualTo(both)
			.Because("the reader is the first player");
	}

	[Test]
	public async Task Shared_is_empty_for_two_players_who_share_none()
	{
		await SceneWith(First);

		await Assert.That(await Shared()).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Mine_with_a_player_lists_every_scene_of_theirs()
	{
		var mine = await SceneWith(First);
		var shared = await SceneWith(First, Second);

		var listed = (await Eval($"scenelist(mine,{Second})")).Split(' ', StringSplitOptions.RemoveEmptyEntries);

		await Assert.That(listed).Contains(shared);
		await Assert.That(listed).DoesNotContain(mine);
	}

	[Test]
	public async Task Bad_arguments_are_errors()
	{
		await Assert.That(await Eval("scenelist(shared)")).IsEqualTo("#-1 NO SUCH PLAYER");
		await Assert.That(await Eval($"scenelist(shared,{First},*NoSuchPlayer{Guid.NewGuid():N})")).IsEqualTo("#-1 NO SUCH PLAYER");
		await Assert.That(await Eval($"scenelist(mine,*NoSuchPlayer{Guid.NewGuid():N})")).IsEqualTo("#-1 NO SUCH PLAYER");
		await Assert.That(await Eval("scenelist(scheduled,soon)")).IsEqualTo("#-1 BAD ARGUMENT FORMAT TO SCENELIST");
	}
}
