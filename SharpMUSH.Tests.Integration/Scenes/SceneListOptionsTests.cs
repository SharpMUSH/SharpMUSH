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
/// scenelist()'s options object, above all <c>with</c>: the scenes two players both belong to, which the relationships
/// card uses for "Last scene together". Each test makes its own two players and its own scenes.
/// </summary>
public class SceneListOptionsTests
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

	/// <summary>The scenes of the first player's that the second also belongs to, as scenelist() with <c>with</c> lists them.</summary>
	private Task<string> Shared(string count = "") =>
		Eval($"scenelist(mine,json(object,player,json(string,{First}),with,json(string,{Second}){(count.Length == 0 ? "" : $",count,{count}")}))");

	[Test]
	public async Task With_lists_only_the_scenes_both_belong_to()
	{
		var both = await SceneWith(First, Second);
		await SceneWith(First);
		var again = await SceneWith(First, Second);

		var shared = (await Shared("10")).Split(' ', StringSplitOptions.RemoveEmptyEntries);

		await Assert.That(shared).IsEquivalentTo(new[] { both, again });
	}

	[Test]
	public async Task A_count_of_one_gives_the_latest()
	{
		await SceneWith(First, Second);
		var latest = await SceneWith(First, Second);

		await Assert.That(await Shared("1")).IsEqualTo(latest);
	}

	[Test]
	public async Task A_count_past_the_scan_takes_what_the_scan_finds()
	{
		var both = await SceneWith(First, Second);

		await Assert.That(await Shared(int.MaxValue.ToString())).IsEqualTo(both);
	}

	[Test]
	public async Task With_is_empty_for_two_players_who_share_none()
	{
		await SceneWith(First);

		await Assert.That(await Shared()).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Player_lists_every_scene_of_that_player()
	{
		var mine = await SceneWith(First);
		var shared = await SceneWith(First, Second);

		var listed = (await Eval($"scenelist(mine,json(object,player,json(string,{Second})))")).Split(' ', StringSplitOptions.RemoveEmptyEntries);

		await Assert.That(listed).Contains(shared);
		await Assert.That(listed).DoesNotContain(mine);
	}

	[Test]
	public async Task Bad_options_are_errors()
	{
		await Assert.That(await Shared("0")).IsEqualTo("#-1 COUNT MUST BE A POSITIVE WHOLE NUMBER");
		await Assert.That(await Eval($"scenelist(mine,json(object,with,json(string,*NoSuchPlayer{Guid.NewGuid():N})))")).IsEqualTo("#-1 NO SUCH PLAYER");
		await Assert.That(await Eval("scenelist(mine,json(object,colour,1))")).IsEqualTo("#-1 UNKNOWN OPTION COLOUR");
		await Assert.That(await Eval("scenelist(mine,json(array,1))")).IsEqualTo("#-1 OPTIONS MUST BE A JSON OBJECT");
		await Assert.That(await Eval("scenelist(scheduled,json(object,from,json(string,soon)))")).IsEqualTo("#-1 FROM MUST BE UTC MILLISECONDS");
	}
}
