using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH's command_parse (<c>command.c:1168</c>): a line that starts with a function runs
/// WARN_ON_MISSING instead, unless that command is disabled, as the shipped restrict.cnf has it.
/// </summary>
public class WarnOnMissingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task DisabledByDefault_TheLineRuns()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("wom");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WarnOff");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"[pemit(me,RAN_{token})]"));

		await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef)).Contains($"RAN_{token}");
	}

	[Test]
	public async Task Enabled_WarnsTheOwnerInstead()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("wom");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WarnOn");

		// A command table of its own with WARN_ON_MISSING enabled, so the shared world's stays as it is.
		var functions = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Functions.Functions>(WebAppFactoryArg.Services);
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(WebAppFactoryArg.Services, functions);
		var parser = (MUSHCodeParser)WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle) with
		{
			CommandLibrary = commands.Get(),
			FunctionLibrary = functions.Get()
		};

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"[pemit(me,RAN_{token})]"));

		var heard = WebAppFactoryArg.Notifications.For(player.DbRef);
		await Assert.That(heard)
			.Contains($"No command found in code by #{player.DbRef.Number} - don't start code with functions.");
		await Assert.That(heard).DoesNotContain($"RAN_{token}");
	}
}
