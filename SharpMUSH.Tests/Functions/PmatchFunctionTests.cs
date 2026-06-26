using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Regression for pmatch()'s global player resolution. pmatch must resolve a player by name
/// regardless of where they (or the looker) stand — the default profile http_handler matches the
/// requested character from the handler object, which is never co-located with that character.
/// A bug in LocateService dropped the global player match unless MatchObjectsInLookerInventory was
/// set (which pmatch does not), so pmatch returned #-1 for any non-co-located player.
/// </summary>
public class PmatchFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task Pmatch_ResolvesPlayerInADifferentRoom()
	{
		var target = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "Faraway");
		var node = await Mediator.Send(new GetObjectNodeQuery(target));
		await Assert.That(node.IsNone).IsFalse();
		var name = node.Known.Object().Name;

		// Separate the looker (#1) from the target: God in Room Zero, target in the Master Room (#2).
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("@tel me=#0"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@tel {target}=#2"));

		// Only a GLOBAL player-name match can resolve the target now.
		var result = (await FunctionParser.FunctionParse(MModule.single($"pmatch({name})")))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(target.ToString());
	}
}
