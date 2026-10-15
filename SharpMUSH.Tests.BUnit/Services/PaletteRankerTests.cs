using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The command palette's ordering (docs/design/command-palette.md): the empty palette's groups, and
/// what typing ranks first.
/// </summary>
public class PaletteRankerTests
{
	private static readonly PaletteRow Wiki = PaletteRow.Go("/wiki", "icon", "Wiki");

	private static PaletteEntryView Entry(string name, string label, string line, PaletteScope scope = PaletteScope.Room,
		string system = "scene", string? context = null, bool always = false, IReadOnlyList<string>? when = null,
		bool urgent = false, PaletteAction action = PaletteAction.Run, string? keywords = null)
		=> new($"#10:1/{name}", label, line, action, scope, system, "Scene Logger", Context: context, Always: always,
			When: when, Urgent: urgent, Keywords: keywords);

	private static PaletteContextView Context(string name, double weight = 1, params (string Key, string Value)[] values)
		=> new(name, values.ToDictionary(v => v.Key, v => v.Value), weight);

	private static PaletteSnapshot Snapshot(IReadOnlyList<PaletteEntryView> entries,
		IReadOnlyList<PaletteContextView>? stack = null, IReadOnlyList<string>? pins = null,
		IReadOnlyList<string>? builtins = null, PaletteInputView? input = null, bool connected = false,
		string? page = null, IReadOnlyList<PaletteRow>? pageRows = null)
		=> new([Wiki], pageRows ?? [], new PaletteResponse(entries, input, builtins ?? [], pins ?? [], stack ?? [], 0, []),
			page, connected);

	private static IReadOnlyList<string> Names(IReadOnlyList<PaletteGroup> groups) => [.. groups.Select(group => group.Name)];

