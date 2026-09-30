using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// <see cref="OobCommFeed"/>: the Play sidebar's channels and pages, fed by the <c>comm.channels</c> and
/// <c>comm.message</c> pushes arriving in the play terminal's OOB store.
/// </summary>
public class OobCommFeedTests
{
	private const string Me = """{"name":"Ilsa","objid":"#5:1"}""";

	private static string ChannelList(params string[] names) =>
		$$"""{"v":2,"viewer":{{Me}},"channels":[{{string.Join(",", names.Select(n => $$"""{"name":"{{n}}","joined":true}"""))}}]}""";

	private static string Line(string channel, string from, string fromObjid, string text, long ts = 1790780182950) =>
		$$"""{"v":2,"kind":"channel","channel":"{{channel}}","to":[],"from":"{{from}}","fromObjid":"{{fromObjid}}","text":"{{text}}","style":"say","ts":{{ts}}}""";

	private static string Page(string from, string fromObjid, (string Name, string ObjId)[] to, string text, long ts = 1790780182950) =>
		$$"""{"v":2,"kind":"page","to":[{{string.Join(",", to.Select(t => $"\"{t.Name}\""))}}],"toObjids":[{{string.Join(",", to.Select(t => $"\"{t.ObjId}\""))}}],"from":"{{from}}","fromObjid":"{{fromObjid}}","text":"{{text}}","style":"say","ts":{{ts}}}""";

	private static (OobChannelStore Store, OobCommFeed Feed) Create()
	{
		var store = new OobChannelStore();
		return (store, new OobCommFeed(store));
	}

