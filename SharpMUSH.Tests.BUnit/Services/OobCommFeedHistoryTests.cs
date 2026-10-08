using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// <see cref="OobCommFeed"/> over the server's channel history and read markers (<c>api/comm</c>): lines
/// pulled from the recall buffer and lines pushed as <c>comm.message</c> are one history, a line known by
/// its id is kept once, and unread counts on load are worked out from the character's read markers, which
/// the feed moves on as the viewer reads.
/// </summary>
public class OobCommFeedHistoryTests
{
	private const string Viewer = "#5:1";
	private const string Wren = "#12:1";
	private const string Tomas = "#7:2";
	private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeMilliseconds(1790780000000);

	private static string ChannelList(params string[] names) =>
		$$"""{"v":2,"viewer":{"name":"Ilsa","objid":"{{Viewer}}"},"channels":[{{string.Join(",", names.Select(n => $$"""{"name":"{{n}}","joined":true}"""))}}]}""";

	private static string Line(long id, string text, string from = "Wren", string fromObjid = Wren, int second = 0) =>
		$$"""{"v":2,"id":{{id}},"kind":"channel","channel":"Public","to":[],"from":"{{from}}","fromObjid":"{{fromObjid}}","text":"{{text}}","style":"say","ts":{{T0.AddSeconds(second).ToUnixTimeMilliseconds()}}}""";

	private static string Page(string text, int second, string from = "Tomas", string fromObjid = Tomas) =>
		$$"""{"v":2,"kind":"page","to":["Ilsa"],"toObjids":["{{Viewer}}"],"from":"{{from}}","fromObjid":"{{fromObjid}}","text":"{{text}}","style":"say","ts":{{T0.AddSeconds(second).ToUnixTimeMilliseconds()}}}""";

	private static ChannelRecallLine Pulled(long id, string text, string from = "Wren", string fromObjid = Wren, int second = 0) =>
		new(id, "Public", from, fromObjid, text, "say", T0.AddSeconds(second).ToUnixTimeMilliseconds());

	private static CommReadMarkers Markers(long? publicId = null, DateTimeOffset? pageAt = null, string character = Viewer) =>
		new(character,
			publicId is { } id ? [new ChannelReadMarker("Public", id, T0)] : [],
			pageAt is { } at ? [new ConversationReadMarker([Tomas], null, at)] : []);

	private static (OobChannelStore Store, OobCommFeed Feed, FakeCommHistory History) Create()
	{
		var store = new OobChannelStore();
		var history = new FakeCommHistory();
		return (store, new OobCommFeed(store, history: history), history);
	}

	[Test]
	public async Task A_pulled_line_pushed_again_is_kept_once()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;
		history.Recall["Public"] = [Pulled(5, "one")];

		await feed.LoadHistoryAsync("Public");
		store.Set(CommPayloadParser.MessagePackage, Line(5, "one"));

