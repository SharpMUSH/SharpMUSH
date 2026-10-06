using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Play;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Components.Characters;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// README §5.1 / board 06 channel view: a channel or a page conversation in main, its lines grouped by day
/// and by author, a "New" divider where the unread lines begin, plain-text bodies, and a composer that
/// sends @chat or page.
/// </summary>
public class CommViewTests : TrackingBunitContext
{
	private readonly TestCommFeed _feed = new();
	private readonly List<string> _sent = [];

	public CommViewTests()
	{
		CharactersApiFake.Install(this);
		Services.AddSingleton<ICommFeed>(_feed);
	}

	// Noon today, not the clock: lines minutes before "now" must fall on today however soon after
	// midnight the suite runs, or the day dividers read Yesterday where the test expects Today.
	private static readonly DateTimeOffset Now = new(DateTime.Today.AddHours(12));

	private static CommMessage Line(string from, string text, DateTimeOffset at, string? objid = null) =>
		new("channel", "Public", [], from, objid, text, at);

	private IRenderedComponent<CommView> RenderView(string key, Action? onClose = null) =>
		Render<CommView>(p => p.Add(x => x.Key, key).Add(x => x.OnSend, c => _sent.Add(c)).Add(x => x.OnClose, () => onClose?.Invoke()));

	[Test]
	public async Task AChannel_HasItsHeader_ItsLinesByDayAndAuthor_AndTheNewDivider()
	{
		_feed.ChannelList = [new CommChannel("Public", 2)];
		_feed.Lines["Public"] =
		[
			Line("Wren Halloway", "yesterday's line", Now.AddDays(-1)),
			Line("Wren Halloway", "anyone up for a scene?", Now.AddMinutes(-20)),
			Line("Wren Halloway", "no plot, just books", Now.AddMinutes(-19)),
			Line("Magister Oake", "Is the Calendar canon now?", Now.AddMinutes(-5)),
			Line("Wren Halloway", "canon, staff approved it", Now.AddMinutes(-3)),
		];
		var cut = RenderView("Public");
		await Assert.That(cut.Find(".comm-title").TextContent).IsEqualTo("# Public");
		var dividers = cut.FindAll(".comm-day").Select(d => d.TextContent.Trim()).ToList();
		await Assert.That(dividers).IsEquivalentTo(new[] { "Yesterday", "Today" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(cut.FindAll(".comm-group").Count).IsEqualTo(4).Because("Wren's two lines a minute apart share one header");
		var items = cut.FindAll(".comm-group, .comm-new").Select(e => e.ClassList.Contains("comm-new") ? "NEW" : e.QuerySelector(".comm-name")!.TextContent).ToList();
		await Assert.That(items).IsEquivalentTo(new[] { "Wren Halloway", "Wren Halloway", "NEW", "Magister Oake", "Wren Halloway" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task Opening_SetsViewing_AndMarksRead_ClosingClearsViewing()
	{
		_feed.ChannelList = [new CommChannel("Public", 0)];
		var cut = RenderView("Public");
		await Assert.That(_feed.Viewing).IsEqualTo("Public");
		await Assert.That(_feed.MarkedRead).Contains("Public");
		await cut.InvokeAsync(() => cut.Instance.Dispose());
		await Assert.That(_feed.Viewing).IsNull();
	}

	/// <summary>The view pulls the channel's recent lines when it opens; live lines then append to them.</summary>
	[Test]
	public async Task Opening_PullsTheHistory_OncePerKey()
	{
		_feed.ChannelList = [new CommChannel("Public", 0), new CommChannel("OOC", 0)];
		var cut = RenderView("Public");
		cut.Render(p => p.Add(x => x.Key, "Public"));
		cut.Render(p => p.Add(x => x.Key, "OOC"));

		await Assert.That(_feed.Loaded).IsEquivalentTo(new[] { "Public", "OOC" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task Bodies_AreText_NeverMarkup()
	{
		_feed.ChannelList = [new CommChannel("Public", 0)];
		_feed.Lines["Public"] = [Line("Mallory", "<img src=x onerror=alert(1)> [ansi(r,hi)]", Now)];
		var cut = RenderView("Public");
		await Assert.That(cut.FindAll(".comm-text img").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".comm-text").TextContent).IsEqualTo("<img src=x onerror=alert(1)> [ansi(r,hi)]");
	}

	[Test]
	public async Task NewLines_Arriving_Rerender()
	{
		_feed.ChannelList = [new CommChannel("Public", 0)];
		var cut = RenderView("Public");
		await Assert.That(cut.Find(".comm-empty")).IsNotNull();
		_feed.Lines["Public"] = [Line("Wren Halloway", "hello", Now)];
		_feed.Raise();
		cut.WaitForAssertion(() => cut.Find(".comm-text"), TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task TheComposer_SendsChat_ToTheChannel()
	{
		_feed.ChannelList = [new CommChannel("Public", 0)];
		var cut = RenderView("Public");
		await Assert.That(cut.Find(".comm-compose input").GetAttribute("placeholder")).IsEqualTo("Message #Public");
		cut.Find(".comm-compose input").Input("hello; world");
		cut.Find(".comm-compose button.comm-send").Click();
		await Assert.That(_sent).IsEquivalentTo(new[] { "@chat Public=hello%; world" });
	}

	/// <summary>
	/// By full objid, not the bare dbref: a conversation outlives the player it was with, and once that dbref
	/// is recycled a bare <c>#312</c> pages whoever holds it now. The page command resolves an objid and
	/// refuses a stale one.
	/// </summary>
	[Test]
	public async Task AConversation_PagesItsPeopleByObjid_SoNamesWithSpacesArriveAndARecycledDbrefDoesNot()
	{
		var key = "page #312:1 #313:1 #315:1";
		_feed.ConversationList = [new CommConversation(key, ["Tomas Reyes", "Dace Kellan"], ["#312:1", "#315:1"], 0, Now)];
		var cut = RenderView(key);
		await Assert.That(cut.Find(".comm-title").TextContent).IsEqualTo("Tomas Reyes, Dace Kellan");
		cut.Find(".comm-compose input").Input("meet at nine");
		cut.Find(".comm-compose input").KeyDown("Enter");
		await Assert.That(_sent).IsEquivalentTo(new[] { "page #312:1 #315:1=meet at nine" });
	}

	[Test]
	public async Task OpeningAConversation_PullsItsPages()
	{
		var key = "page #312:1 #5:1";
		_feed.ConversationList = [new CommConversation(key, ["Tomas Reyes"], ["#312:1"], 0, Now)];

		RenderView(key);

		await Assert.That(_feed.Loaded).IsEquivalentTo(new[] { key });
	}

	/// <summary>A game that keeps no page log says so, so an empty history does not read as nobody having paged.</summary>
	[Test]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(null, false)]
	public async Task AConversation_SaysWhenTheGameKeepsNoPageHistory(bool? logging, bool shown)
	{
		var key = "page #312:1 #5:1";
		_feed.ConversationList = [new CommConversation(key, ["Tomas Reyes"], ["#312:1"], 0, Now)];
		_feed.PageLogging = logging;

		var cut = RenderView(key);

		await Assert.That(cut.FindAll(".comm-unlogged").Count).IsEqualTo(shown ? 1 : 0);
	}

	[Test]
	public async Task AChannel_NeverSaysSoAboutPages()
	{
		_feed.PageLogging = false;

		var cut = RenderView("Public");

		await Assert.That(cut.FindAll(".comm-unlogged")).IsEmpty();
	}

	[Test]
	public async Task APersonWithoutAnObjid_IsPagedByQuotedName()
	{
		var key = "page Wren Halloway";
		_feed.ConversationList = [new CommConversation(key, ["Wren Halloway"], [null], 0, Now)];
		var cut = RenderView(key);
		cut.Find(".comm-compose input").Input("hi");
		cut.Find(".comm-compose button.comm-send").Click();
		await Assert.That(_sent).IsEquivalentTo(new[] { "page \"Wren Halloway\"=hi" });
	}

	[Test]
	[Arguments(2)]
	[Arguments(250)]
	public async Task EveryRetainedLineUnread_PutsTheDividerBeforeTheFirst(int unread)
	{
		// 2 is every line in the history; 250 is more than the bounded history keeps.
		_feed.ChannelList = [new CommChannel("Public", unread)];
		_feed.Lines["Public"] = [Line("A", "one", Now.AddMinutes(-3)), Line("B", "two", Now.AddMinutes(-2))];
		var cut = RenderView("Public");
		var order = cut.FindAll(".comm-group, .comm-new").Select(e => e.ClassList.Contains("comm-new") ? "NEW" : e.QuerySelector(".comm-text")!.TextContent).ToList();
		await Assert.That(order).IsEquivalentTo(new[] { "NEW", "one", "two" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task TheNewDivider_StaysOnItsLine_WhenTheHistoryIsTrimmed()
	{
		// The feed keeps a bounded history: when a line arrives at the cap the oldest drops, and an index
		// would slide the divider one line later each time.
		var first = Line("A", "one", Now.AddMinutes(-3));
		var unread = Line("B", "two", Now.AddMinutes(-2));
		_feed.ChannelList = [new CommChannel("Public", 1)];
		_feed.Lines["Public"] = [first, unread];
		var cut = RenderView("Public");
		_feed.Lines["Public"] = [unread, Line("C", "three", Now)];
		_feed.Raise();
		cut.WaitForAssertion(() => cut.Find(".comm-group .comm-text"), TimeSpan.FromSeconds(5));
		var order = cut.FindAll(".comm-group, .comm-new").Select(e => e.ClassList.Contains("comm-new") ? "NEW" : e.QuerySelector(".comm-text")!.TextContent).ToList();
		await Assert.That(order.IndexOf("NEW")).IsEqualTo(order.IndexOf("two") - 1);
	}

	[Test]
	public async Task AFeedClearedUnderTheView_IsToldAgainWhatIsBeingViewed()
	{
		_feed.ChannelList = [new CommChannel("Public", 0)];
		RenderView("Public");
		_feed.Viewing = null; // a reconnect clears the feed
		_feed.Raise();
		await Assert.That(_feed.Viewing).IsEqualTo("Public");
	}

	[Test]
	public async Task TheCloseButton_GoesBack()
	{
		_feed.ChannelList = [new CommChannel("Public", 0)];
		var closed = false;
		var cut = RenderView("Public", () => closed = true);
		cut.Find("button.comm-close").Click();
		await Assert.That(closed).IsTrue();
	}
}
