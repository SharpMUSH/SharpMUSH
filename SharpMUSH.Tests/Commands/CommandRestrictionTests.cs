using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH folds CMD_T_NOGUEST and CMD_T_NOGAGGED into every command's lock as
/// <c>!POWER^GUEST</c> and <c>!FLAG^GAGGED</c> (<c>src/command.c</c>), so a guest cannot run
/// <c>@name</c> and a gagged player cannot run <c>@name</c> or <c>@lset</c>. Guest is a power; there
/// is no GUEST flag.
/// </summary>
[NotInParallel(GuestLoginTests.GuestCharacters)]
public class CommandRestrictionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private readonly List<DBRef> _players = [];

	// A leftover Guest power would count as a guest character in GuestLoginTests; revoke both grants
	// so no player these tests made keeps a restriction.
	[After(Test)]
	public async Task RevokeGrants()
	{
		foreach (var player in _players)
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {player}=!Guest"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {player}=!GAGGED"));
		}
	}

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix, string? grant = null)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		_players.Add(player.DbRef);
		if (grant is not null) await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(grant.Replace("{0}", player.DbRef.ToString())));
		return player;
	}

	private async Task<string> NameOf(DBRef player)
		=> (await Mediator.Send(new GetObjectNodeQuery(player))).Expect<AnySharpObject>().Object().Name;

	// Test players share a room, so a player's bucket also holds other tests' room broadcasts; read
	// every delivery the command caused rather than whichever arrived last.
	private async Task<List<TestHelpers.NotificationRecorder.Delivery>> Run(TestIsolationHelpers.TestPlayer who, string command)
	{
		var before = WebAppFactoryArg.Notifications.DeliveryCountFor(who.DbRef);
		await Parser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command));
		return [.. WebAppFactoryArg.Notifications.DeliveriesFor(who.DbRef).Skip(before)];
	}

	[Test]
	[Arguments("@power {0}=Guest", "Permission denied. Guests cannot use @NAME.")]
	[Arguments("@set {0}=GAGGED", "Permission denied. You cannot use @NAME while gagged.")]
	public async Task RestrictedPlayer_CannotRename(string grant, string refusal)
	{
		var player = await PlayerAsync("CmdRestricted", grant);
		var original = await NameOf(player.DbRef);

		var said = await Run(player, $"@name me={$"Ren{Guid.NewGuid():N}"[..12]}");

		await Assert.That(await NameOf(player.DbRef)).IsEqualTo(original);
		await Assert.That(said.Select(delivery => delivery.Message)).Contains(refusal);
	}

	[Test]
	[Arguments("think hi", "THINK")]
	[Arguments("@create GagThing", "@CREATE")]
	[Arguments("@set me=GAGATTR:1", "@SET")]
	[Arguments("@pemit me=hello", "@PEMIT")]
	[Arguments("@teleport me=here", "@TELEPORT")]
	public async Task GaggedPlayer_CannotRunANoGaggedCommand(string command, string commandName)
	{
		var player = await PlayerAsync("CmdGagged", "@set {0}=GAGGED");

		var said = await Run(player, command);

		await Assert.That(said.Select(delivery => delivery.Message)).Contains($"Permission denied. You cannot use {commandName} while gagged.");
	}

	[Test]
	public async Task Mortal_CanRename()
	{
		var player = await PlayerAsync("CmdMortal");
		var renamed = $"Ren{Guid.NewGuid():N}"[..12];

		await Run(player, $"@name me={renamed}");

		await Assert.That(await NameOf(player.DbRef)).IsEqualTo(renamed);
	}

	[Test]
	[Arguments("@set {0}=GAGGED", true)]
	[Arguments("@power {0}=Guest", false)]
	[Arguments(null, false)]
	public async Task LsetFunction_FollowsTheAtLsetRestrictions(string? grant, bool denied)
	{
		var player = await PlayerAsync("CmdLset", grant);

		// Evaluated as the player rather than through think, which a GAGGED player may not run.
		var answer = (await WebAppFactoryArg.FunctionParserFor(player.DbRef).FunctionParse(MarkupText.Plain("lset(me/Basic,no_inherit)")))
			?.Message.ToPlainText();

		await Assert.That(answer == "#-1 PERMISSION DENIED: CANNOT USE @LSET").IsEqualTo(denied);
	}
}
