using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// <see cref="IPageLogStore"/> against the Lightning provider directly: each delivered page kept as every
/// participant's own copy, read back only as the character it belongs to, gone with the character, and
/// purged by age.
/// </summary>
public class PageLogTests
{
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private string _path = null!;
	private LightningDatabase _db = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		if (Directory.Exists(_path))
		{
			try
			{
				Directory.Delete(_path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort, as the other Lightning fixtures: a lingering mdb.lck can outlive the writer.
			}
		}
	}

	private static readonly DateTimeOffset At = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

	/// <summary>An id as the id source takes one: microseconds since 1970.</summary>
	private static long IdAt(DateTimeOffset at) => (at - DateTimeOffset.UnixEpoch).Ticks / TimeSpan.TicksPerMicrosecond;

	private async Task<DBRef> NewPlayer(string name)
	{
		var created = await _db.CreatePlayerAsync(name, "pw", new DBRef(0), new DBRef(0), 0);
		return (await _db.GetObjectNodeAsync(created)).Expect<SharpPlayer>().Object.DBRef;
	}

	private static SharpPage Page(DBRef from, string fromName, DBRef[] to, string[] toNames, string message,
		DateTimeOffset at, string style = "say") =>
		new(IdAt(at), from, fromName, to, toNames, style, message, at);

	/// <summary>Records the page for everyone in it, as the page command does.</summary>
	private async Task<SharpPage> Send(SharpPage page)
	{
		await _db.RecordPageAsync(page, page.Participants);
		return page;
	}

