using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// <see cref="OobCommFeed"/> over the server's page log (<c>api/comm/conversations</c>, the game's
/// <c>page_log</c>): after a load the feed rebuilds its page conversations from the server, pulls a
/// conversation's pages when it is opened, keeps one copy of a page known by its id however it arrived, and
/// counts unread from the conversation's id-based read marker.
/// </summary>
public class OobCommFeedPageLogTests
{
	private const string Viewer = "#5:1";
	private const string Tomas = "#7:2";
	private const string Dace = "#9:3";
	private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeMilliseconds(1790780000000);

	private static readonly string ChannelList =
		$$"""{"v":2,"viewer":{"name":"Ilsa","objid":"{{Viewer}}"},"channels":[]}""";

	private static string PageFromTomas(long id, string text, int second) =>
		$$"""{"v":2,"id":{{id}},"kind":"page","to":["Ilsa"],"toObjids":["{{Viewer}}"],"from":"Tomas","fromObjid":"{{Tomas}}","text":"{{text}}","style":"say","ts":{{T0.AddSeconds(second).ToUnixTimeMilliseconds()}}}""";

	private static PageRecallLine Logged(long id, string text, int second, string from = "Tomas", string fromObjid = Tomas) =>
		new(id, fromObjid == Viewer ? ["Tomas"] : ["Ilsa"], fromObjid == Viewer ? [Tomas] : [Viewer], from, fromObjid, text,
			"say", T0.AddSeconds(second).ToUnixTimeMilliseconds());

	private static PageConversationSummary WithTomas(long lastId, int second) =>
		new([Tomas], ["Tomas"], lastId, T0.AddSeconds(second));

	private static CommReadMarkers Markers(long? tomasId = null, string character = Viewer) =>
		new(character, [], tomasId is { } id ? [new ConversationReadMarker([Tomas], id, T0)] : []);

	private static string TomasKey => "page " + string.Join(' ', new[] { Tomas, Viewer }.Order(StringComparer.Ordinal));

	private static (OobChannelStore Store, OobCommFeed Feed, FakeCommHistory History) Create()
	{
		var store = new OobChannelStore();
		var history = new FakeCommHistory { Markers = Markers() };
		return (store, new OobCommFeed(store, history: history), history);
	}

	private static async Task LoadAsync(OobChannelStore store, OobCommFeed feed)
	{
		store.Set(CommPayloadParser.ChannelsPackage, ChannelList);
		await feed.Synced;
	}

