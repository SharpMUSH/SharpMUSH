using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A connection's command count and last signal follow the lines typed at it, as PennMUSH's <c>cmds</c>
/// does: the commands a <c>$</c>-command's body runs are not typed, and each stamp is a write to the
/// shared connection state.
/// </summary>
public class ConnectionActivityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task ATypedDollarCommandCountsOnceHoweverManyCommandsItRuns()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CmdCount");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var verb = TestIsolationHelpers.GenerateUniqueName("countme").ToLowerInvariant();
		var done = TestIsolationHelpers.GenerateUniqueName("CountDone");

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@set me=!no_command"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&CMD`COUNT me=${verb}:@pemit me=one;think two;@pemit me={done}"));
		var before = CommandCount(player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(verb));

		await WebAppFactoryArg.Notifications.WaitForAsync(player.DbRef, done);
		await Assert.That(CommandCount(player.Handle)).IsEqualTo(before + 1);
		await ConnectionService.Disconnect(player.Handle);
	}

	private int CommandCount(long handle) =>
		int.TryParse(ConnectionService.Get(handle)?.Metadata.GetValueOrDefault("CommandCount"), out var count) ? count : 0;
}