	[Test]
	public async Task The_channel_list_is_the_latest_push()
	{
		var (store, feed) = Create();
		var changes = 0;
		feed.Changed += () => changes++;

		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public", "Newcomers"));
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));

		await Assert.That(feed.Channels.Select(c => c.Name)).IsEquivalentTo(new[] { "Public" });
		await Assert.That(changes).IsEqualTo(2);
	}

	[Test]
	public async Task Lines_are_filed_under_their_channel_and_counted_until_read()
	{
		var (store, feed) = Create();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public", "Builders"));

		store.Set(CommPayloadParser.MessagePackage, Line("Public", "Wren", "#12:1", "one"));
		store.Set(CommPayloadParser.MessagePackage, Line("public", "Dace", "#13:1", "two"));

		await Assert.That(feed.Messages("Public").Select(m => m.Text)).IsEquivalentTo(new[] { "one", "two" });
		await Assert.That(feed.Channels.Single(c => c.Name == "Public").Unread).IsEqualTo(2);
		await Assert.That(feed.Channels.Single(c => c.Name == "Builders").Unread).IsEqualTo(0);

		feed.MarkRead("PUBLIC");

		await Assert.That(feed.Channels.Single(c => c.Name == "Public").Unread).IsEqualTo(0);
		await Assert.That(feed.Messages("Public").Count).IsEqualTo(2).Because("reading a channel keeps its history");
	}

	[Test]
	public async Task The_viewers_own_lines_are_not_unread()
	{
		var (store, feed) = Create();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));

		store.Set(CommPayloadParser.MessagePackage, Line("Public", "Ilsa", "#5:1", "mine"));

		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(0);
		await Assert.That(feed.Messages("Public").Count).IsEqualTo(1);
	}

	[Test]
	public async Task Lines_for_the_key_being_viewed_are_not_unread()
	{
		var (store, feed) = Create();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public", "Builders"));
		store.Set(CommPayloadParser.MessagePackage, Line("Public", "Wren", "#12:1", "before"));

		feed.Viewing = "Public";
		store.Set(CommPayloadParser.MessagePackage, Line("Public", "Wren", "#12:1", "while looking"));
		store.Set(CommPayloadParser.MessagePackage, Line("Builders", "Wren", "#12:1", "elsewhere"));

		await Assert.That(feed.Channels.Single(c => c.Name == "Public").Unread).IsEqualTo(0)
			.Because("looking at a channel reads it, and what arrives while looking is read as it comes");
		await Assert.That(feed.Channels.Single(c => c.Name == "Builders").Unread).IsEqualTo(1);

		feed.Viewing = null;
		store.Set(CommPayloadParser.MessagePackage, Line("Public", "Wren", "#12:1", "after"));
		await Assert.That(feed.Channels.Single(c => c.Name == "Public").Unread).IsEqualTo(1);
	}

	[Test]
	public async Task History_is_bounded_per_key_dropping_the_oldest()
	{
		var (store, feed) = Create();

		for (var i = 0; i < OobCommFeed.HistoryLimit + 5; i++)
			store.Set(CommPayloadParser.MessagePackage, Line("Public", "Wren", "#12:1", $"line {i}"));
		store.Set(CommPayloadParser.MessagePackage, Line("Builders", "Wren", "#12:1", "other"));

		var kept = feed.Messages("Public");
		await Assert.That(kept.Count).IsEqualTo(OobCommFeed.HistoryLimit);
		await Assert.That(kept[0].Text).IsEqualTo("line 5");
		await Assert.That(kept[^1].Text).IsEqualTo($"line {OobCommFeed.HistoryLimit + 4}");
		await Assert.That(feed.Messages("Builders").Count).IsEqualTo(1);
	}

	[Test]
	public async Task A_page_conversation_is_keyed_by_its_participants_whoever_sent_each_line()
	{
		var (store, feed) = Create();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList());

		// Tomas pages me, I answer, Tomas answers: one conversation, with Tomas.
		store.Set(CommPayloadParser.MessagePackage, Page("Tomas", "#7:2", [("Ilsa", "#5:1")], "psst", 1000));
		store.Set(CommPayloadParser.MessagePackage, Page("Ilsa", "#5:1", [("Tomas", "#7:2")], "yes?", 2000));
		store.Set(CommPayloadParser.MessagePackage, Page("Tomas", "#7:2", [("Ilsa", "#5:1")], "later", 3000));

		var conversation = feed.Conversations.Single();
		await Assert.That(conversation.With).IsEquivalentTo(new[] { "Tomas" });
		await Assert.That(conversation.WithObjIds).IsEquivalentTo(new string?[] { "#7:2" });
		await Assert.That(conversation.Unread).IsEqualTo(2).Because("my own reply is not unread");
		await Assert.That(conversation.LastAt).IsEqualTo(DateTimeOffset.FromUnixTimeMilliseconds(3000));
		await Assert.That(feed.Messages(conversation.Key).Select(m => m.Text))
			.IsEquivalentTo(new[] { "psst", "yes?", "later" });
	}

	[Test]
	public async Task A_group_page_is_its_own_conversation_whatever_order_names_arrive_in()
	{
		var (store, feed) = Create();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList());

		store.Set(CommPayloadParser.MessagePackage, Page("Tomas", "#7:2", [("Ilsa", "#5:1"), ("Dace", "#8:3")], "all", 1000));
		store.Set(CommPayloadParser.MessagePackage, Page("Dace", "#8:3", [("Tomas", "#7:2"), ("Ilsa", "#5:1")], "agreed", 2000));
		store.Set(CommPayloadParser.MessagePackage, Page("Tomas", "#7:2", [("Ilsa", "#5:1")], "just you", 3000));

		await Assert.That(feed.Conversations.Count).IsEqualTo(2);
		var group = feed.Conversations.Single(c => c.With.Count == 2);
		await Assert.That(group.With).IsEquivalentTo(new[] { "Tomas", "Dace" });
		await Assert.That(feed.Messages(group.Key).Count).IsEqualTo(2);
		await Assert.That(feed.Conversations[0].With).IsEquivalentTo(new[] { "Tomas" })
			.Because("conversations are listed most recent first");
	}

	[Test]
	public async Task Without_objids_a_conversation_is_keyed_by_name()
	{
		var (store, feed) = Create();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList());

		store.Set(CommPayloadParser.MessagePackage,
			"""{"kind":"page","to":["Ilsa"],"from":"Tomas","text":"one"}""");
		store.Set(CommPayloadParser.MessagePackage,
			"""{"kind":"page","to":["ilsa"],"from":"TOMAS","text":"two"}""");

		var conversation = feed.Conversations.Single();
		await Assert.That(feed.Messages(conversation.Key).Count).IsEqualTo(2);
		await Assert.That(conversation.WithObjIds).IsEquivalentTo(new string?[] { null });
	}

	[Test]
	public async Task A_channel_name_can_never_be_mistaken_for_a_conversation()
	{
		var (store, feed) = Create();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList());

		store.Set(CommPayloadParser.MessagePackage, Page("Ilsa", "#5:1", [("Ilsa", "#5:1")], "note to self"));

		var key = feed.Conversations.Single().Key;
		await Assert.That(key).Contains(" ").Because("a channel name cannot hold a space");
		await Assert.That(feed.Messages("#5:1")).IsEmpty();
	}

	[Test]
	public async Task Clearing_the_store_clears_the_feed_once()
	{
		var (store, feed) = Create();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		store.Set(CommPayloadParser.MessagePackage, Line("Public", "Wren", "#12:1", "hello"));
		store.Set(CommPayloadParser.MessagePackage, Page("Tomas", "#7:2", [("Ilsa", "#5:1")], "psst"));
		var changes = 0;
		feed.Changed += () => changes++;

		store.Clear();

		await Assert.That(feed.Channels).IsEmpty();
		await Assert.That(feed.Conversations).IsEmpty();
		await Assert.That(feed.Messages("Public")).IsEmpty();
		await Assert.That(changes).IsEqualTo(1);
	}

	[Test]
	public async Task A_character_switch_through_the_proxy_clears_the_feed()
	{
		var proxy = new OobChannelStoreProxy();
		var first = new OobChannelStore();
		proxy.SetInner(first);
		var feed = new OobCommFeed(proxy);
		first.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		first.Set(CommPayloadParser.MessagePackage, Line("Public", "Wren", "#12:1", "hello"));

		proxy.SetInner(new OobChannelStore());

		await Assert.That(feed.Channels).IsEmpty();
		await Assert.That(feed.Messages("Public")).IsEmpty();
	}

	[Test]
	public async Task Unreadable_pushes_change_nothing()
	{
		var (store, feed) = Create();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		var changes = 0;
		feed.Changed += () => changes++;

		store.Set(CommPayloadParser.ChannelsPackage, "{not json");
		store.Set(CommPayloadParser.MessagePackage, """{"kind":"shout"}""");
		store.Set("room.contents", """{"v":2,"who":[]}""");

		await Assert.That(feed.Channels.Select(c => c.Name)).IsEquivalentTo(new[] { "Public" });
		await Assert.That(changes).IsEqualTo(0);
	}

	[Test]
	public async Task Without_a_channel_list_the_viewer_is_the_room_row_marked_you()
	{
		var (store, feed) = Create();
		store.Set("room.contents", """{"v":2,"who":[{"dbref":"#5","objid":"#5:1","name":"Ilsa","you":true}]}""");
		store.Set(CommPayloadParser.ChannelsPackage, """{"v":2,"channels":[{"name":"Public"}]}""");

		store.Set(CommPayloadParser.MessagePackage, Line("Public", "Ilsa", "#5:1", "mine"));
		store.Set(CommPayloadParser.MessagePackage, Page("Tomas", "#7:2", [("Ilsa", "#5:1")], "psst"));

		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(0);
		await Assert.That(feed.Conversations.Single().With).IsEquivalentTo(new[] { "Tomas" });
	}

	[Test]
	public async Task A_server_unread_count_is_taken_when_the_list_carries_one()
	{
		var (store, feed) = Create();

		store.Set(CommPayloadParser.ChannelsPackage,
			"""{"v":2,"channels":[{"name":"Public","unread":4},{"name":"OOC"}]}""");

		await Assert.That(feed.Channels.Single(c => c.Name == "Public").Unread).IsEqualTo(4);
		await Assert.That(feed.Channels.Single(c => c.Name == "OOC").Unread).IsEqualTo(0);
	}
}