	/// <summary>A reload keeps the conversations: the feed lists them from the server once it knows its viewer.</summary>
	[Test]
	public async Task Conversations_are_rebuilt_from_the_server_after_a_load()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, true,
		[
			WithTomas(20, 20),
			new PageConversationSummary([Dace, Tomas], ["Dace", "Tomas"], 10, T0.AddSeconds(10))
		]);

		await LoadAsync(store, feed);

		var conversations = feed.Conversations;
		await Assert.That(conversations.Count).IsEqualTo(2);
		await Assert.That(conversations[0].Key).IsEqualTo(TomasKey).Because("the latest first");
		await Assert.That(conversations[0].With).IsEquivalentTo(new[] { "Tomas" });
		await Assert.That(conversations[0].WithObjIds).IsEquivalentTo(new[] { Tomas });
		await Assert.That(conversations[0].LastAt).IsEqualTo(T0.AddSeconds(20));
		await Assert.That(conversations[1].With).IsEquivalentTo(new[] { "Dace", "Tomas" });
		await Assert.That(feed.PageLogging).IsTrue();
	}

	/// <summary>A pushed page files under the same key the rebuilt conversation has.</summary>
	[Test]
	public async Task A_rebuilt_conversation_and_a_pushed_page_are_one_conversation()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(20, 20)]);
		await LoadAsync(store, feed);

		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(30, "live", 30));

		await Assert.That(feed.Conversations.Count).IsEqualTo(1);
		await Assert.That(feed.Conversations.Single().LastAt).IsEqualTo(T0.AddSeconds(30));
	}

	[Test]
	public async Task Opening_a_conversation_pulls_its_pages()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(21, 21)]);
		history.PageLog[Tomas] = [Logged(20, "hello", 20), Logged(21, "you there?", 21)];
		await LoadAsync(store, feed);

		await feed.LoadHistoryAsync(TomasKey);

		var lines = feed.Messages(TomasKey);
		await Assert.That(lines.Select(line => line.Text)).IsEquivalentTo(new[] { "hello", "you there?" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(lines[0].Kind).IsEqualTo(CommPayloadParser.PageKind);
		await Assert.That(lines[0].Channel).IsNull();
		await Assert.That(lines[0].To).IsEquivalentTo(new[] { "Ilsa" });
		await Assert.That(lines[0].FromObjId).IsEqualTo(Tomas);
		await Assert.That(lines[0].Id).IsEqualTo(20);
		await Assert.That(history.PageRecalled).Contains(Tomas);
		await Assert.That(history.PageRecallLines.Distinct()).IsEquivalentTo(new[] { 0 })
			.Because("with no marker to go back to, the feed asks for as many pages as the server gives");
	}

	[Test]
	public async Task A_pulled_page_pushed_again_is_kept_once()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(20, 20)]);
		history.PageLog[Tomas] = [Logged(20, "hello", 20)];
		await LoadAsync(store, feed);
		await feed.LoadHistoryAsync(TomasKey);

		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(20, "hello", 20));

		await Assert.That(feed.Messages(TomasKey).Count).IsEqualTo(1);
		await Assert.That(feed.Conversations.Single().Unread).IsEqualTo(0).Because("a page already held is not news");
	}

	/// <summary>A resumed connection replays what it missed; a page the feed already has is not counted twice.</summary>
	[Test]
	public async Task A_pushed_page_pushed_again_is_kept_once()
	{
		var (store, feed, _) = Create();
		await LoadAsync(store, feed);

		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(20, "once", 20));
		store.Set(CommPayloadParser.MessagePackage, "{}");
		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(20, "once", 20));

		await Assert.That(feed.Messages(TomasKey).Count).IsEqualTo(1);
		await Assert.That(feed.Conversations.Single().Unread).IsEqualTo(1);
	}

	[Test]
	public async Task Pulled_pages_join_pushed_ones_in_order()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(22, 22)]);
		await LoadAsync(store, feed);
		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(22, "three", 22));
		history.PageLog[Tomas] = [Logged(20, "one", 20), Logged(21, "two", 21), Logged(22, "three", 22)];

		await feed.LoadHistoryAsync(TomasKey);

		await Assert.That(feed.Messages(TomasKey).Select(line => line.Text)).IsEquivalentTo(new[] { "one", "two", "three" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// A conversation whose last page is past the viewer's marker is pulled on load, so its unread count —
	/// what came after the marker from someone else — survives a reload.
	/// </summary>
	[Test]
	public async Task Unread_on_load_is_counted_from_the_conversations_marker()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(tomasId: 21);
		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(24, 24)]);
		history.PageLog[Tomas] =
		[
			Logged(20, "old", 20), Logged(21, "read", 21), Logged(22, "new", 22), Logged(23, "newer", 23),
			Logged(24, "mine", 24, "Ilsa", Viewer)
		];

		await LoadAsync(store, feed);

		await Assert.That(feed.Messages(TomasKey).Count).IsEqualTo(5);
		await Assert.That(feed.Conversations.Single().Unread).IsEqualTo(2).Because("22 and 23 are after the marker and Tomas's");
	}

	/// <summary>
	/// On load a conversation behind its marker is pulled back to it, however far that is past the lines a
	/// conversation usually keeps, so pages missed while on another machine are all there.
	/// </summary>
	[Test]
	public async Task Backfill_reaches_back_to_the_conversations_marker()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(tomasId: 5);
		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(300, 300)]);
		history.PageLog[Tomas] = Enumerable.Range(1, 300).Select(id => Logged(id, $"page {id}", id)).ToList();

		await LoadAsync(store, feed);

		await Assert.That(history.PageRecallAfter).IsEquivalentTo(new long?[] { 5 });
		await Assert.That(feed.Messages(TomasKey).Count).IsEqualTo(295);
		await Assert.That(feed.Messages(TomasKey)[0].Id).IsEqualTo(6).Because("the first page after the marker");
		await Assert.That(feed.Conversations.Single().Unread).IsEqualTo(295);
	}

	/// <summary>A conversation with no marker is pulled on load, as far back as the server gives.</summary>
	[Test]
	public async Task A_conversation_without_a_marker_is_pulled_whole_on_load()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(400, 400)]);
		history.PageLog[Tomas] = Enumerable.Range(1, 400).Select(id => Logged(id, $"page {id}", id)).ToList();

		await LoadAsync(store, feed);

		await Assert.That(history.PageRecallLines).IsEquivalentTo(new[] { 0 });
		await Assert.That(history.PageRecallAfter).IsEquivalentTo(new long?[] { null });
		await Assert.That(feed.Messages(TomasKey).Count).IsEqualTo(400);
	}

	/// <summary>A conversation pull that fails on load is tried again on the next list.</summary>
	[Test]
	public async Task A_failed_conversation_pull_is_tried_again_on_the_next_list()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(tomasId: 20);
		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(21, 21)]);
		history.PageLog[Tomas] = [Logged(20, "read", 20), Logged(21, "missed", 21)];
		history.FailRecalls = 1;

		await LoadAsync(store, feed);
		await Assert.That(feed.Messages(TomasKey)).IsEmpty();

		await LoadAsync(store, feed);

		await Assert.That(feed.Messages(TomasKey).Select(line => line.Text)).Contains("missed");
	}

	/// <summary>A conversation read up to its last page is not pulled until it is opened.</summary>
	[Test]
	public async Task A_conversation_read_to_its_end_is_not_pulled_on_load()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(tomasId: 24);
		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(24, 24)]);
		history.PageLog[Tomas] = [Logged(24, "read", 24)];

		await LoadAsync(store, feed);

		await Assert.That(history.PageRecalled).IsEmpty();
		await Assert.That(feed.Conversations.Single().Unread).IsEqualTo(0);
	}

	/// <summary>A page now has an id, and a conversation's marker moves by it.</summary>
	[Test]
	public async Task Reading_a_conversation_marks_it_by_the_last_pages_id()
	{
		var (store, feed, history) = Create();
		await LoadAsync(store, feed);
		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(40, "psst", 3));

		feed.MarkRead(TomasKey);

		var mark = history.ConversationMarks.Single();
		await Assert.That(mark.With).IsEquivalentTo(new[] { Tomas });
		await Assert.That(mark.LastReadId).IsEqualTo(40);
		await Assert.That(mark.LastReadAt).IsEqualTo(T0.AddSeconds(3));
	}

	/// <summary>A page behind an id marker is read, whatever its time: two pages can share a millisecond.</summary>
	[Test]
	public async Task A_live_page_behind_the_marker_is_not_unread()
	{
		var (store, feed, history) = Create();
		history.Markers = Markers(tomasId: 30);
		await LoadAsync(store, feed);

		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(29, "read elsewhere", 50));
		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(31, "news", 50));

		await Assert.That(feed.Conversations.Single().Unread).IsEqualTo(1);
	}

	/// <summary>With the game keeping no page log the feed lists nothing from it and says so.</summary>
	[Test]
	public async Task With_the_page_log_off_nothing_is_rebuilt_and_the_feed_says_so()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, false, []);
		history.PageLogging = false;

		await LoadAsync(store, feed);
		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(20, "live only", 20));
		await feed.LoadHistoryAsync(TomasKey);

		await Assert.That(feed.PageLogging).IsFalse();
		await Assert.That(feed.Messages(TomasKey).Select(line => line.Text)).IsEquivalentTo(new[] { "live only" });
	}

	/// <summary>
	/// <c>page_log</c> is a live option: a game that turns it on while the portal is open has history to
	/// pull, so opening a conversation asks again rather than trusting an earlier "off".
	/// </summary>
	[Test]
	public async Task A_page_log_turned_on_later_is_pulled_when_a_conversation_opens()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, false, []);
		history.PageLogging = false;
		await LoadAsync(store, feed);
		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(21, "live", 21));
		await feed.LoadHistoryAsync(TomasKey);

		history.PageLogging = true;
		history.PageLog[Tomas] = [Logged(20, "logged once on", 20), Logged(21, "live", 21)];
		await feed.LoadHistoryAsync(TomasKey);

		await Assert.That(feed.PageLogging).IsTrue();
		await Assert.That(feed.Messages(TomasKey).Select(line => line.Text)).IsEquivalentTo(new[] { "logged once on", "live" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// The view starts the pull without awaiting it and redraws on <see cref="OobCommFeed.Changed"/>, so the
	/// logging-off note goes away only if the feed says something changed, even when the pull added no line.
	/// </summary>
	[Test]
	public async Task Page_logging_turning_back_on_raises_Changed_even_with_nothing_new()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, false, []);
		history.PageLogging = false;
		await LoadAsync(store, feed);
		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(21, "live", 21));
		await feed.LoadHistoryAsync(TomasKey);

		history.PageLogging = true;
		history.PageLog[Tomas] = [Logged(21, "live", 21)];
		var changes = 0;
		feed.Changed += () => changes++;
		await feed.LoadHistoryAsync(TomasKey);

		await Assert.That(feed.PageLogging).IsTrue();
		await Assert.That(changes).IsEqualTo(1);
	}

	/// <summary>
	/// As for channels: without the markers, conversations would be filed uncounted, so a failed read of the
	/// markers neither lists nor pulls any; the next list tries again.
	/// </summary>
	[Test]
	public async Task A_failed_marker_read_lists_no_conversations_and_the_next_list_retries()
	{
		var (store, feed, history) = Create();
		history.Markers = null;
		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(22, 22)]);
		history.PageLog[Tomas] = [Logged(21, "read", 21), Logged(22, "new", 22)];
		await LoadAsync(store, feed);

		await Assert.That(history.ConversationListings).IsEqualTo(0);
		await Assert.That(history.PageRecalled).IsEmpty();
		await Assert.That(feed.Conversations).IsEmpty();

		history.Markers = Markers(tomasId: 21);
		await LoadAsync(store, feed);

		await Assert.That(feed.Conversations.Single().Unread).IsEqualTo(1);
	}

	/// <summary>A failed read of the conversation list is retried on the next list, not given up on.</summary>
	[Test]
	public async Task A_failed_conversation_list_is_retried_on_the_next_list()
	{
		var (store, feed, history) = Create();
		history.PageConversations = null;
		await LoadAsync(store, feed);

		await Assert.That(feed.Conversations).IsEmpty();

		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(20, 20)]);
		await LoadAsync(store, feed);

		await Assert.That(feed.Conversations.Single().Key).IsEqualTo(TomasKey);
		await Assert.That(feed.PageLogging).IsTrue();
	}

	/// <summary>
	/// A listing answered while <c>page_log</c> was off listed nothing, so it is not taken as done: once the
	/// game turns logging on, the next list asks again and the kept conversations appear.
	/// </summary>
	[Test]
	public async Task A_listing_while_logging_was_off_is_asked_again_on_the_next_list()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, false, []);
		await LoadAsync(store, feed);

		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(20, 20)]);
		await LoadAsync(store, feed);

		await Assert.That(feed.Conversations.Single().Key).IsEqualTo(TomasKey);
		await Assert.That(feed.PageLogging).IsTrue();
	}

	/// <summary>
	/// Ids keep rising when the clock steps back, so a later page can carry an earlier time. Pages with ids
	/// are kept in id order, and the marker follows the last of them.
	/// </summary>
	[Test]
	public async Task Pages_with_ids_are_kept_in_id_order_when_the_clock_stepped_back()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(101, 0)]);
		history.PageLog[Tomas] = [Logged(100, "first", 60), Logged(101, "second, clock stepped back", 0)];
		await LoadAsync(store, feed);

		feed.Viewing = TomasKey;
		await feed.LoadHistoryAsync(TomasKey);

		await Assert.That(feed.Messages(TomasKey).Select(line => line.Id)).IsEquivalentTo(new long?[] { 100, 101 },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(history.ConversationMarks.Last().LastReadId).IsEqualTo(101);
	}

	/// <summary>
	/// Ids keep rising when the clock steps back, so a conversation's recency is its latest page's id: the
	/// one whose last page has the higher id is the more recent, whatever the times say.
	/// </summary>
	[Test]
	public async Task Conversations_are_ordered_by_their_latest_page_id()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, true,
		[
			new PageConversationSummary([Dace], ["Dace"], 100, T0.AddSeconds(60)),
			WithTomas(200, 0)
		]);

		await LoadAsync(store, feed);

		await Assert.That(feed.Conversations.Select(conversation => conversation.With.Single()))
			.IsEquivalentTo(new[] { "Tomas", "Dace" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// With the conversation limit reached, the one dropped is the least recent by id: a new page whose time
	/// is earlier than the rest (the clock stepped back) but whose id is the highest stays.
	/// </summary>
	[Test]
	public async Task A_new_page_stamped_before_the_rest_is_not_the_one_dropped()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, true,
			Enumerable.Range(0, OobCommFeed.ConversationLimit)
				.Select(n => new PageConversationSummary([$"#{1000 + n}:1"], [$"P{n}"], 1000 + n, T0.AddSeconds(100 + n)))
				.ToArray());
		await LoadAsync(store, feed);

		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(5000, "newest by id", 0));

		await Assert.That(feed.Conversations.Count).IsEqualTo(OobCommFeed.ConversationLimit);
		await Assert.That(feed.Conversations[0].Key).IsEqualTo(TomasKey).Because("its page has the highest id");
		await Assert.That(feed.Conversations.Any(conversation => conversation.WithObjIds.Contains("#1000:1"))).IsFalse()
			.Because("the conversation with the lowest last id is the least recent");
	}

	/// <summary>
	/// The server hides the page log while <c>page_log</c> is off, and so does the feed once it hears so:
	/// pages it pulled from the log go, pages pushed live stay, and a conversation known only from the
	/// listing goes with its history. Turning logging on again lists the conversations afresh.
	/// </summary>
	[Test]
	public async Task Page_logging_turned_off_hides_the_pulled_history_and_keeps_live_pages()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, true,
			[WithTomas(21, 21), new PageConversationSummary([Dace], ["Dace"], 10, T0.AddSeconds(10))]);
		history.PageLog[Tomas] = [Logged(20, "logged", 20), Logged(21, "logged and pushed", 21)];
		await LoadAsync(store, feed);
		await feed.LoadHistoryAsync(TomasKey);
		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(21, "logged and pushed", 21));
		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(22, "live", 22));

		history.PageLogging = false;
		await feed.LoadHistoryAsync(TomasKey);

		await Assert.That(feed.PageLogging).IsFalse();
		await Assert.That(feed.Messages(TomasKey).Select(line => line.Text)).IsEquivalentTo(new[] { "logged and pushed", "live" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(feed.Conversations.Select(conversation => conversation.Key)).IsEquivalentTo(new[] { TomasKey })
			.Because("the conversation with Dace was known only from the log");

		history.PageLogging = true;
		history.PageConversations = new PageConversations(Viewer, true,
			[WithTomas(22, 22), new PageConversationSummary([Dace], ["Dace"], 10, T0.AddSeconds(10))]);
		await LoadAsync(store, feed);

		await Assert.That(feed.Conversations.Count).IsEqualTo(2).Because("logging back on lists them again");
	}

	/// <summary>
	/// The conversation being viewed stays a conversation when logging goes off, even if it was known only
	/// from the listing: the view needs it to stay one, to say why there is no history and to page its people.
	/// Its pulled pages go, as everywhere.
	/// </summary>
	[Test]
	public async Task The_viewed_conversation_stays_when_page_logging_goes_off()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(20, 20)]);
		history.PageLog[Tomas] = [Logged(20, "logged", 20)];
		await LoadAsync(store, feed);
		feed.Viewing = TomasKey;
		await feed.LoadHistoryAsync(TomasKey);

		history.PageLogging = false;
		await feed.LoadHistoryAsync(TomasKey);

		await Assert.That(feed.Conversations.Single().Key).IsEqualTo(TomasKey);
		await Assert.That(feed.Conversations.Single().WithObjIds).IsEquivalentTo(new[] { Tomas });
		await Assert.That(feed.Messages(TomasKey)).IsEmpty();
	}

	/// <summary>For the same reason, the conversation being viewed is never the one dropped past the limit.</summary>
	[Test]
	public async Task The_viewed_conversation_is_not_dropped_past_the_limit()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, true,
			Enumerable.Range(0, OobCommFeed.ConversationLimit)
				.Select(n => new PageConversationSummary([$"#{1000 + n}:1"], [$"P{n}"], 1000 + n, T0.AddSeconds(100 + n)))
				.ToArray());
		await LoadAsync(store, feed);
		var oldest = feed.Conversations[^1].Key;
		feed.Viewing = oldest;

		store.Set(CommPayloadParser.MessagePackage, PageFromTomas(5000, "newest", 500));

		await Assert.That(feed.Conversations.Any(conversation => conversation.Key == oldest)).IsTrue();
		await Assert.That(feed.Conversations.Count).IsEqualTo(OobCommFeed.ConversationLimit);
	}

	/// <summary>
	/// A conversation with more people than the server lets a marker name (a group page past
	/// <see cref="CommLimits.ConversationMaxOthers"/>) is not marked: the server would
	/// refuse the write every time.
	/// </summary>
	[Test]
	public async Task A_conversation_too_large_for_a_marker_is_not_marked()
	{
		var (store, feed, history) = Create();
		await LoadAsync(store, feed);
		var others = Enumerable.Range(0, CommLimits.ConversationMaxOthers + 1).Select(n => $"#{2000 + n}:1").ToArray();
		store.Set(CommPayloadParser.MessagePackage,
			$$"""{"v":2,"id":50,"kind":"page","to":[{{string.Join(",", others.Append(Viewer).Select((_, i) => $"\"P{i}\""))}}],"toObjids":[{{string.Join(",", others.Append(Viewer).Select(o => $"\"{o}\""))}}],"from":"Tomas","fromObjid":"{{Tomas}}","text":"all","style":"say","ts":{{T0.ToUnixTimeMilliseconds()}}}""");

		feed.MarkRead(feed.Conversations.Single().Key);

		await Assert.That(history.ConversationMarks).IsEmpty();
	}

	/// <summary>Once listed, a later list does not ask for the conversations again: pushes keep it current.</summary>
	[Test]
	public async Task The_conversations_are_listed_once_per_sync()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(20, 20)]);
		await LoadAsync(store, feed);
		await LoadAsync(store, feed);

		await Assert.That(history.ConversationListings).IsEqualTo(1);
	}

	/// <summary>A feed held for someone other than the session's character does not use their page log.</summary>
	[Test]
	public async Task Another_characters_conversations_are_not_used()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations("#99:1", true, [WithTomas(20, 20)]);

		await LoadAsync(store, feed);

		await Assert.That(feed.Conversations).IsEmpty();
		await Assert.That(feed.PageLogging).IsNull();
	}

	[Test]
	public async Task A_clear_forgets_the_rebuilt_conversations()
	{
		var (store, feed, history) = Create();
		history.PageConversations = new PageConversations(Viewer, true, [WithTomas(20, 20)]);
		await LoadAsync(store, feed);

		store.Clear();

		await Assert.That(feed.Conversations).IsEmpty();
		await Assert.That(feed.PageLogging).IsNull();
	}
}
