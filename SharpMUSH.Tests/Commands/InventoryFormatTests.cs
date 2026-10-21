using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH's <c>do_inventory</c> (<c>look.c:1002</c>): an INVFORMAT on the player replaces the listing,
/// evaluated with %0 the carried dbrefs and %1 their names, '|'-separated.
/// </summary>
public class InventoryFormatTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<(TestIsolationHelpers.TestPlayer Player, IMUSHCodeParser Parser)> PlayerCarrying(string token)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "InvFmt");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@create Thing_{token}"));
		return (player, parser);
	}

	[Test]
	public async Task InvFormat_ReplacesTheListing()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("invfmt");
		var (player, parser) = await PlayerCarrying(token);

		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&INVFORMAT me=INV_{token}:[words(%0)]:[name(%0)]:[strmatch(%1,Thing_{token}*#*)]"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("inventory"));

		var heard = WebAppFactoryArg.Notifications.For(player.DbRef);
		await Assert.That(heard).Contains($"INV_{token}:1:Thing_{token}:1");
		await Assert.That(heard).DoesNotContain("You are carrying:");
	}

	[Test]
	public async Task EmptyInvFormat_ShowsNothing()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("invempty");
		var (player, parser) = await PlayerCarrying(token);

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&INVFORMAT me=[null(x)]"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("inventory"));

		var heard = WebAppFactoryArg.Notifications.For(player.DbRef);
		await Assert.That(heard).DoesNotContain("You are carrying:");
	}
}