		await Assert.That(feed.Messages("Public").Select(m => m.Text)).IsEquivalentTo(new[] { "one" });
		await Assert.That(feed.Messages("Public").Single().Id).IsEqualTo(5);
		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(0).Because("a line already held is not news");
		await Assert.That(history.RecallLines.Distinct()).IsEquivalentTo(new[] { 0 })
			.Because("with no marker to go back to, the feed asks for the whole buffer");
	}

	/// <summary>
	/// The list carries the channels the viewer may join as well, for the channel browser. Only the ones they
	/// are on are pulled; leaving one keeps it listed and forgets its history, so joining again pulls it again.
	/// </summary>
	[Test]
	public async Task Only_the_joined_channels_are_pulled_and_kept()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers();
		history.Recall["Public"] = [Pulled(5, "one")];
		const string bothListed = $$"""{"v":2,"viewer":{"name":"Ilsa","objid":"{{Viewer}}"},"channels":[{"name":"Public","joined":true},{"name":"Newbie","joined":false}]}""";
		const string publicLeft = $$"""{"v":2,"viewer":{"name":"Ilsa","objid":"{{Viewer}}"},"channels":[{"name":"Public","joined":false},{"name":"Newbie","joined":false}]}""";

		store.Set(CommPayloadParser.ChannelsPackage, bothListed);
		await feed.Synced;

		await Assert.That(history.Recalled).IsEquivalentTo(new[] { "Public" });
		await Assert.That(feed.Channels.Select(c => (c.Name, c.Joined))).IsEquivalentTo(new[] { ("Public", true), ("Newbie", false) });
		await Assert.That(feed.Messages("Public").Count).IsEqualTo(1);

		store.Set(CommPayloadParser.ChannelsPackage, publicLeft);
		await feed.Synced;

		await Assert.That(feed.Messages("Public")).IsEmpty().Because("a channel left is forgotten, though still listed");
		await Assert.That(history.Recalled).IsEquivalentTo(new[] { "Public" });
	}

	/// <summary>
	/// On login a channel with a marker is pulled back to it, however far that is past the lines a channel
	/// usually keeps, so the viewer sees everything they missed that the buffer still holds.
	/// </summary>
	[Test]
	public async Task Backfill_reaches_back_to_the_marker()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(publicId: 5);
		history.Recall["Public"] = Enumerable.Range(1, 300).Select(id => Pulled(id, $"line {id}")).ToList();

		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		await Assert.That(history.RecallAfter).IsEquivalentTo(new long?[] { 5 });
		await Assert.That(history.RecallLines).IsEquivalentTo(new[] { OobCommFeed.HistoryLimit });
		await Assert.That(feed.Messages("Public").Count).IsEqualTo(295);
		await Assert.That(feed.Messages("Public")[0].Id).IsEqualTo(6).Because("the first line after the marker");
		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(295);

		store.Set(CommPayloadParser.MessagePackage, Line(301, "new"));

		await Assert.That(feed.Messages("Public").Count).IsEqualTo(295)
			.Because("the channel keeps as many as the backfill brought, dropping the oldest for the new line");
		await Assert.That(feed.Messages("Public")[^1].Text).IsEqualTo("new");
	}

	/// <summary>
	/// A reconnect logs in again without clearing the store or replaying what was sent while it was down, so the
	/// first channel list after a drop pulls every channel again, back to its marker. A list without a drop
	/// pulls nothing already pulled.
	/// </summary>
	[Test]
	public async Task The_first_list_after_a_drop_pulls_what_was_missed()
	{
		var store = new OobChannelStore();
		var history = new FakeCommHistory { Markers = Markers(publicId: 2) };
		var connection = Substitute.For<ITerminalService>();
		using var feed = new OobCommFeed(store, history: history, connection: connection);
		history.Recall["Public"] = [Pulled(1, "one"), Pulled(2, "two")];
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;
		await Assert.That(history.Recalled.Count).IsEqualTo(1).Because("no drop, and Public is already pulled");

		connection.ConnectionStateChanged += Raise.Event<Action<bool>>(false);
		history.Recall["Public"].Add(Pulled(3, "missed"));
		connection.ConnectionStateChanged += Raise.Event<Action<bool>>(true);
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		await Assert.That(history.Recalled.Count).IsEqualTo(2);
		await Assert.That(history.RecallAfter[^1]).IsEqualTo(2);
		await Assert.That(feed.Messages("Public").Select(m => m.Text)).IsEquivalentTo(new[] { "one", "two", "missed" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(1);

		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;
		await Assert.That(history.Recalled.Count).IsEqualTo(2).Because("that drop is covered");
	}

	/// <summary>A pull that fails after a drop is tried again on the next list, not taken as covered.</summary>
	[Test]
	public async Task A_failed_pull_after_a_drop_is_tried_again()
	{
		var store = new OobChannelStore();
		var history = new FakeCommHistory { Markers = Markers(publicId: 1) };
		var connection = Substitute.For<ITerminalService>();
		using var feed = new OobCommFeed(store, history: history, connection: connection);
		history.Recall["Public"] = [Pulled(1, "one")];
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		connection.ConnectionStateChanged += Raise.Event<Action<bool>>(false);
		history.Recall["Public"].Add(Pulled(2, "missed"));
		history.FailRecalls = 1;
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;
		await Assert.That(feed.Messages("Public").Select(m => m.Text)).IsEquivalentTo(new[] { "one" });

		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		await Assert.That(feed.Messages("Public").Select(m => m.Text)).IsEquivalentTo(new[] { "one", "missed" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>With no marker, the whole recall buffer is history, past the lines a channel usually keeps.</summary>
	[Test]
	public async Task Without_a_marker_the_whole_buffer_is_pulled()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers();
		history.Recall["Public"] = Enumerable.Range(1, 500).Select(id => Pulled(id, $"line {id}")).ToList();

		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		await Assert.That(history.RecallAfter).IsEquivalentTo(new long?[] { null });
		await Assert.That(feed.Messages("Public").Count).IsEqualTo(500);
		await Assert.That(feed.Messages("Public")[0].Id).IsEqualTo(1);
	}

	/// <summary>A resumed connection replays what it missed; a line the feed already has is not counted twice.</summary>
	[Test]
	public async Task A_pushed_line_pushed_again_is_kept_once()
	{
		var (store, feed, _) = Create();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));

		store.Set(CommPayloadParser.MessagePackage, Line(5, "one"));
		store.Set(CommPayloadParser.MessagePackage, "{}");
		store.Set(CommPayloadParser.MessagePackage, Line(5, "one"));

		await Assert.That(feed.Messages("Public").Count).IsEqualTo(1);
		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(1);
	}

	[Test]
	public async Task Pulled_lines_join_pushed_ones_in_order()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;
		store.Set(CommPayloadParser.MessagePackage, Line(7, "three", second: 3));
		history.Recall["Public"] = [Pulled(5, "one", second: 1), Pulled(6, "two", second: 2), Pulled(7, "three", second: 3)];

		await feed.LoadHistoryAsync("Public");

		await Assert.That(feed.Messages("Public").Select(m => m.Text)).IsEquivalentTo(new[] { "one", "two", "three" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// On load the feed reads the markers once it knows whose feed it is, pulls each channel's history, and
	/// counts as unread what came after the marker from someone else — so the count survives a reload.
	/// </summary>
	[Test]
	public async Task Unread_on_load_is_counted_from_the_marker()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(publicId: 5);
		history.Recall["Public"] =
		[
			Pulled(4, "old"), Pulled(5, "read"), Pulled(6, "new"), Pulled(7, "mine", "Ilsa", Viewer)
		];

		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		await Assert.That(feed.Messages("Public").Count).IsEqualTo(4);
		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(1).Because("only 6 is after the marker and someone else's");
	}

	/// <summary>Lines pushed before the markers arrived are counted again from them, pull or no pull.</summary>
	[Test]
	public async Task Lines_pushed_before_the_markers_arrived_are_counted_from_them()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(publicId: 5);

		store.Set(CommPayloadParser.MessagePackage, Line(4, "read elsewhere"));
		store.Set(CommPayloadParser.MessagePackage, Line(6, "news"));
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(1);
	}

	[Test]
	public async Task Without_a_marker_pulled_history_is_not_unread()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers();
		history.Recall["Public"] = [Pulled(4, "old"), Pulled(5, "older")];

		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		await Assert.That(feed.Messages("Public").Count).IsEqualTo(2);
		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(0)
			.Because("a channel never read before has nothing to count from; only lines that arrive count");
	}

	[Test]
	public async Task A_live_line_behind_the_marker_is_not_unread()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(publicId: 10);
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		store.Set(CommPayloadParser.MessagePackage, Line(9, "read on another device"));
		store.Set(CommPayloadParser.MessagePackage, Line(11, "news"));

		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(1);
	}

	[Test]
	public async Task Marking_read_moves_the_server_marker_to_the_last_line()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(publicId: 1);
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;
		store.Set(CommPayloadParser.MessagePackage, Line(20, "one", second: 1));
		store.Set(CommPayloadParser.MessagePackage, Line(21, "two", second: 2));

		feed.MarkRead("Public");
		feed.MarkRead("Public");

		await Assert.That(history.ChannelMarks.Single())
			.IsEqualTo(("Public", new ReadMarkerUpdate(21, T0.AddSeconds(2))))
			.Because("a second MarkRead with nothing new sends nothing");
	}

	/// <summary>A marker write that failed is sent again on the next read; the feed only records what the server took.</summary>
	[Test]
	public async Task A_failed_marker_write_is_sent_again()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(publicId: 1);
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;
		store.Set(CommPayloadParser.MessagePackage, Line(20, "one", second: 1));
		history.FailMarks = 1;

		feed.MarkRead("Public");
		feed.MarkRead("Public");
		feed.MarkRead("Public");

		await Assert.That(history.ChannelMarks.Count).IsEqualTo(2)
			.Because("the first write failed, the second succeeded, and the third had nothing new to send");
	}

	[Test]
	public async Task A_failed_conversation_marker_write_is_sent_again()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList());
		await feed.Synced;
		store.Set(CommPayloadParser.MessagePackage, Page("psst", 3));
		history.FailMarks = 1;

		var key = feed.Conversations.Single().Key;
		feed.MarkRead(key);
		feed.MarkRead(key);
		feed.MarkRead(key);

		await Assert.That(history.ConversationMarks.Count).IsEqualTo(2);
	}

	/// <summary>Leaving a channel and joining it again pulls it again: what was said meanwhile is history to fetch.</summary>
	[Test]
	public async Task A_channel_left_and_rejoined_is_pulled_again()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(publicId: 5);
		history.Recall["Public"] = [Pulled(5, "read")];
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		store.Set(CommPayloadParser.ChannelsPackage, ChannelList());
		history.Recall["Public"] = [Pulled(5, "read"), Pulled(6, "while away")];
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		await Assert.That(history.Recalled.Count(name => name == "Public")).IsEqualTo(2);
		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(1);
	}

	/// <summary>
	/// A failed read of the markers on a join or rename pulls nothing, so the channel is not taken as done:
	/// the next list tries again, and then counts from the marker.
	/// </summary>
	[Test]
	public async Task A_failed_marker_refresh_pulls_nothing_and_the_next_list_retries()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList());
		await feed.Synced;

		history.Markers = null;
		history.Recall["Public"] = [Pulled(5, "read"), Pulled(6, "new")];
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		await Assert.That(history.Recalled).IsEmpty();

		history.Markers = Markers(publicId: 5);
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		await Assert.That(history.Recalled).IsEquivalentTo(new[] { "Public" });
		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(1);
	}

	/// <summary>A count the list carries is the game's own and stands; a marker does not recount it.</summary>
	[Test]
	public async Task A_count_the_list_carries_is_not_recounted_from_a_marker()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(publicId: 5);
		history.Recall["Public"] = [Pulled(5, "read"), Pulled(6, "new")];

		store.Set(CommPayloadParser.ChannelsPackage,
			$$"""{"v":2,"viewer":{"name":"Ilsa","objid":"{{Viewer}}"},"channels":[{"name":"Public","unread":500}]}""");
		await feed.Synced;

		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(500);
	}

	/// <summary>
	/// A channel renamed while the viewer is connected: the server moved its marker to the new name, and
	/// the new list names it. The feed reads the markers again, so the lines before the rename are still
	/// counted from where the viewer had read to.
	/// </summary>
	[Test]
	public async Task A_renamed_channel_keeps_its_marker()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(publicId: 5);
		history.Recall["Public"] = [Pulled(5, "read")];
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		history.Markers = new CommReadMarkers(Viewer, [new ChannelReadMarker("Commons", 5, T0)], []);
		history.Recall["Commons"] = [Pulled(4, "old"), Pulled(5, "read"), Pulled(6, "new")];
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Commons"));
		await feed.Synced;

		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(1).Because("only 6 is past the marker");
	}

	[Test]
	public async Task A_line_arriving_while_viewing_moves_the_marker()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(publicId: 1);
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;
		feed.Viewing = "Public";

		store.Set(CommPayloadParser.MessagePackage, Line(30, "seen as it came", second: 4));

		await Assert.That(history.ChannelMarks.Last()).IsEqualTo(("Public", new ReadMarkerUpdate(30, T0.AddSeconds(4))));
	}

	/// <summary>
	/// The markers are the session's acting character's. A play terminal holding someone else's feed
	/// neither reads nor writes them.
	/// </summary>
	[Test]
	public async Task Another_characters_markers_are_not_used()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(publicId: 5, character: "#99:1");
		history.Recall["Public"] = [Pulled(6, "new")];
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;
		store.Set(CommPayloadParser.MessagePackage, Line(7, "live"));

		feed.MarkRead("Public");

		await Assert.That(history.ChannelMarks).IsEmpty();
		await Assert.That(history.Recalled).IsEmpty();
	}

	[Test]
	public async Task A_page_read_before_the_reload_is_not_unread()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(pageAt: T0.AddSeconds(5));
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList());
		await feed.Synced;

		store.Set(CommPayloadParser.MessagePackage, Page("replayed", 4));
		store.Set(CommPayloadParser.MessagePackage, Page("new", 6));

		await Assert.That(feed.Conversations.Single().Unread).IsEqualTo(1);
	}

	[Test]
	public async Task Reading_a_conversation_marks_it_by_who_it_is_with_and_when()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList());
		await feed.Synced;
		store.Set(CommPayloadParser.MessagePackage, Page("psst", 3));

		feed.MarkRead(feed.Conversations.Single().Key);

		var mark = history.ConversationMarks.Single();
		await Assert.That(mark.With).IsEquivalentTo(new[] { Tomas });
		await Assert.That(mark.LastReadId).IsNull();
		await Assert.That(mark.LastReadAt).IsEqualTo(T0.AddSeconds(3));
	}

	[Test]
	public async Task A_conversation_with_someone_known_only_by_name_is_not_marked()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers();
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList());
		await feed.Synced;
		store.Set(CommPayloadParser.MessagePackage, """{"kind":"page","to":["Ilsa"],"toObjids":["#5:1"],"from":"Tomas","text":"hi"}""");

		feed.MarkRead(feed.Conversations.Single().Key);

		await Assert.That(history.ConversationMarks).IsEmpty();
	}

	[Test]
	public async Task A_character_switch_forgets_the_markers()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(publicId: 10);
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;

		store.Clear();
		history.Markers = null;
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList("Public"));
		await feed.Synced;
		store.Set(CommPayloadParser.MessagePackage, Line(9, "counts now"));

		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(1);
	}
}
