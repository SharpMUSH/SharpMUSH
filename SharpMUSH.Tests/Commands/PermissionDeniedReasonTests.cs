using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A refusal says why after "Permission denied.": the permission that would have let the player, the
/// object they do not control, or that only God may.
/// </summary>
public class PermissionDeniedReasonTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix)
		=> await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private async Task<List<string>> Run(TestIsolationHelpers.TestPlayer who, string command)
	{
		var before = WebAppFactoryArg.Notifications.DeliveryCountFor(who.DbRef);
		await Parser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command));
		return [.. WebAppFactoryArg.Notifications.DeliveriesFor(who.DbRef).Skip(before).Select(delivery => delivery.Message)];
	}

	[Test]
	public async Task CommandLock_NamesThePermissionsThatPassIt()
	{
		var mortal = await PlayerAsync("PdLock");

		var said = await Run(mortal, "@allhalt");

		await Assert.That(said).Contains("Permission denied. You need one of these permissions: game.wizard, game.halt.");
	}

	[Test]
	public async Task GodOnlyCommand_SaysOnlyGod()
	{
		var mortal = await PlayerAsync("PdGod");

		var said = await Run(mortal, "@logwipe/check");

		await Assert.That(said).Contains("Permission denied. Only God can do that.");
	}

	[Test]
	public async Task CommandLockOfSeveral_NamesEach()
	{
		var mortal = await PlayerAsync("PdWall");

		var said = await Run(mortal, "@wall hello");

		await Assert.That(said).Contains("Permission denied. You need one of these permissions: chat.admin, game.royalty, game.announce.");
	}

	[Test]
	public async Task NarrowedWall_NamesItsPermission()
	{
		var announcer = await PlayerAsync("PdWallWiz");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {announcer.DbRef}=Announce"));

		var said = await Run(announcer, "@wall/wizard hello");

		await Assert.That(said).Contains("Permission denied. You need the chat.admin permission.");
	}

	[Test]
	public async Task HaltingSomeoneElse_NamesTheObjectAndTheAlternatives()
	{
		var mortal = await PlayerAsync("PdHalter");
		var other = await PlayerAsync("PdHalted");
		var otherName = (await Mediator.Send(new GetObjectNodeQuery(other.DbRef))).Expect<AnySharpObject>().Object().Name;

		var said = await Run(mortal, $"@halt {other.DbRef}");

		await Assert.That(said).Contains(
			$"Permission denied. You do not control {otherName}, and you need one of these permissions: game.wizard, game.halt.");
	}

	[Test]
	[Arguments("PERM^server.operate", new[] { "server.operate" })]
	[Arguments("FLAG^WIZARD|POWER^SQL_OK", new[] { "game.wizard", "game.sql_ok" })]
	[Arguments("PERM^chat.admin|FLAG^ROYALTY|POWER^ANNOUNCE", new[] { "chat.admin", "game.royalty", "game.announce" })]
	[Arguments("(FLAG^ROYALTY|FLAG^WIZARD)", new[] { "game.royalty", "game.wizard" })]
	public async Task PermissionsOf_ReadsAPermissionOnlyLock(string commandLock, string[] expected)
		=> await Assert.That(CommandRestrictions.PermissionsOf(commandLock)).IsEquivalentTo(expected);

	[Test]
	[Arguments("")]
	[Arguments("(TYPE^PLAYER|TYPE^THING)&!FLAG^GAGGED")]
	[Arguments("FLAG^WIZARD|TYPE^PLAYER")]
	[Arguments("(FLAG^WIZARD)&(TYPE^PLAYER)")]
	[Arguments("(PERM^chat.admin)&(PERM^config.admin)")]
	[Arguments("#FALSE")]
	public async Task PermissionsOf_GivesNoneForAnyOtherLock(string commandLock)
		=> await Assert.That(CommandRestrictions.PermissionsOf(commandLock)).IsEmpty();
}
