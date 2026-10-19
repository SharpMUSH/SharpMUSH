using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Database.Lightning;

public class WikiSeekScanTests : LightningDatabaseFixture
{
	private async Task<WikiPage> Add(string title, bool published = true, string author = "#1", string[]? categories = null)
	{
		var at = DateTimeOffset.UnixEpoch;
		return (await Db.CreatePageAsync(new WikiPage("", title, title, "main", "body", "body", "body",
			author, author, at, at, 1)
		{ Published = published, Categories = categories ?? [] })).Expect<WikiPage>();
	}

	private static WikiPageScanContinuation Resume(WikiPageScanBatch batch) => batch.Next is WikiPageScanContinuation next
		? next : throw new InvalidOperationException("Expected continuation.");

	[Test]
	public async Task SeekBatchesPreserveOrderAndExactFullTerminalBatchWithoutLookaheadLoss()
	{
		var pages = new[] { await Add("A"), await Add("B"), await Add("C"), await Add("D") };
		var first = await Db.ScanPagesAsync(WikiVisibility.PublishedOnly, new WikiPageScanStart(), 2);
		await Assert.That(first.Pages.Select(page => page.Id).SequenceEqual(pages.Take(2).Select(page => page.Id))).IsTrue();
		var second = await Db.ScanPagesAsync(WikiVisibility.PublishedOnly, Resume(first), 2);
		await Assert.That(second.Pages.Select(page => page.Id).SequenceEqual(pages.Skip(2).Select(page => page.Id))).IsTrue();
		await Assert.That(Resume(second).Offset > Resume(first).Offset).IsTrue();
		var end = await Db.ScanPagesAsync(WikiVisibility.PublishedOnly, Resume(second), 2);
		await Assert.That(end.Pages).IsEmpty();
		await Assert.That(end.Next is WikiPageScanEnd).IsTrue();
	}

	[Test]
	public async Task SeekResumesAfterDeletedBoundaryAndReappliesExplicitVisibility()
	{
		var first = await Add("A");
		var hidden = await Add("B");
		var last = await Add("C", false, "#42");
		var batch = await Db.ScanPagesAsync(WikiVisibility.All, new WikiPageScanStart(), 1);
		await Assert.That(batch.Pages.Single().Id).IsEqualTo(first.Id);
		await Db.DeletePageAsync(first.Id);
		var visibility = new WikiVisibility(false, "#42")
		{
			IncludedPageIds = new HashSet<string> { hidden.Id, last.Id },
			ExcludedPageIds = new HashSet<string> { hidden.Id }
		};
		var resumed = await Db.ScanPagesAsync(visibility, Resume(batch), 2);
		await Assert.That(resumed.Pages.Single().Id).IsEqualTo(last.Id);
		await Assert.That(resumed.Next is WikiPageScanEnd).IsTrue();
	}

	[Test]
	public async Task SeekDoesNotRehydrateEarlierBodiesAndStillAppliesSecondCategoryRequirements()
	{
		var first = await Add("A");
		await Add("B", categories: ["public", "secret"]);
		var last = await Add("C");
		var visibility = WikiVisibility.PublishedOnly with
		{
			Hidden = new WikiReadRestrictions(false, new HashSet<string>(), new HashSet<string> { "secret" }, new HashSet<string>())
		};
		var batch = await Db.ScanPagesAsync(visibility, new WikiPageScanStart(), 1);
		await Db.Store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(long.Parse(first.Id["wiki_page/".Length..])), "not json"u8));
		var resumed = await Db.ScanPagesAsync(visibility, Resume(batch), 2);
		await Assert.That(resumed.Pages.Single().Id).IsEqualTo(last.Id);
		await Assert.That(resumed.Next is WikiPageScanEnd).IsTrue();
	}

	[Test]
	public async Task SeekSkipsMissingApplicationRowsUntilActualIteratorEnd()
	{
		var first = await Add("A");
		var missing = await Add("B");
		var last = await Add("C");
		// A deliberately stale logical index in an otherwise valid native transaction.
		await Db.Store.WriteAsync(tx => tx.Delete(Tables.WikiPage, Keys.Dbref(long.Parse(missing.Id["wiki_page/".Length..]))));
		var batch = await Db.ScanPagesAsync(WikiVisibility.PublishedOnly, new WikiPageScanStart(), 2);
		await Assert.That(batch.Pages.Select(page => page.Id)).IsEquivalentTo(new[] { first.Id, last.Id });
		var end = await Db.ScanPagesAsync(WikiVisibility.PublishedOnly, Resume(batch), 2);
		await Assert.That(end.Pages).IsEmpty();
		await Assert.That(end.Next is WikiPageScanEnd).IsTrue();
	}

	[Test]
	public async Task SeekProvesEndAfterHiddenTailAndHandlesEmptySelection()
	{
		var first = await Add("A");
		await Add("B", false, "#43");
		await Add("C", categories: ["secret"]);
		var visibility = WikiVisibility.PublishedOnly with
		{
			Hidden = new WikiReadRestrictions(false, new HashSet<string>(), new HashSet<string> { "secret" }, new HashSet<string>())
		};
		var batch = await Db.ScanPagesAsync(visibility, new WikiPageScanStart(), 1);
		await Assert.That(batch.Pages.Single().Id).IsEqualTo(first.Id);
		var tail = await Db.ScanPagesAsync(visibility, Resume(batch), 1);
		await Assert.That(tail.Pages).IsEmpty();
		await Assert.That(tail.Next is WikiPageScanEnd).IsTrue();
		var empty = await Db.ScanPagesAsync(visibility with { IncludedPageIds = new HashSet<string>() }, new WikiPageScanStart(), 1);
		await Assert.That(empty.Pages).IsEmpty();
		await Assert.That(empty.Next is WikiPageScanEnd).IsTrue();
	}

	private async Task Reject(WikiPageScanPosition position, int take)
	{
		Exception? failure = null;
		try { await Db.ScanPagesAsync(WikiVisibility.All, position, take); }
		catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException) { failure = exception; }
		await Assert.That(failure is not null).IsTrue();
	}

	[Test]
	public async Task SeekValidatesBatchOffsetTokenVersionLengthEncodingAndKeyShape()
	{
		await Reject(new WikiPageScanStart(), 0);
		await Reject(new WikiPageScanStart(), -1);
		await Reject(new WikiPageScanContinuation(-1, "offset:v1"), 1);
		await Reject(new WikiPageScanContinuation(int.MaxValue, "offset:v1"), 1);
		await Reject(new WikiPageScanContinuation(1, "offset:v1"), 1);
		await Reject(new WikiPageScanContinuation(1, "lightning-wiki-scan:v2:AAAA"), 1);
		await Reject(new WikiPageScanContinuation(1, "lightning-wiki-scan:v1:!"), 1);
		await Reject(new WikiPageScanContinuation(1, "lightning-wiki-scan:v1:" + new string('A', 100000)), 1);
		await Reject(new WikiPageScanContinuation(1, "lightning-wiki-scan:v1:" + Convert.ToBase64String(new byte[11])), 1);
		await Reject(new WikiPageScanContinuation(1, "lightning-wiki-scan:v1:" + Convert.ToBase64String(Enumerable.Repeat((byte)1, 12).ToArray())), 1);
	}
}
