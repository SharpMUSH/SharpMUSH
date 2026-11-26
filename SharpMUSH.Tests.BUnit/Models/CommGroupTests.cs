using SharpMUSH.Client.Models;

namespace SharpMUSH.Tests.BUnit.Models;

/// <summary><see cref="CommGroup"/>: its view key, and the JSON the account keeps it as.</summary>
public class CommGroupTests
{
	[Test]
	public async Task EveryLimitAtOnce_FitsTheStoredSetting()
	{
		// 20 groups of 16 channels with PennMUSH's longest channel names (30), each named 32 characters and
		// coloured, all folded: the most the editor lets an account keep.
		var groups = Enumerable.Range(0, CommGroup.GroupLimit).Select(g =>
		{
			var channels = Enumerable.Range(0, CommGroup.ChannelLimit).Select(c => $"{g:D2}{c:D2}".PadRight(30, 'x')).ToList();
			return new CommGroup($"{g:D2}".PadRight(CommGroup.NameLimit, 'n'), channels, true,
				channels.ToDictionary(c => c, _ => "purple"));
		}).ToList();
		var limit = SharpMUSH.Library.Models.Packages.PlayPreferences.Group.Keys
			.Single(p => p.Key == SharpMUSH.Library.Models.Packages.PlayPreferences.CommGroups).MaxLength;

		var json = CommGroup.ToJson(groups);

		await Assert.That(json.Length).IsLessThanOrEqualTo(limit);
		await Assert.That(CommGroup.FromJson(json).Count).IsEqualTo(CommGroup.GroupLimit);
	}

	[Test]
	public async Task Groups_RoundTripThroughTheirJson()
	{
		CommGroup[] groups = [new("Roleplay", ["Public", "Scenes"]), new("Staff talk", ["Admin", "Builders", "Jobs"])];

		var read = CommGroup.FromJson(CommGroup.ToJson(groups));

		await Assert.That(read.Select(g => g.Name)).IsEquivalentTo(new[] { "Roleplay", "Staff talk" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(read[1].Channels).IsEquivalentTo(new[] { "Admin", "Builders", "Jobs" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task Folding_RoundTrips_AndIsLeftOutWhenOpen()
	{
		CommGroup[] groups = [new("Roleplay", ["Public", "Scenes"], Folded: true), new("Staff talk", ["Admin", "Jobs"])];
		var json = CommGroup.ToJson(groups);

		await Assert.That(json).IsEqualTo("""[{"name":"Roleplay","channels":["Public","Scenes"],"folded":true},{"name":"Staff talk","channels":["Admin","Jobs"]}]""");
		await Assert.That(CommGroup.FromJson(json).Select(g => g.Folded)).IsEquivalentTo(new[] { true, false },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task Colours_AreChosenOrPickedInTurn_SkippingChosenOnes()
	{
		var group = new CommGroup("Roleplay", ["Public", "Scenes", "OOC"],
			Colors: new Dictionary<string, string> { ["scenes"] = "blue" });

		await Assert.That(group.HueOf("Public")).IsEqualTo("orange");
		await Assert.That(group.HueOf("SCENES")).IsEqualTo("blue");
		await Assert.That(group.HueOf("OOC")).IsEqualTo("purple");
		await Assert.That(group.HueOf("Market")).IsNull();
	}

	[Test]
	public async Task Colours_RoundTrip_AndUnknownOnesAreLeftOut()
	{
		var json = """[{"name":"R","channels":["Public","Scenes"],"colors":{"public":"green","Scenes":"chartreuse","Market":"red"}}]""";
		var group = CommGroup.FromJson(json).Single();

		await Assert.That(group.Chosen("Public")).IsEqualTo("green");
		await Assert.That(group.Chosen("Scenes")).IsNull();
		await Assert.That(CommGroup.ToJson([group])).IsEqualTo("""[{"name":"R","channels":["Public","Scenes"],"colors":{"Public":"green"}}]""");
	}

	[Test]
	public async Task AGroupKey_NeverMeetsAChannelOrAConversation()
	{
		var group = new CommGroup("Roleplay", ["Public", "Scenes"]);

		await Assert.That(group.Key).IsEqualTo("group Roleplay");
		await Assert.That(CommGroup.IsGroupKey(group.Key)).IsTrue();
		await Assert.That(CommGroup.IsGroupKey("Public")).IsFalse();
		await Assert.That(CommGroup.IsGroupKey("page #5:1 #12:1")).IsFalse();
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("not json")]
	[Arguments("{\"name\":\"Roleplay\"}")]
	[Arguments("[{\"name\":\"a\",\"name\":\"b\",\"channels\":[\"Public\",\"OOC\"]}]")]
	public async Task NothingReadable_IsNoGroups(string? json) =>
		await Assert.That(CommGroup.FromJson(json)).IsEmpty();

	[Test]
	public async Task Unreadable_Entries_AreLeftOut()
	{
		var json = """
			[
			  {"name":"Roleplay","channels":["Public","Scenes"]},
			  {"name":"roleplay","channels":["Public","OOC"]},
			  {"name":"One","channels":["Public"]},
			  {"name":"  ","channels":["Public","OOC"]},
			  {"name":"Dupes","channels":["Public","public"]},
			  {"name":"Fine","channels":["Public",3,"OOC"]},
			  "nonsense"
			]
			""";

		var read = CommGroup.FromJson(json);

		await Assert.That(read.Select(g => g.Name)).IsEquivalentTo(new[] { "Roleplay", "Fine" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("a taken name, one channel, a blank name, and two spellings of one channel are not groups");
		await Assert.That(read[1].Channels).IsEquivalentTo(new[] { "Public", "OOC" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task NoMoreThanTheLimit_IsRead()
	{
		var many = Enumerable.Range(0, CommGroup.GroupLimit + 5).Select(i => new CommGroup($"G{i}", ["Public", "OOC"]));

		await Assert.That(CommGroup.FromJson(CommGroup.ToJson(many)).Count).IsEqualTo(CommGroup.GroupLimit);
	}
}
