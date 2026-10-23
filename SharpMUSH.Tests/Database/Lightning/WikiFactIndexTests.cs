using System.Buffers.Binary;
using System.Text;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// The fact index: built in batches over the pages a world already has, kept in step by every page write from the
/// moment it starts, resumed after a reopen, and started over when it was written in another key format. Every test
/// compares the stored entries with the facts the pages state.
/// </summary>
public class WikiFactIndexTests : LightningDatabaseFixture
{
	private IWikiStore Wiki => Db;

	[Before(Test)]
	public Task DefineFactProperties() => WikiFactTestProperties.DefineAsync(Wiki);

	private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private async Task<WikiPage> AddAsync(string slug, params WikiPageFact[] facts)
	{
		var page = (await Wiki.CreatePageAsync(new WikiPage("", slug, slug, "main", "md", "<p>md</p>", "md", "#1", "#1", T0, T0, 1)))
			.Expect<WikiPage>();
		return facts.Length == 0 ? page : (await Wiki.SetPageFactsAsync(page.Id, facts)).Expect<WikiPage>();
	}

	private static WikiPageFact Level(int level) => new("pf2e:level", level.ToString(System.Globalization.CultureInfo.InvariantCulture));
	private static WikiPageFact Tradition(string tradition) => new("pf2e:tradition", tradition);

	/// <summary>What the index should hold: every fact of every page, as (property, value, page id).</summary>
	private async Task<List<(string, string, string)>> StatedAsync()
		=> [.. (await Wiki.GetPagesAsync(null, 0, int.MaxValue, WikiVisibility.All))
			.SelectMany(page => page.Facts.Select(f => (f.Property, f.Value, page.Id)))];

	/// <summary>What the index holds, decoded from its keys in key order.</summary>
	private List<(string, string, string)> Entries() => Db.Store.Read(tx => tx.Range(Tables.WikiFact, []).Select(entry =>
	{
		var key = entry.Key.AsSpan();
		var end = key.IndexOf((byte)0);
		var property = Encoding.UTF8.GetString(key[..end]);
		var rest = key[(end + 1)..];
		string value;
		if (rest[0] == 1)
		{
			value = unchecked((long)(BinaryPrimitives.ReadUInt64BigEndian(rest[1..9]) ^ 0x8000_0000_0000_0000UL))
				.ToString(System.Globalization.CultureInfo.InvariantCulture);
			rest = rest[9..];
		}
		else
		{
			var stop = rest[1..].IndexOf((byte)0);
			value = Encoding.UTF8.GetString(rest.Slice(1, stop));
			rest = rest[(stop + 2)..];
		}
		return (property, value, $"wiki_page/{Keys.ReadDbref(rest)}");
	}).ToList());

	private async Task<int> BuildAsync()
	{
		var batches = 0;
		while (!(await Wiki.BuildFactIndexBatchAsync(256)).Ready) batches++;
		return batches + 1;
	}

	[Test]
	public async Task AWorldBuildsTheFactsItAlreadyHadInBatches()
	{
		for (var i = 0; i < 1000; i++)
			await AddAsync($"spell-{i}", i % 3 == 0 ? [Level(i % 10), Tradition("arcane"), Tradition("primal")] : i % 3 == 1 ? [Level(1)] : []);
		var pages = (await Wiki.GetFactIndexStatusAsync()).Pages;

		await Assert.That(await Wiki.GetFactIndexStatusAsync()).IsEqualTo(new WikiFactIndexStatus(WikiFactIndexPhase.Building, 0, pages));
		var first = await Wiki.BuildFactIndexBatchAsync(256);
		await Assert.That(first).IsEqualTo(new WikiFactIndexStatus(WikiFactIndexPhase.Building, 256, pages));
		await Assert.That(1 + await BuildAsync()).IsEqualTo((int)Math.Ceiling(pages / 256.0));
		await Assert.That(await Wiki.GetFactIndexStatusAsync()).IsEqualTo(new WikiFactIndexStatus(WikiFactIndexPhase.Ready, pages, pages));
		await Assert.That(Entries()).IsEquivalentTo(await StatedAsync());
		await Assert.That(Entries().Count).IsEqualTo(334 * 3 + 333);
	}

	[Test]
	public async Task WritesDuringABuildAreInTheIndexWhenItIsReady()
	{
		var pages = new List<WikiPage>();
		for (var i = 0; i < 600; i++) pages.Add(await AddAsync($"feat-{i}", Level(i % 20)));
		await Wiki.BuildFactIndexBatchAsync(256);

		// One page the build has passed and one it has not, each edited, and one of each deleted.
		await Wiki.SetPageFactsAsync(pages[10].Id, [Level(19), Tradition("occult")]);
		await Wiki.SetPageFactsAsync(pages[500].Id, [Tradition("divine")]);
		await Wiki.DeletePageAsync(pages[11].Id);
		await Wiki.DeletePageAsync(pages[501].Id);
		await Wiki.SetPageTitleAsync(pages[12].Id, "Renamed feat");
		await AddAsync("feat-late", Level(7));
		await BuildAsync();

		await Assert.That(Entries()).IsEquivalentTo(await StatedAsync());
	}

