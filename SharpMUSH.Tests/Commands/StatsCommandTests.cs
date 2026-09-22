using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@stats</c> is <c>cmd_stats</c> (<c>src/cmds.c:1472</c>): the object count is <c>do_stats</c>
/// (<c>src/wiz.c:748</c>), <c>/flags</c> is <c>flag_stats</c> (<c>src/flags.c:1136</c>),
/// <c>/tables</c> is <c>do_list_memstats</c> (<c>src/game.c:2663</c>) and the other four report
/// Penn's chunk allocator. The command is <c>CMD_T_ANY</c> with no lock (<c>src/command.c:300</c>),
/// so a mortal reaches every switch; only counting another player's objects needs Search_All.
/// </summary>
public partial class StatsCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[GeneratedRegex(@"^(\d+) objects = (\d+) rooms, (\d+) exits, (\d+) things, (\d+) players\.$")]
	private static partial Regex CountLine();

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	private Task<TestIsolationHelpers.TestPlayer> Mortal(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	/// <summary>
	/// <c>do_stats</c> counts only the named player's objects — the player itself included, since a
	/// player owns itself — and says so in one line. Printing the name over a count of the whole
	/// world reports another player's figures as this one's.
	/// </summary>
	[Test]
	public async ValueTask NamedPlayer_CountsOnlyThatPlayersObjects()
	{
		var owner = await Mortal("StatsOwner");
		await Parser.CommandParse(owner.Handle, ConnectionService, MarkupText.Plain("@create StatsThingA"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MarkupText.Plain("@create StatsThingB"));

		var wizard = await Mortal("StatsWiz");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		var messages = await MessagesWhile(wizard.DbRef,
			async () => await Parser.CommandParse(wizard.Handle, ConnectionService, MarkupText.Plain($"@stats {owner.Name}")));

		await Assert.That(messages).Contains("3 objects = 0 rooms, 0 exits, 2 things, 1 players.");
	}

	/// <summary>With no argument the count covers every object, one figure per type that sums to the total.</summary>
	[Test]
	public async ValueTask NoArgument_CountsTheWholeWorld()
	{
		// Anyone may count the world; a player of its own keeps other tests' @stats out of the window.
		var counter = await Mortal("StatsWorld");
		var messages = await MessagesWhile(counter.DbRef,
			async () => await Parser.CommandParse(counter.Handle, ConnectionService, MarkupText.Plain("@stats")));

		var line = messages.Select(m => CountLine().Match(m)).Single(m => m.Success);
		var figures = Enumerable.Range(1, 5).Select(i => int.Parse(line.Groups[i].Value)).ToArray();
		await Assert.That(figures[0]).IsEqualTo(figures[1] + figures[2] + figures[3] + figures[4]);
		await Assert.That(figures[1]).IsGreaterThan(0).Because("the world always has room #0");
		await Assert.That(figures[4]).IsGreaterThan(0).Because("the world always has God");
	}

	/// <summary>
	/// <c>if (!Search_All(player)) { if (owner != ANY_OWNER &amp;&amp; owner != player) … "You need a search
	/// warrant to do that!" }</c>: a mortal may count their own objects, or the world's, but not
	/// another player's.
	/// </summary>
	[Test]
	public async ValueTask Mortal_CannotCountAnotherPlayersObjects()
	{
		var mortal = await Mortal("StatsMortal");
		var other = await Mortal("StatsOther");

		var messages = await MessagesWhile(mortal.DbRef,
			async () => await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@stats {other.Name}")));

		await Assert.That(messages).Contains("You need a search warrant to do that!");
		await Assert.That(messages.Any(m => CountLine().IsMatch(m))).IsFalse();
	}

	[Test]
	public async ValueTask Mortal_CanCountTheirOwnObjects()
	{
		var mortal = await Mortal("StatsSelf");
		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain("@create StatsSelfThing"));

		var messages = await MessagesWhile(mortal.DbRef,
			async () => await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain("@stats me")));

		await Assert.That(messages).Contains("2 objects = 0 rooms, 0 exits, 1 things, 1 players.");
	}

	[Test]
	public async ValueTask UnknownPlayer_SaysSo()
	{
		var asker = await Mortal("StatsUnknown");
		var messages = await MessagesWhile(asker.DbRef,
			async () => await Parser.CommandParse(asker.Handle, ConnectionService, MarkupText.Plain("@stats NoSuchStatsPlayer")));

		await Assert.That(messages).Contains("NoSuchStatsPlayer: No such player.");
	}

	/// <summary>
	/// <c>flag_stats</c> reports each flagspace in turn. The table size is the number of definitions,
	/// which here is what the flag and power stores hold; the flagset figures come from the objects.
	/// Penn's slab and hash-bucket lines describe its own memory layout and have no counterpart.
	/// </summary>
	[Test]
	public async ValueTask Flags_ReportsEachFlagspaceFromItsStore()
	{
		var mortal = await Mortal("StatsFlags");
		var flagCount = await Mediator.CreateStream(new GetAllObjectFlagsQuery()).CountAsync();
		var powerCount = await Mediator.CreateStream(new GetPowersQuery()).CountAsync();

		var messages = await MessagesWhile(mortal.DbRef,
			async () => await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain("@stats/flags")));

		await Assert.That(messages).Contains("Stats for flagspace FLAG:");
		await Assert.That(messages).Contains($"  {flagCount} entries in flag table.");
		await Assert.That(messages).Contains("Stats for flagspace POWER:");
		await Assert.That(messages).Contains($"  {powerCount} entries in flag table.");
		await Assert.That(messages.Any(m => m.EndsWith("objects share the most common set of flags."))).IsTrue();
		await Assert.That(messages.Any(m => m.EndsWith("objects have unique flagsets."))).IsTrue();
	}

	/// <summary>
	/// <c>do_list_memstats</c> lists Penn's hash tables by entry count. The same tables exist here —
	/// functions, @functions, commands, flags, powers, attribute definitions, config options,
	/// connections — each held by its own service, and each row's figure is that service's count.
	/// </summary>
	[Test]
	public async ValueTask Tables_ReportsEntryCountsFromTheOwningServices()
	{
		var mortal = await Mortal("StatsTables");
		var flagCount = await Mediator.CreateStream(new GetAllObjectFlagsQuery()).CountAsync();
		var attributeCount = await Mediator.CreateStream(new GetAllAttributeEntriesQuery()).CountAsync();

		var messages = await MessagesWhile(mortal.DbRef,
			async () => await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain("@stats/tables")));

		static int Row(List<string> lines, string table)
			=> lines.Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
				.Where(cells => cells.Length == 2 && cells[0] == table)
				.Select(cells => int.Parse(cells[1]))
				.DefaultIfEmpty(-1).First();

		await Assert.That(Row(messages, "Flags")).IsEqualTo(flagCount);
		await Assert.That(Row(messages, "Attributes")).IsEqualTo(attributeCount);
		await Assert.That(Row(messages, "Functions")).IsGreaterThan(100);
		await Assert.That(Row(messages, "Commands")).IsGreaterThan(100);
		await Assert.That(Row(messages, "@Functions")).IsGreaterThanOrEqualTo(0);
		await Assert.That(Row(messages, "Connections")).IsGreaterThan(0).Because("the mortal's own connection is one");
	}

	/// <summary>
	/// The chunk switches describe Penn's attribute-chunk allocator (<c>src/chunk.c</c>). SharpMUSH
	/// keeps attributes in the database provider, which has no such allocator, so these say that
	/// rather than printing a number that measures something else.
	/// </summary>
	[Test]
	[Arguments("chunks")]
	[Arguments("regions")]
	[Arguments("paging")]
	[Arguments("freespace")]
	public async ValueTask ChunkSwitches_SayTheyHaveNoEquivalent(string sw)
	{
		var god = WebAppFactoryArg.ExecutorDBRef;
		CallState? result = null;
		var messages = await MessagesWhile(god,
			async () => result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@stats/{sw}")));

		await Assert.That(messages.Any(m => m.StartsWith($"@stats/{sw}: SharpMUSH has no chunk allocator"))).IsTrue();
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.ErrorNotSupported);
	}
}
