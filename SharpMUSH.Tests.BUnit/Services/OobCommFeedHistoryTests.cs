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
