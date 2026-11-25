using SharpMUSH.Client.Models;

namespace SharpMUSH.Tests.BUnit.Models;

/// <summary><see cref="CommGroup"/>: its view key, and the JSON the account keeps it as.</summary>
public class CommGroupTests
{
	[Test]
	public async Task Groups_RoundTripThroughTheirJson()
	{
		CommGroup[] groups = [new("Roleplay", ["Public", "Scenes"]), new("Staff talk", ["Admin", "Builders", "Jobs"])];

		var read = CommGroup.FromJson(CommGroup.ToJson(groups));

		await Assert.That(read.Select(g => g.Name)).IsEquivalentTo(new[] { "Roleplay", "Staff talk" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(read[1].Channels).IsEquivalentTo(new[] { "Admin", "Builders", "Jobs" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
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