	[Test]
	public async Task AReadyIndexFollowsEveryWrite()
	{
		var page = await AddAsync("fireball", Level(3), Tradition("arcane"), Tradition("primal"));
		await BuildAsync();

		await Wiki.SetPageFactsAsync(page.Id, [Level(5), Tradition("arcane")]);
		await Wiki.SetPageTitleAsync(page.Id, "Greater Fireball");
		await Wiki.SetPageMetadataAsync(page.Id, ["spells"], false);
		await Wiki.UpdatePageBodyAsync(page.Id, new WikiBody("hotter", "<p>hotter</p>", "hotter"), "#1", null, T0);
		await Assert.That(Entries()).IsEquivalentTo([("pf2e:level", "5", page.Id), ("pf2e:tradition", "arcane", page.Id)]);

		await Wiki.DeletePageAsync(page.Id);
		await Assert.That(Entries()).IsEmpty();
	}

	[Test]
	public async Task IntegersSortAsNumbers()
	{
		var high = await AddAsync("high", Level(10));
		var low = await AddAsync("low", Level(-2));
		var middle = await AddAsync("middle", Level(3));
		await BuildAsync();

		await Assert.That(Entries().Select(e => e.Item3)).IsEquivalentTo([low.Id, middle.Id, high.Id], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ABuildResumesAfterAReopen()
	{
		for (var i = 0; i < 700; i++) await AddAsync($"item-{i}", Level(i % 5));
		await Wiki.BuildFactIndexBatchAsync(256);
		await ReopenAsync();

		var resumed = await Wiki.GetFactIndexStatusAsync();
		await Assert.That((resumed.Phase, resumed.Indexed)).IsEqualTo((WikiFactIndexPhase.Building, 256L));
		await Assert.That(await BuildAsync()).IsEqualTo((int)Math.Ceiling((resumed.Pages - 256) / 256.0));
		await Assert.That(Entries()).IsEquivalentTo(await StatedAsync());
	}

	[Test]
	public async Task AnIndexWrittenInAnotherFormatStartsOver()
	{
		var page = await AddAsync("shield", Level(1));
		await BuildAsync();
		// The state an older format left, and an entry written in it.
		await Db.Store.WriteAsync(tx =>
		{
			tx.TryGet(Tables.Meta, Keys.Str("wiki.fact.index"), out var state);
			state[^1] ^= 0xFF;
			tx.Put(Tables.Meta, Keys.Str("wiki.fact.index"), state);
			tx.Put(Tables.WikiFact, Keys.Concat(Keys.Str("old:property"), Keys.Sep, [2], Keys.Str("x"), Keys.Sep, Keys.Dbref(99)), []);
		});
		await Assert.That((await Wiki.GetFactIndexStatusAsync()).Phase).IsEqualTo(WikiFactIndexPhase.Clearing);
		// Writes leave an index in another format alone; the rebuild picks them up.
		await Wiki.SetPageFactsAsync(page.Id, [Level(2)]);

		await BuildAsync();
		await Assert.That(Entries()).IsEquivalentTo([("pf2e:level", "2", page.Id)]);
	}

	[Test]
	public async Task AStateThisBuildCannotReadStartsOver()
	{
		var page = await AddAsync("light", Level(0));
		await Db.Store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("wiki.fact.index"), [9, 9, 9]));

		await Assert.That((await Wiki.GetFactIndexStatusAsync()).Phase).IsEqualTo(WikiFactIndexPhase.Clearing);
		await BuildAsync();
		await Assert.That(Entries()).IsEquivalentTo([("pf2e:level", "0", page.Id)]);
	}

	[Test]
	public async Task ARebuildClearsAndBuildsTheSameEntries()
	{
		for (var i = 0; i < 300; i++) await AddAsync($"rune-{i}", Level(i % 7), Tradition(i % 2 == 0 ? "arcane" : "divine"));
		await BuildAsync();
		var built = Entries();

		await Assert.That((await Wiki.RebuildFactIndexAsync()).Phase).IsEqualTo(WikiFactIndexPhase.Clearing);
		// 600 entries clear in three batches before the build starts.
		await Assert.That((await Wiki.BuildFactIndexBatchAsync(256)).Phase).IsEqualTo(WikiFactIndexPhase.Clearing);
		await BuildAsync();
		await Assert.That(Entries()).IsEquivalentTo(built);
	}
}
