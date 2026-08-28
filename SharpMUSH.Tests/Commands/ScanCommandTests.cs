using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The four places <c>do_scan</c> looks (<c>src/game.c:1873-1994</c>). Each of these covered a branch
/// of <c>@scan</c> that reported nothing at all: the location object, the scanning player, the master
/// room, and a zone that is not a Zone Master Room.
///
/// <para>Every test runs as its own player in its own dug room, so the shared God object and room #0
/// are never mutated - a stray <c>$</c>-command on #1 would be live for every other test in the
/// session.</para>
/// </summary>
[NotInParallel]
public class ScanCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>A player with a connection handle, standing in a room they own.</summary>
	private async Task<(TestIsolationHelpers.TestPlayer Player, string Room, string Word)> ScannerAsync(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

		var digResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"@dig {TestIsolationHelpers.GenerateUniqueName($"{prefix}Room")}"));
		var room = digResult.Message!.ToPlainText().Trim();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={room}"));

		return (player, room, TestIsolationHelpers.GenerateUniqueName(prefix.ToLowerInvariant()));
	}

	private async Task<string> ScanAsync(TestIsolationHelpers.TestPlayer player, string command)
	{
		var result = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single(command));
		return result.Message?.ToPlainText() ?? string.Empty;
	}

	/// <summary>
	/// <c>CHECK_HERE</c> (<c>src/game.c:1901</c>). Only <c>CHECK_NEIGHBORS</c> - the location's
	/// contents - was implemented, so a <c>$</c>-command on the room itself, which is where most of
	/// them live, was invisible to <c>@scan</c>.
	/// </summary>
	[Test]
	public async Task Scan_ReportsACommandOnTheLocationItself()
	{
		var (player, room, word) = await ScannerAsync("ScanHere");
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"&CMD_HERE {room}=${word} *:think here"));

		await Assert.That(await ScanAsync(player, $"@scan {word} test"))
			.Contains($"#{DBRef.Parse(room).Number}/CMD_HERE");
	}

	/// <summary>
	/// <c>CHECK_SELF</c> (<c>src/game.c:1922</c>). The SELF branch scanned only the executor's
	/// inventory, never the executor. Scanned with <c>/self</c> alone: under the default switch set the
	/// player is in their room's contents, so the neighbours pass covers them and this branch stays
	/// silent to avoid a duplicate (<c>scan_list</c>, <c>src/game.c:1763-1764</c>).
	/// </summary>
	[Test]
	public async Task Scan_ReportsACommandOnTheScanningPlayerItself()
	{
		var (player, _, word) = await ScannerAsync("ScanSelf");
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"&CMD_SELF me=${word} *:think self"));

		await Assert.That(await ScanAsync(player, $"@scan/self {word} test"))
			.Contains($"#{player.DbRef.Number}/CMD_SELF");
	}

	/// <summary>
	/// The switch is declared <c>GLOBALS</c> and the no-switch default supplies <c>GLOBALS</c>, but
	/// the branch tested for <c>GLOBAL</c> - and <c>@scan/global</c> is rejected as an invalid switch,
	/// so no spelling reached it. Asserted through both the explicit switch and the default.
	/// </summary>
	[Test]
	public async Task Scan_ReportsACommandOnAnObjectInTheMasterRoom()
	{
		var (player, _, word) = await ScannerAsync("ScanGlobal");

		// Owned by the player, so CanScan passes without needing VISUAL; moved by God, who controls
		// the master room.
		var createResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"@create {TestIsolationHelpers.GenerateUniqueName("ScanGlobalObj")}"));
		var global = createResult.Message!.ToPlainText().Trim();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {global}=#2"));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"&CMD_GLOBAL {global}=${word} *:think global"));

		var expected = $"#{DBRef.Parse(global).Number}/CMD_GLOBAL";
		await Assert.That(await ScanAsync(player, $"@scan/globals {word} test")).Contains(expected);
		await Assert.That(await ScanAsync(player, $"@scan {word} test")).Contains(expected)
			.Because("the no-switch default includes GLOBALS");
	}

	/// <summary>
	/// A zone that is not a room carries its <c>$</c>-commands itself; only a Zone Master <em>Room</em>
	/// holds them in its contents (<c>src/game.c:1936-1953</c>). This scanned the contents either way,
	/// so an ordinary zone object never matched.
	/// </summary>
	[Test]
	public async Task Scan_ReportsACommandOnARegularZoneObject()
	{
		var (player, room, word) = await ScannerAsync("ScanZone");

		var createResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"@create {TestIsolationHelpers.GenerateUniqueName("ScanZoneObj")}"));
		var zone = createResult.Message!.ToPlainText().Trim();

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"&CMD_ZONE {zone}=${word} *:think zone"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {room}={zone}"));

		await Assert.That(await ScanAsync(player, $"@scan/zone {word} test"))
			.Contains($"#{DBRef.Parse(zone).Number}/CMD_ZONE");
	}
}
