using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Palette;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Tests.Palette;

/// <summary>
/// The pieces the server and the portal share: a line's slots, what makes two lines one command, which
/// entries the stack raises and in what order, and reading an entry's JSON.
/// </summary>
public class PaletteSuggestionTests
{
	private static PaletteEntryView Entry(string label, string line, string system = "bboards",
		PaletteScope scope = PaletteScope.Master, string[]? when = null, bool urgent = false, string? info = null,
		string? context = null)
		=> new($"#9:1/{label.ToUpperInvariant()}", label, line, PaletteAction.Run, scope, system, "Globals",
			Context: context, Info: info, When: when ?? [], Urgent: urgent);

	private static PaletteContextView Context(string name, double weight, string? info = null,
		params (string Key, string Value)[] values)
		=> new(name, values.ToDictionary(pair => pair.Key, pair => pair.Value), weight, info);

	[Test]
	public async ValueTask Slots_AreReadInOrder_AndAnEscapedBracketIsText()
	{
		await Assert.That(PaletteLines.Slots("+bbread <board>/<post>")).IsEquivalentTo(new[] { "board", "post" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(PaletteLines.Slots(@"think \<b> <x")).IsEmpty();
		await Assert.That(PaletteLines.Plain(@"think \<b>", new Dictionary<string, string>())).IsEqualTo("think <b>");
	}

	[Test]
	public async ValueTask Fill_FillsWhatItCan_AndSaysWhetherTheLineIsReady()
	{
		var (partial, partialReady) = PaletteLines.Fill("+bbread <board>/<post>", new Dictionary<string, string> { ["BOARD"] = "2" });
		await Assert.That(partial).IsEqualTo("+bbread 2/<post>");
		await Assert.That(partialReady).IsFalse();

		var (whole, ready) = PaletteLines.Fill("+scene/join <scene>", new Dictionary<string, string> { ["scene"] = "12" });
		await Assert.That(whole).IsEqualTo("+scene/join 12");
		await Assert.That(ready).IsTrue();
	}

	[Test]
	public async ValueTask Key_IgnoresCaseSpacingAndSlotNames()
		=> await Assert.That(PaletteLines.Key("+BBREAD   <b>/<n>")).IsEqualTo(PaletteLines.Key("+bbread <board>/<post>"));

	[Test]
	public async ValueTask AnEntry_RisesWithItsOwnSystem_WhenItNamesNoContexts()
	{
		var suggestions = PaletteSuggestions.Suggest(
			[Entry("Next unread post", "+bbnext"), Entry("Who is on", "+who", system: "who-where")],
			[Context("bboards", 0.4)]);

		await Assert.That(suggestions.Select(suggestion => suggestion.Entry.Label)).IsEquivalentTo(new[] { "Next unread post" });
		await Assert.That(suggestions[0].Contexts).IsEquivalentTo(new[] { "bboards" });
	}

	[Test]
	public async ValueTask AnEntry_NeedsEveryContextItNames_AndRisesByTheLowestWeight()
	{
		var queue = Entry("Queue your defence", "+queue", system: "combat", when: ["combat", "my_turn"], urgent: true);
		var join = Entry("Join the scene here", "+scene/join <scene>", system: "scene", when: ["scene_here"]);
		var disengage = Entry("Disengage", "+disengage <encounter>", system: "tactics", when: ["combat"]);

		var suggestions = PaletteSuggestions.Suggest([join, disengage, queue],
		[
			Context("my_turn", 1.0, "The ogre swings at you."),
			Context("combat", 0.87, null, ("encounter", "7")),
			Context("scene_here", 0.4, null, ("scene", "12"))
		]);

		await Assert.That(suggestions.Select(suggestion => suggestion.Entry.Label)).IsEquivalentTo(
			new[] { "Queue your defence", "Disengage", "Join the scene here" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(suggestions[0].Urgent).IsTrue();
		await Assert.That(suggestions[0].Weight).IsEqualTo(0.87);
		await Assert.That(suggestions[0].Info).IsEqualTo("The ogre swings at you.").Because("the newest raising context's info wins");
		await Assert.That(suggestions[1].Line).IsEqualTo("+disengage 7");
		await Assert.That(suggestions[2].Line).IsEqualTo("+scene/join 12");
		await Assert.That(suggestions[2].Ready).IsTrue();
	}

	[Test]
	public async ValueTask AMissingContext_LeavesTheEntryOut()
	{
		var suggestions = PaletteSuggestions.Suggest(
			[Entry("Queue your defence", "+queue", system: "combat", when: ["combat", "my_turn"], urgent: true)],
			[Context("combat", 0.9)]);
		await Assert.That(suggestions).IsEmpty();
	}

	[Test]
	public async ValueTask TheSameLine_CountsOnce_FromTheNearestObject()
	{
		var near = Entry("Read (room)", "+bbread <b>/<p>", scope: PaletteScope.Room, info: "near");
		var far = Entry("Read a post", "+BBREAD <board>/<post>", scope: PaletteScope.Master, info: "far");

		var suggestions = PaletteSuggestions.Suggest([near, far], [Context("bboards", 1)]);
		await Assert.That(suggestions.Single().Info).IsEqualTo("near");
	}

	[Test]
	public async ValueTask InputEntries_AreNeverRaisedByTheStack()
		=> await Assert.That(PaletteSuggestions.Suggest([Entry("Next", "n", context: "input")], [Context("bboards", 1)])).IsEmpty();

	[Test]
	public async ValueTask AnEntry_IsReadFromItsJson()
	{
		var entry = PaletteEntry.Parse("join",
			"""{"label":"Join the scene here","line":"+scene/join <scene>","attr":"cmd`join","when":"scene_here","urgent":1,"keywords":["rp","pose"],"order":2}""")
			.Expect<PaletteEntry>();

		await Assert.That(entry.Name).IsEqualTo("JOIN");
		await Assert.That(entry.Attribute).IsEqualTo("CMD`JOIN");
		await Assert.That(entry.When).IsEquivalentTo(new[] { "scene_here" });
		await Assert.That(entry.Urgent).IsTrue();
		await Assert.That(entry.Keywords).IsEqualTo("rp pose");
		await Assert.That(entry.Order).IsEqualTo(2);
		await Assert.That(entry.Action).IsEqualTo(PaletteAction.Run);
	}

	[Test]
	[Arguments("not json", "not a JSON object")]
	[Arguments("""{"line":"+who"}""", "no label")]
	[Arguments("""{"label":"Who"}""", "no line")]
	[Arguments("""{"label":"Who","line":"+who","action":"explode"}""", "action is neither run nor insert")]
	public async ValueTask ABadEntry_SaysWhy(string value, string reason)
		=> await Assert.That(PaletteEntry.Parse("X", value).Expect<Error<string>>().Value).IsEqualTo(reason);
}