	[Test]
	public async Task APage_IsKeptForTheSenderAndEachRecipient()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		var page = await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "hello", At, "pose"));

		var ilsas = await _db.GetPageLogAsync(ilsa, [wren], 0);
		var wrens = await _db.GetPageLogAsync(wren, [ilsa], 0);

		await Assert.That(ilsas).IsEquivalentTo(new[] { page }, new PageComparer());
		await Assert.That(wrens).IsEquivalentTo(new[] { page }, new PageComparer());
		await Assert.That(ilsas.Single().Style).IsEqualTo("pose");
		await Assert.That(ilsas.Single().RecipientNames).IsEquivalentTo(new[] { "Wren" });
	}

	/// <summary>Only the owners named get a copy: the page command names only players.</summary>
	[Test]
	public async Task APage_IsKeptOnlyForTheOwnersNamed()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		await _db.RecordPageAsync(Page(ilsa, "Ilsa", [wren], ["Wren"], "hello", At), [wren]);

		await Assert.That(await _db.GetPageLogAsync(ilsa, [wren], 0)).IsEmpty();
		await Assert.That((await _db.GetPageLogAsync(wren, [ilsa], 0)).Count).IsEqualTo(1);
	}

	/// <summary>
	/// A character reads only their own copies, and only of the conversation asked for: a third party in no
	/// page reads nothing, whoever they name.
	/// </summary>
	[Test]
	public async Task ACharacter_ReadsOnlyTheirOwnConversation()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		var tomas = await NewPlayer("Tomas");
		var outsider = await NewPlayer("Outsider");
		await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "to wren", At));
		await Send(Page(ilsa, "Ilsa", [tomas], ["Tomas"], "to tomas", At.AddSeconds(1)));
		await Send(Page(ilsa, "Ilsa", [wren, tomas], ["Wren", "Tomas"], "to both", At.AddSeconds(2)));

		var withWren = await _db.GetPageLogAsync(ilsa, [wren], 0);
		var withBoth = await _db.GetPageLogAsync(ilsa, [tomas, wren], 0);

		await Assert.That(withWren.Select(page => page.Message)).IsEquivalentTo(new[] { "to wren" });
		await Assert.That(withBoth.Select(page => page.Message)).IsEquivalentTo(new[] { "to both" })
			.Because("the same people in any order are one conversation, and a group is not a pair");
		await Assert.That(await _db.GetPageLogAsync(outsider, [ilsa], 0)).IsEmpty();
		await Assert.That(await _db.GetPageLogAsync(outsider, [wren], 0)).IsEmpty();
		await Assert.That(await _db.GetPageConversationsAsync(outsider)).IsEmpty();
	}

	[Test]
	public async Task TheLastLines_AreReturnedOldestFirst()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		for (var i = 0; i < 5; i++)
		{
			await Send(Page(i % 2 == 0 ? ilsa : wren, i % 2 == 0 ? "Ilsa" : "Wren", [i % 2 == 0 ? wren : ilsa],
				[i % 2 == 0 ? "Wren" : "Ilsa"], $"line {i}", At.AddSeconds(i)));
		}

		var last = await _db.GetPageLogAsync(wren, [ilsa], 3);

		await Assert.That(last.Select(page => page.Message)).IsEquivalentTo(new[] { "line 2", "line 3", "line 4" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task Conversations_NameTheOthersAndTheLatestPage()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		var tomas = await NewPlayer("Tomas");
		await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "one", At));
		var latest = await Send(Page(wren, "Wren", [ilsa], ["Ilsa"], "two", At.AddMinutes(1)));
		var group = await Send(Page(tomas, "Tomas", [ilsa, wren], ["Ilsa", "Wren"], "all", At.AddMinutes(2)));

		var conversations = await _db.GetPageConversationsAsync(ilsa);

		await Assert.That(conversations.Count).IsEqualTo(2);
		var pair = conversations.Single(c => c.With.Count == 1);
		await Assert.That(pair.With.Single()).IsEqualTo(wren);
		await Assert.That(pair.Names.Single()).IsEqualTo("Wren");
		await Assert.That(pair.LastId).IsEqualTo(latest.Id);
		await Assert.That(pair.LastAt).IsEqualTo(latest.Timestamp);
		var three = conversations.Single(c => c.With.Count == 2);
		await Assert.That(three.With).IsEquivalentTo(PageConversation.Normalize([tomas, wren]),
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(three.LastId).IsEqualTo(group.Id);
	}

	/// <summary>
	/// The listing is the latest conversations first, and reads no more than the limit: a character with
	/// years of conversations gets the newest, not all of them.
	/// </summary>
	[Test]
	public async Task Conversations_AreListedLatestFirst_AndBoundedByTheLimit()
	{
		var ilsa = await NewPlayer("Ilsa");
		var others = new List<DBRef>();
		for (var i = 0; i < 5; i++)
		{
			var other = await NewPlayer($"Other{i}");
			others.Add(other);
			await Send(Page(ilsa, "Ilsa", [other], [$"Other{i}"], $"to {i}", At.AddMinutes(i)));
		}

		// The oldest conversation speaks again, and is the latest now.
		await Send(Page(others[0], "Other0", [ilsa], ["Ilsa"], "back again", At.AddHours(1)));

		var latest = await _db.GetPageConversationsAsync(ilsa, 3);

		await Assert.That(latest.Select(conversation => conversation.With.Single()))
			.IsEquivalentTo(new[] { others[0], others[4], others[3] }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That((await _db.GetPageConversationsAsync(ilsa)).Count).IsEqualTo(5).Because("no limit lists them all");
		await Assert.That(_db.Store.Read(tx => tx.Range(Tables.PageConversationLatest,
				SharpMUSH.Library.Plugins.Storage.Lightning.Keys.Dbref(ilsa.Number)).Count())).IsEqualTo(5)
			.Because("one latest-order entry per conversation of Ilsa's, the old one replaced when it spoke again");
	}

	/// <summary>A page to oneself is a conversation with oneself.</summary>
	[Test]
	public async Task APageToOneself_IsAConversationWithOneself()
	{
		var ilsa = await NewPlayer("Ilsa");
		await Send(Page(ilsa, "Ilsa", [ilsa], ["Ilsa"], "note to self", At));

		await Assert.That((await _db.GetPageLogAsync(ilsa, [ilsa], 0)).Single().Message).IsEqualTo("note to self");
		await Assert.That((await _db.GetPageConversationsAsync(ilsa)).Single().With).IsEquivalentTo(new[] { ilsa });
	}

	/// <summary>
	/// The log is the character's, not the dbref's: a player who takes a recycled number does not inherit
	/// what the previous holder paged.
	/// </summary>
	[Test]
	public async Task AnotherObjectOnTheSameNumber_ReadsNoneOfIt()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "hello", At));

		var successor = new DBRef(ilsa.Number, ilsa.CreationMilliseconds + 1);

		await Assert.That(await _db.GetPageLogAsync(successor, [wren], 0)).IsEmpty();
		await Assert.That(await _db.GetPageConversationsAsync(successor)).IsEmpty();
	}

	[Test]
	public async Task DestroyingTheCharacter_DropsTheirCopiesOnly()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "hello", At));

		await _db.DeleteObjectAsync(ilsa);

		var (logs, conversations, times) = _db.Store.Read(tx => (
			tx.Range(Tables.PageLog, []).Count(),
			tx.Range(Tables.PageConversation, []).Count(),
			tx.Range(Tables.PageLogTime, []).Count()));
		await Assert.That(logs).IsEqualTo(1).Because("Wren's copy stays");
		await Assert.That(conversations).IsEqualTo(1);
		await Assert.That(times).IsEqualTo(1).Because("the destroyed character's purge index goes with their copy");
		await Assert.That(_db.Store.Read(tx => tx.Range(Tables.PageConversationLatest, []).Count())).IsEqualTo(1)
			.Because("the destroyed character's latest-order entry goes with their conversation");
		await Assert.That((await _db.GetPageLogAsync(wren, [ilsa], 0)).Single().Message).IsEqualTo("hello");
	}

	[Test]
	public async Task Purging_DeletesOlderPagesAndEmptiedConversations()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		var tomas = await NewPlayer("Tomas");
		await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "old", At));
		await Send(Page(ilsa, "Ilsa", [tomas], ["Tomas"], "old to tomas", At.AddMinutes(1)));
		await Send(Page(wren, "Wren", [ilsa], ["Ilsa"], "new", At.AddDays(2)));

		var purged = await _db.PurgePageLogAsync(At.AddDays(1));

		await Assert.That(purged).IsEqualTo(4).Because("two pages, two copies each");
		await Assert.That((await _db.GetPageLogAsync(ilsa, [wren], 0)).Select(page => page.Message))
			.IsEquivalentTo(new[] { "new" });
		await Assert.That((await _db.GetPageConversationsAsync(ilsa)).Single().With.Single()).IsEqualTo(wren)
			.Because("the conversation with Tomas has nothing left in it");
		await Assert.That(await _db.GetPageConversationsAsync(tomas)).IsEmpty();
		await Assert.That(_db.Store.Read(tx => tx.Range(Tables.PageLogTime, []).Count())).IsEqualTo(2);
		await Assert.That(_db.Store.Read(tx => tx.Range(Tables.PageConversationLatest, []).Count())).IsEqualTo(2)
			.Because("an emptied conversation leaves the latest order too");
	}

	/// <summary>
	/// Retention is by when a page was sent. An id can run ahead of the clock (the id source reserves a
	/// block across a restart, or holds its value when the clock steps back), so an id is no measure of age.
	/// </summary>
	[Test]
	public async Task Purging_GoesByWhenAPageWasSent_NotByItsId()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "old, with an id ahead of the clock", At) with { Id = IdAt(At.AddYears(1)) });
		await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "recent", At.AddDays(2)) with { Id = IdAt(At.AddYears(1)) + 1 });

		var purged = await _db.PurgePageLogAsync(At.AddDays(1));

		await Assert.That(purged).IsEqualTo(2);
		await Assert.That((await _db.GetPageLogAsync(ilsa, [wren], 0)).Select(page => page.Message))
			.IsEquivalentTo(new[] { "recent" });
	}

	/// <summary>
	/// Ids keep rising when the clock steps back, so the page with the highest id can be the oldest by time.
	/// Purging it leaves the conversation, and its summary then names the newest page still kept.
	/// </summary>
	[Test]
	public async Task Purging_TheLatestByIdButOldestByTime_RefreshesTheSummary()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		var kept = await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "kept", At.AddDays(2)) with { Id = 10 });
		await Send(Page(wren, "Wren Renamed", [ilsa], ["Ilsa"], "old by the clock", At) with { Id = 20 });

		await _db.PurgePageLogAsync(At.AddDays(1));

		var summary = (await _db.GetPageConversationsAsync(ilsa)).Single();
		await Assert.That(summary.LastId).IsEqualTo(kept.Id);
		await Assert.That(summary.LastAt).IsEqualTo(kept.Timestamp);
		await Assert.That(summary.Names.Single()).IsEqualTo("Wren").Because("the names are the kept page's, not the purged one's");
		await Assert.That((await _db.GetPageConversationsAsync(ilsa, 1)).Single().LastId).IsEqualTo(kept.Id)
			.Because("the latest order follows the refreshed summary");
	}

	/// <summary>
	/// The recall reads a conversation from its newest page back, and stops at the lines asked for: the
	/// store's reverse range yields a prefix's entries last first, and nothing either side of it.
	/// </summary>
	[Test]
	public async Task TheReverseRange_YieldsAPrefixNewestFirst_AndStopsAtItsEdges()
	{
		byte[] Key(string prefix, int n) => [.. System.Text.Encoding.UTF8.GetBytes(prefix), (byte)n];
		await _db.Store.WriteAsync(tx =>
		{
			foreach (var prefix in new[] { "a", "b", "c" })
			{
				for (var n = 1; n <= 3; n++) tx.Put(Tables.PageConversation, Key(prefix, n), [(byte)n]);
			}
		});
		await _db.Store.WriteAsync(tx => tx.Put(Tables.PageConversation, [0xFF, 0xFF], [9]));

		var middle = _db.Store.Read(tx => tx.RangeReverse(Tables.PageConversation, Key("b", 0)[..1]).Select(e => e.Value[0]).ToList());
		var last = _db.Store.Read(tx => tx.RangeReverse(Tables.PageConversation, [0xFF]).Select(e => e.Value[0]).ToList());
		var firstTwo = _db.Store.Read(tx => tx.RangeReverse(Tables.PageConversation, Key("a", 0)[..1]).Take(2).Select(e => e.Value[0]).ToList());
		var none = _db.Store.Read(tx => tx.RangeReverse(Tables.PageConversation, Key("d", 0)[..1]).Count());

		await Assert.That(middle).IsEquivalentTo(new byte[] { 3, 2, 1 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(last).IsEquivalentTo(new byte[] { 9 }).Because("a prefix of 0xFF bytes has no successor key to seek to");
		await Assert.That(firstTwo).IsEquivalentTo(new byte[] { 3, 2 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(none).IsEqualTo(0);
	}

	/// <summary>
	/// A conversation counts its pages: each one recorded adds one, also one that arrives out of order, and a
	/// purge leaves the count of what is still kept.
	/// </summary>
	[Test]
	public async Task Conversations_CountTheirPages_ThroughRecordingAndPurging()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "old", At));
		await Send(Page(wren, "Wren", [ilsa], ["Ilsa"], "new", At.AddDays(2)));
		await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "late arrival", At.AddMinutes(1)));

		await Assert.That((await _db.GetPageConversationsAsync(ilsa)).Single().Pages).IsEqualTo(3);
		await Assert.That((await _db.GetPageConversationsAsync(wren)).Single().Pages).IsEqualTo(3);

		await _db.PurgePageLogAsync(At.AddDays(1));

		await Assert.That((await _db.GetPageConversationsAsync(ilsa)).Single().Pages).IsEqualTo(1);
	}

	/// <summary>A summary written before conversations counted their pages is counted when it is read.</summary>
	[Test]
	public async Task AConversationSummaryWithoutACount_IsCountedWhenRead()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "one", At));
		await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "two", At.AddMinutes(1)));
		var key = SharpMUSH.Library.Plugins.Storage.Lightning.Keys.Composite(ilsa.Number, PageConversation.Key([wren]));
		await _db.Store.WriteAsync(tx =>
		{
			tx.TryGet(Tables.PageConversation, key, out var bytes);
			var record = Codec.Deserialize<SharpMUSH.Database.Lightning.Records.PageConversationRecord>(bytes);
			tx.Put(Tables.PageConversation, key, Codec.Serialize(record with { Pages = null }));
		});

		await Assert.That((await _db.GetPageConversationsAsync(ilsa)).Single().Pages).IsEqualTo(2);
	}

	/// <summary>
	/// The latest pages across every conversation, oldest first: what <c>page/recall</c> with no player
	/// shows. Only the character's own copies.
	/// </summary>
	[Test]
	public async Task RecentPages_AreTheNewestAcrossConversations_OldestFirst()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		var tomas = await NewPlayer("Tomas");
		var outsider = await NewPlayer("Outsider");
		await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "1 to wren", At));
		await Send(Page(ilsa, "Ilsa", [tomas], ["Tomas"], "2 to tomas", At.AddMinutes(1)));
		await Send(Page(wren, "Wren", [ilsa], ["Ilsa"], "3 from wren", At.AddMinutes(2)));
		await Send(Page(ilsa, "Ilsa", [wren, tomas], ["Wren", "Tomas"], "4 to both", At.AddMinutes(3)));
		await Send(Page(wren, "Wren", [tomas], ["Tomas"], "not ilsa's", At.AddMinutes(4)));

		var lastThree = await _db.GetRecentPagesAsync(ilsa, 3);
		var all = await _db.GetRecentPagesAsync(ilsa, 0);

		await Assert.That(lastThree.Select(page => page.Message)).IsEquivalentTo(
			new[] { "2 to tomas", "3 from wren", "4 to both" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(all.Select(page => page.Message)).IsEquivalentTo(
			new[] { "1 to wren", "2 to tomas", "3 from wren", "4 to both" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(await _db.GetRecentPagesAsync(outsider, 10)).IsEmpty();
	}

	/// <summary>The pager's own name, beside the name the page gave them, survives the round trip.</summary>
	[Test]
	public async Task ThePagersOwnName_IsKept()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		await Send(Page(ilsa, "Ilsa (il)", [wren], ["Wren"], "hello", At) with { SenderPlainName = "Ilsa" });

		var page = (await _db.GetPageLogAsync(wren, [ilsa], 0)).Single();
		await Assert.That(page.SenderName).IsEqualTo("Ilsa (il)");
		await Assert.That(page.SenderPlainName).IsEqualTo("Ilsa");
		await Assert.That((await _db.GetPageConversationsAsync(wren)).Single().Names.Single()).IsEqualTo("Ilsa")
			.Because("a conversation names its people by their names, not their names and page aliases");
	}

	[Test]
	public async Task Purging_WithNothingOldEnough_DeletesNothing()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		await Send(Page(ilsa, "Ilsa", [wren], ["Wren"], "hello", At));

		await Assert.That(await _db.PurgePageLogAsync(At)).IsEqualTo(0);
		await Assert.That((await _db.GetPageLogAsync(ilsa, [wren], 0)).Count).IsEqualTo(1);
	}

	[Test]
	public async Task AnOwnerNamedWithoutItsCreationTime_IsRefused()
	{
		var wren = await NewPlayer("Wren");
		await Assert.That(async () => await _db.RecordPageAsync(Page(new DBRef(1), "God", [wren], ["Wren"], "x", At), [new DBRef(1)]))
			.Throws<ArgumentException>();
		await Assert.That(async () => await _db.GetPageLogAsync(new DBRef(1), [wren], 0)).Throws<ArgumentException>();
	}

	[Test]
	public async Task AnOwnerNotInThePage_IsRefused()
	{
		var ilsa = await NewPlayer("Ilsa");
		var wren = await NewPlayer("Wren");
		var outsider = await NewPlayer("Outsider");
		await Assert.That(async () => await _db.RecordPageAsync(Page(ilsa, "Ilsa", [wren], ["Wren"], "x", At), [outsider]))
			.Throws<ArgumentException>();
	}

	private sealed class PageComparer : IEqualityComparer<SharpPage>
	{
		public bool Equals(SharpPage? x, SharpPage? y) =>
			x is not null && y is not null && x.Id == y.Id && x.Sender == y.Sender && x.SenderName == y.SenderName
			&& x.Recipients.SequenceEqual(y.Recipients) && x.RecipientNames.SequenceEqual(y.RecipientNames)
			&& x.Style == y.Style && x.Message == y.Message && x.Timestamp == y.Timestamp;

		public int GetHashCode(SharpPage obj) => obj.Id.GetHashCode();
	}
}
