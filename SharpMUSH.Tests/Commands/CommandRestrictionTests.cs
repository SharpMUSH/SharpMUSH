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

	// A leftover Guest power would count as a guest character in GuestLoginTests.
	[After(Test)]
	public async Task RevokeGrants()
	{
		foreach (var player in _players)
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {player}=!Guest"));
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
	[Arguments("@power {0}=Guest")]
	[Arguments("@set {0}=GAGGED")]
	public async Task RestrictedPlayer_CannotRename(string grant)
	{
		var player = await PlayerAsync("CmdRestricted", grant);
		var original = await NameOf(player.DbRef);

		var said = await Run(player, $"@name me={TestIsolationHelpers.GenerateUniqueName("Renamed")}");

		await Assert.That(await NameOf(player.DbRef)).IsEqualTo(original);
		await Assert.That(said.Select(delivery => delivery.Message)).Contains("Permission denied.");
	}

	[Test]
	public async Task Mortal_CanRename()
	{
		var player = await PlayerAsync("CmdMortal");
		var renamed = TestIsolationHelpers.GenerateUniqueName("Renamed");

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

		var said = (await Run(player, "think lset(me/Basic,no_inherit)")).Single(delivery => delivery.Sender == player.DbRef).Message;

		await Assert.That(said == ErrorMessages.Returns.PermissionDenied).IsEqualTo(denied);
	}
}