	[Test]
	public async Task TheEmptyPalette_PutsUrgentFirst_ThenPins_ThenWhatContextRaises_ThenStaples_ThenPages()
	{
		var defend = Entry("DEFEND", "Defend", "+defend", system: "combat", when: ["combat-turn"], urgent: true);
		var pose = Entry("POSE", "Pose in the scene", "+scene/pose <text>");
		var who = Entry("WHO", "Who is on", "+who", system: "who-where", always: true);
		var mail = Entry("MAIL", "Read mail", "+mail", system: "mail");
		var groups = PaletteRanker.Groups(Snapshot([defend, pose, who, mail],
			stack: [Context("combat-turn"), Context("scene", 0.5)], pins: [mail.Id]));

		await Assert.That(Names(groups)).IsEquivalentTo(
			[PaletteRanker.NowGroup, PaletteRanker.PinnedGroup, PaletteRanker.HereGroup, PaletteRanker.AlwaysGroup, PaletteRanker.GoGroup],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(groups[0].Rows.Single().Label).IsEqualTo("Defend");
		await Assert.That(groups[1].Rows.Single().Label).IsEqualTo("Read mail");
		await Assert.That(groups[2].Rows.Single().Label).IsEqualTo("Pose in the scene");
		await Assert.That(groups[3].Rows.Single().Label).IsEqualTo("Who is on");
	}

	[Test]
	public async Task ARowShowsOnce_InTheFirstGroupThatHasIt()
	{
		var who = Entry("WHO", "Who is on", "+who", system: "who-where", always: true);
		var groups = PaletteRanker.Groups(Snapshot([who], pins: [who.Id]));

		await Assert.That(groups.SelectMany(group => group.Rows).Count(row => row.Id == who.Id)).IsEqualTo(1);
		await Assert.That(groups[0].Name).IsEqualTo(PaletteRanker.PinnedGroup);
	}

	[Test]
	public async Task AGroupPastItsLimit_HidesTheRest_UntilExpanded()
	{
		var entries = Enumerable.Range(0, 11)
			.Select(i => Entry($"E{i}", $"Entry {i:D2}", $"+e{i}", always: true)).ToList();
		var snapshot = Snapshot(entries);

		var always = PaletteRanker.Groups(snapshot).Single(group => group.Name == PaletteRanker.AlwaysGroup);
		await Assert.That(always.Rows.Count).IsEqualTo(PaletteRanker.GroupLimit);
		await Assert.That(always.Hidden).IsEqualTo(3);

		var expanded = PaletteRanker.Groups(snapshot, new HashSet<string> { PaletteRanker.AlwaysGroup })
			.Single(group => group.Name == PaletteRanker.AlwaysGroup);
		await Assert.That(expanded.Rows.Count).IsEqualTo(11);
		await Assert.That(expanded.Hidden).IsEqualTo(0);
	}

	[Test]
	public async Task ARaisedEntry_CarriesTheLineFilledFromItsContext()
	{
		var join = Entry("JOIN", "Join the scene", "+scene/join <scene>", when: ["scene_here"]);
		var row = PaletteRanker.Groups(Snapshot([join], stack: [Context("scene_here", 1, ("scene", "12"))]))
			.Single(group => group.Name == PaletteRanker.HereGroup).Rows.Single();

		await Assert.That(row.Line).IsEqualTo("+scene/join 12");
		await Assert.That(row.HasSlots).IsFalse();
	}

	[Test]
	public async Task ThePagesOwnRows_AndEntriesForThisPage_AreHere()
	{
		var edit = PaletteRow.Go("/wiki/main/home/edit", "icon", "Edit");
		var link = Entry("LINK", "Link a scene", "+scene/link <scene>", context: "wiki");
		var here = PaletteRanker.Groups(Snapshot([link], page: "wiki", pageRows: [edit]))
			.Single(group => group.Name == PaletteRanker.HereGroup);

		await Assert.That(here.Rows.Select(row => row.Label)).IsEquivalentTo(["Edit", "Link a scene"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task InputRows_ShowOnlyWhileTheTerminalIsConnected_WithALeaveRowForAPlainExit()
	{
		var next = Entry("NEXT", "Next message", "n", scope: PaletteScope.Input, context: PaletteSuggestions.InputContext);
		var input = new PaletteInputView(Guid.NewGuid().ToString(), "q");

		await Assert.That(Names(PaletteRanker.Groups(Snapshot([next], input: input)))).DoesNotContain(PaletteRanker.InputGroup);

		var rows = PaletteRanker.Groups(Snapshot([next], input: input, connected: true))[0].Rows;
		await Assert.That(rows.Select(row => row.Label)).IsEquivalentTo(["Next message", "Leave"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(rows[1].Line).IsEqualTo("q");
	}

	[Test]
	public async Task Typing_RanksADeclaredEntry_AboveTheBuiltIn_AndHidesTheBuiltInWithItsLine()
	{
		var look = Entry("LOOK", "Look around", "look", scope: PaletteScope.Master, system: "staples");
		var page = Entry("PAGE", "Page someone", "page <who>=<message>", scope: PaletteScope.Master, system: "staples");
		var results = PaletteRanker.Search(Snapshot([look, page], builtins: ["LOOK", "PAGE"]), "l");
		var ids = results.Select(row => row.Id).ToList();

		await Assert.That(results[0].Label).IsEqualTo("Look around");
		await Assert.That(ids).DoesNotContain("cmd:look").Because("an entry with the same line stands in for the built-in");
		await Assert.That(ids).Contains("help:look");
		await Assert.That(PaletteRanker.Search(Snapshot([look, page], builtins: ["LOOK", "PAGE"]), "pa")
			.Select(row => row.Id)).Contains("cmd:page").Because("the entry's line has slots, so it is not the bare built-in");
	}

	[Test]
	public async Task Typing_FindsAnEntryByKeyword_AndByItsLinesFirstWord()
	{
		var roll = Entry("ROLL", "Roll dice", "+roll <dice>", keywords: "dice d20");

		await Assert.That(PaletteRanker.Search(Snapshot([roll]), "d20").Select(row => row.Label)).Contains("Roll dice");
		await Assert.That(PaletteRanker.Search(Snapshot([roll]), "+roll").Select(row => row.Label)).Contains("Roll dice");
	}

	[Test]
	public async Task Typing_PutsAPinnedRow_AboveAnEqualMatch()
	{
		var first = Entry("A", "Scene list", "+scene/list", system: "a");
		var second = Entry("B", "Scene log", "+scene/log", system: "b");
		var results = PaletteRanker.Search(Snapshot([first, second], pins: [second.Id]), "scene");

		await Assert.That(results[0].Label).IsEqualTo("Scene log");
	}

	[Test]
	public async Task Typing_LeavesInputRowsOut_WhileNoSessionIsCapturing()
	{
		var next = Entry("NEXT", "Next message", "n", scope: PaletteScope.Input, context: PaletteSuggestions.InputContext);

		await Assert.That(PaletteRanker.Search(Snapshot([next]), "next")).IsEmpty();
	}

	[Test]
	public async Task TwoEntriesWithOneLabel_AreToldApartByTheirSource()
	{
		var mine = Entry("SHEET", "Sheet", "+sheet", system: "sheet");
		var theirs = Entry("SHEET2", "Sheet", "+sheet/full", system: "#20:5") with { ObjectName = "Old Sheet" };
		var rows = PaletteRanker.EntryRows(Snapshot([mine, theirs]));

		await Assert.That(rows.Select(row => row.Hint)).IsEquivalentTo(["sheet", "Old Sheet"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}
}
