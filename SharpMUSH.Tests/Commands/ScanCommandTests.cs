using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using TUnit.Assertions.Enums;

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
public class ScanCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>Read from config rather than hard-coded: the branch under test reads it from there too.</summary>
	private int MasterRoom => (int)WebAppFactoryArg.Services
		.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Database.MasterRoom;

	/// <summary>A player with a connection handle, standing in a room they own.</summary>
	private async Task<(TestIsolationHelpers.TestPlayer Player, string Room, string Word)> ScannerAsync(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

		var digResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName($"{prefix}Room")}"));
		var room = digResult.Message.ToPlainText().Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={room}"));

		// Players and rooms are created NO_COMMAND, so neither is scanned until the flag comes off.
		// @scan reports what would match, and nothing on a NO_COMMAND object ever does.
		await TestIsolationHelpers.ClearNoCommandAsync(Parser, ConnectionService, player.DbRef);
		await TestIsolationHelpers.ClearNoCommandAsync(Parser, ConnectionService, DBRef.Parse(room));

		return (player, room, TestIsolationHelpers.GenerateUniqueName(prefix.ToLowerInvariant()));
	}

	private async Task<string> ScanAsync(TestIsolationHelpers.TestPlayer player, string command)
	{
		var result = await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		return result.Message.ToPlainText();
	}

	/// <summary>The lines <paramref name="player"/> was notified of by one <c>@scan</c>, in order.</summary>
	private async Task<List<string>> ScanOutputAsync(TestIsolationHelpers.TestPlayer player, string command)
	{
		var offset = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		return [.. WebAppFactoryArg.Notifications.For(player.DbRef).Skip(offset)];
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
			MarkupText.Plain($"&CMD_HERE {room}=${word} *:think here"));

		await Assert.That(await ScanAsync(player, $"@scan {word} test"))
			.Contains($"#{DBRef.Parse(room).Number}/CMD_HERE");

		await Assert.That(await ScanAsync(player, $"@scan/self {word} test"))
			.DoesNotContain("CMD_HERE")
			.Because("the location is the ROOM scope's business, not SELF's");
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
			MarkupText.Plain($"&CMD_SELF me=${word} *:think self"));

		await Assert.That(await ScanAsync(player, $"@scan/self {word} test"))
			.Contains($"#{player.DbRef.Number}/CMD_SELF");

		await Assert.That(await ScanAsync(player, $"@scan {word}"))
			.DoesNotContain("CMD_SELF")
			.Because("the pattern is \"<word> *\" and a bare word does not match it");
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
			MarkupText.Plain($"@create {TestIsolationHelpers.GenerateUniqueName("ScanGlobalObj")}"));
		var global = createResult.Message.ToPlainText().Trim();
		await TestIsolationHelpers.ClearNoCommandAsync(Parser, ConnectionService, DBRef.Parse(global));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {global}=#{MasterRoom}"));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&CMD_GLOBAL {global}=${word} *:think global"));

		var expected = $"#{DBRef.Parse(global).Number}/CMD_GLOBAL";
		await Assert.That(await ScanAsync(player, $"@scan/globals {word} test")).Contains(expected);
		await Assert.That(await ScanAsync(player, $"@scan {word} test")).Contains(expected)
			.Because("the no-switch default includes GLOBALS");
		await Assert.That(await ScanAsync(player, $"@scan/room {word} test"))
			.DoesNotContain("CMD_GLOBAL")
			.Because("the master room is out of scope for /room");
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
			MarkupText.Plain($"@create {TestIsolationHelpers.GenerateUniqueName("ScanZoneObj")}"));
		var zone = createResult.Message.ToPlainText().Trim();
		await TestIsolationHelpers.ClearNoCommandAsync(Parser, ConnectionService, DBRef.Parse(zone));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&CMD_ZONE {zone}=${word} *:think zone"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone {room}={zone}"));

		await Assert.That(await ScanAsync(player, $"@scan/zone {word} test"))
			.Contains($"#{DBRef.Parse(zone).Number}/CMD_ZONE");

		await Assert.That(await ScanAsync(player, $"@scan/room {word} test"))
			.DoesNotContain("CMD_ZONE")
			.Because("a zone object is not in the room, so /room must not reach it");
	}

	/// <summary>
	/// #1355. <c>do_scan</c> prints a section's heading before it looks, not after it finds something
	/// (<c>src/game.c:1891</c>, <c>:1912</c>, <c>:1988</c>), so a scan that matches nothing still tells
	/// the player where it looked. SharpMUSH printed nothing at all.
	///
	/// <para>The expected lines are what a live PennMUSH 1.8.8 (<c>pennmush/</c> @ 80a1d5b9) printed for
	/// <c>@scan RandomGuid</c> from a room with no zone: the three unconditional headings, in that
	/// order, and nothing else. The zone headings are absent because an unzoned location has no zone
	/// master room to name.</para>
	/// </summary>
	[Test]
	public async Task Scan_PrintsEverySectionHeadingWhenNothingMatches()
	{
		var (player, _, word) = await ScannerAsync("ScanHeadings");

		await Assert.That(await ScanOutputAsync(player, $"@scan {word} test")).IsEquivalentTo(
		[
			"Matches on contents of this room:",
			"Matches on carried objects:",
			"Matches on objects in the Master Room:"
		], CollectionOrdering.Matching);
	}

	/// <summary>
	/// Each switch prints its own section's heading and no other's (<c>cmd_scan</c>,
	/// <c>src/cmds.c:1367-1383</c>, maps ROOM to CHECK_NEIGHBORS|CHECK_HERE, SELF to
	/// CHECK_INVENTORY|CHECK_SELF, GLOBALS to CHECK_GLOBAL). CHECK_HERE and CHECK_SELF have no heading
	/// of their own, so <c>/room</c> and <c>/self</c> print exactly one line each.
	/// </summary>
	[Test]
	[Arguments("room", "Matches on contents of this room:")]
	[Arguments("self", "Matches on carried objects:")]
	[Arguments("globals", "Matches on objects in the Master Room:")]
	public async Task Scan_PrintsOnlyTheHeadingOfTheSwitchedSection(string @switch, string heading)
	{
		var (player, _, word) = await ScannerAsync("ScanSwitchHeading");

		await Assert.That(await ScanOutputAsync(player, $"@scan/{@switch} {word} test"))
			.IsEquivalentTo([heading], CollectionOrdering.Matching);
	}

	/// <summary>
	/// <c>/zone</c> prints no heading when the location has no zone: both zone blocks are guarded on
	/// the zone existing, and the heading sits inside the guard (<c>src/game.c:1931-1981</c>).
	/// </summary>
	[Test]
	public async Task Scan_PrintsNoZoneHeadingWhenTheLocationHasNoZone()
	{
		var (player, _, word) = await ScannerAsync("ScanNoZone");

		await Assert.That(await ScanOutputAsync(player, $"@scan/zone {word} test")).IsEmpty();
	}

	/// <summary>
	/// A zone master room gets its own heading, and the objects under it are reported in <c>do_scan</c>'s
	/// <c>"%s  [%d:%s]"</c> shape - two spaces, the count of matching attributes, then each match as
	/// <c>" #&lt;dbref&gt;/&lt;ATTR&gt;"</c> (<c>src/game.c:1937-1944</c>, <c>src/attrib.c:1990-2000</c>).
	/// A live 1.8.8 printed, for a thing with two matching commands:
	/// <c>ScanBox(#3T)  [2: #3/CMD_A #3/CMD_B]</c>.
	/// </summary>
	[Test]
	public async Task Scan_ReportsOneLinePerObjectCountingItsMatchingAttributes()
	{
		var (player, _, word) = await ScannerAsync("ScanCount");

		var createResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@create {TestIsolationHelpers.GenerateUniqueName("ScanCountObj")}"));
		var box = createResult.Message.ToPlainText().Trim();
		await TestIsolationHelpers.ClearNoCommandAsync(Parser, ConnectionService, DBRef.Parse(box));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&CMD_A {box}=${word} *:think a"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&CMD_B {box}=${word} *:think b"));

		var number = DBRef.Parse(box).Number;
		var lines = await ScanOutputAsync(player, $"@scan/self {word} test");

		await Assert.That(lines).Count().IsEqualTo(2);
		await Assert.That(lines[0]).IsEqualTo("Matches on carried objects:");
		await Assert.That(lines[1]).EndsWith($"  [2: #{number}/CMD_A #{number}/CMD_B]");
	}

	/// <summary>
	/// The location object's match has no heading of its own and reads "Matched here: …"
	/// (<c>src/game.c:1905</c>); the executor's reads "Matched self: …" (<c>:1926</c>). With the default
	/// switch set the executor is also in the room's contents, so <c>do_scan</c> reports it twice - once
	/// under the room heading, once as "Matched self:" - and a live 1.8.8 prints both lines.
	/// </summary>
	[Test]
	public async Task Scan_LabelsTheLocationAndTheExecutorWithTheirOwnWording()
	{
		var (player, room, word) = await ScannerAsync("ScanLabels");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&CMD_HERE {room}=${word} *:think here"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&CMD_ME me=${word} *:think me"));

		var roomRef = DBRef.Parse(room);
		var lines = await ScanOutputAsync(player, $"@scan {word} test");

		var self = $"{await UnparseAsync(player.DbRef, player.DbRef)}  [1: #{player.DbRef.Number}/CMD_ME]";
		var here = $"{await UnparseAsync(roomRef, player.DbRef)}  [1: #{roomRef.Number}/CMD_HERE]";

		await Assert.That(lines).IsEquivalentTo(
		[
			"Matches on contents of this room:",
			self,
			$"Matched here: {here}",
			"Matches on carried objects:",
			$"Matched self: {self}",
			"Matches on objects in the Master Room:"
		], CollectionOrdering.Matching);
	}

	/// <summary>
	/// The <c>unparse_object</c> half of a match line: name, dbref and the flag symbols
	/// <paramref name="viewer"/> sees, CONNECTED among them for a connected player.
	/// </summary>
	private async Task<string> UnparseAsync(DBRef dbref, DBRef viewer)
	{
		var obj = await Mediator.Send(new GetObjectNodeQuery(dbref)) is AnySharpObject found
			? found.Object()
			: throw new InvalidOperationException($"#{dbref.Number} vanished mid-test");
		var looker = (await Mediator.Send(new GetObjectNodeQuery(viewer))).Expect<AnySharpObject>();

		return await MessageFormatting.FormatObjectWithDbref(obj, await FlagView.ForAsync(looker, ConnectionService));
	}
}
