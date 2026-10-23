using System.Globalization;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Fact queries over the fact index: every answer, read through all its continuations, is compared with a scan of
/// every page that checks the same conditions.
/// </summary>
public class WikiFactQueryTests : LightningDatabaseFixture
{
	private IWikiStore Wiki => Db;

	[Before(Test)]
	public Task DefineFactProperties() => WikiFactTestProperties.DefineAsync(Wiki);

	private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
	private static readonly string[] Traditions = ["arcane", "divine", "occult", "primal"];

	private async Task<WikiPage> AddAsync(string slug, string author = "#1", IReadOnlyList<string>? categories = null,
		bool published = true, params WikiPageFact[] facts)
	{
		var page = (await Wiki.CreatePageAsync(new WikiPage("", slug, slug, "main", "md", "<p>md</p>", "md", author, author, T0, T0, 1)))
			.Expect<WikiPage>();
		if (categories is not null || !published) page = (await Wiki.SetPageMetadataAsync(page.Id, categories ?? [], published)).Expect<WikiPage>();
		return facts.Length == 0 ? page : (await Wiki.SetPageFactsAsync(page.Id, facts)).Expect<WikiPage>();
	}

	private static WikiPageFact Fact(string property, object value) => new(property, Convert.ToString(value, CultureInfo.InvariantCulture)!);

	private async Task BuildAsync()
	{
		while (!(await Wiki.BuildFactIndexBatchAsync(256)).Ready) { }
	}

	/// <summary>Every page of a query's answer, following each continuation, and how many calls it took.</summary>
	private async Task<(List<WikiPage> Pages, int Calls)> AllAsync(WikiFactQuery query, WikiVisibility visibility)
	{
		var pages = new List<WikiPage>();
		var calls = 0;
		for (string? after = null; ;)
		{
			calls++;
			var page = (await Wiki.QueryFactsAsync(query with { After = after }, visibility)).Expect<WikiFactQueryPage>();
			await Assert.That(page.Pages.Count).IsLessThanOrEqualTo(query.Take);
			pages.AddRange(page.Pages);
			if (page.Next is not { } next) return (pages, calls);
			after = next;
		}
	}

	/// <summary>What a query should answer, by reading every page.</summary>
	private async Task<List<string>> ScanAsync(WikiFactQuery query, WikiVisibility visibility)
		=> [.. (await Wiki.GetPagesAsync(null, 0, int.MaxValue, WikiVisibility.All))
			.Where(page => visibility.Admits(page)
				&& (query.Namespace is null || page.Namespace == query.Namespace)
				&& (query.Category is null || page.Categories.Contains(query.Category))
				&& query.Conditions.All(condition => condition switch
				{
					WikiFactEquals e => page.Facts.Any(f => f.Property == e.Property && f.Value == e.Value),
					WikiFactRange r => page.Facts.Any(f => f.Property == r.Property && long.Parse(f.Value, CultureInfo.InvariantCulture) is var n
						&& n >= (r.Min ?? long.MinValue) && n <= (r.Max ?? long.MaxValue))
				}))
			.Select(page => page.Id)];

	[Test]
	public async Task EveryAnswerIsWhatAScanOfThePagesFinds()
	{
		var random = new Random(2031);
		var classes = new List<WikiPage>();
		for (var i = 0; i < 5; i++) classes.Add(await AddAsync($"class-{i}"));
		for (var i = 0; i < 1500; i++)
		{
			var facts = new List<WikiPageFact>();
			if (random.Next(5) > 0) facts.Add(Fact("pf2e:level", random.Next(-3, 21)));
			if (random.Next(3) == 0) facts.Add(Fact("pf2e:rank", random.Next(1, 11)));
			facts.AddRange(Traditions.Where(_ => random.Next(3) == 0).Select(t => Fact("pf2e:tradition", t)));
			facts.AddRange(classes.Where(_ => random.Next(6) == 0).Select(c => Fact("pf2e:class", c.Id)));
			await AddAsync($"spell-{i}", author: random.Next(10) == 0 ? "#2" : "#1",
				categories: random.Next(4) == 0 ? ["cantrips"] : null, published: random.Next(8) > 0, [.. facts]);
		}
		await BuildAsync();

		var visibilities = new[] { WikiVisibility.All, WikiVisibility.PublishedOnly, new WikiVisibility(false, "#2") };
		for (var q = 0; q < 150; q++)
		{
			var conditions = Enumerable.Range(0, random.Next(1, 4)).Select(_ => (WikiFactCondition)(random.Next(4) switch
			{
				0 => LevelRange(random),
				1 => new WikiFactEquals("pf2e:tradition", Traditions[random.Next(4)]),
				2 => new WikiFactEquals("pf2e:class", classes[random.Next(5)].Id),
				_ => new WikiFactEquals("pf2e:rank", random.Next(1, 11).ToString(CultureInfo.InvariantCulture))
			})).ToList();
			var query = new WikiFactQuery(conditions, random.Next(1, 51)) { Category = random.Next(4) == 0 ? "cantrips" : null };
			var visibility = visibilities[random.Next(3)];

			var (pages, _) = await AllAsync(query, visibility);
			await Assert.That(pages.Select(p => p.Id)).IsEquivalentTo(await ScanAsync(query, visibility));
			await Assert.That(pages.Select(p => p.Id).Distinct().Count()).IsEqualTo(pages.Count);
		}
	}

	/// <summary>A level range with either bound open, but not both.</summary>
	private static WikiFactRange LevelRange(Random random)
	{
		long? min = random.Next(3) == 0 ? null : random.Next(-5, 15);
		long? max = random.Next(3) == 0 ? null : random.Next(0, 25);
		return new("pf2e:level", min is null && max is null ? 0 : min, max);
	}

	[Test]
	public async Task ARangeAnswersInOrderOfItsValues()
	{
		var high = await AddAsync("high", facts: Fact("pf2e:level", 10));
		var low = await AddAsync("low", facts: Fact("pf2e:level", -2));
		var middle = await AddAsync("middle", facts: Fact("pf2e:level", 3));
		var also = await AddAsync("also-middle", facts: Fact("pf2e:level", 3));
		await AddAsync("too-high", facts: Fact("pf2e:level", 11));
		await BuildAsync();

		var (pages, _) = await AllAsync(new WikiFactQuery([new WikiFactRange("pf2e:level", null, 10)], 2), WikiVisibility.All);
		await Assert.That(pages.Select(p => p.Id)).IsEquivalentTo([low.Id, middle.Id, also.Id, high.Id], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ABroadQueryReturnsWhatItFoundAndGoesOn()
	{
		for (var i = 0; i < 5000; i++)
			await AddAsync($"rune-{i}", categories: i == 4999 ? ["rare"] : null, facts: Fact("pf2e:tradition", "arcane"));
		await BuildAsync();
		var query = new WikiFactQuery([new WikiFactEquals("pf2e:tradition", "arcane")], 50) { Category = "rare" };

		var first = (await Wiki.QueryFactsAsync(query, WikiVisibility.All)).Expect<WikiFactQueryPage>();
		await Assert.That(first.Pages).IsEmpty();
		await Assert.That(first.Next).IsNotNull();
		var (pages, calls) = await AllAsync(query, WikiVisibility.All);
		await Assert.That(pages.Select(p => p.Slug)).IsEquivalentTo(["rune-4999"]);
		await Assert.That(calls).IsEqualTo((int)Math.Ceiling(5000 / (double)WikiFactQuery.MaxCandidates));
	}

	[Test]
	public async Task TheConditionWithFewestEntriesDrives()
	{
		for (var i = 0; i < 3000; i++) await AddAsync($"feat-{i}", facts: [Fact("pf2e:tradition", "divine"), Fact("pf2e:level", i == 2999 ? 20 : 1)]);
		await BuildAsync();

		// Read from the tradition, the answer would take two calls; the level has one entry.
		var (pages, calls) = await AllAsync(new WikiFactQuery([new WikiFactEquals("pf2e:tradition", "divine"), new WikiFactEquals("pf2e:level", "20")], 50),
			WikiVisibility.All);
		await Assert.That(pages.Select(p => p.Slug)).IsEquivalentTo(["feat-2999"]);
		await Assert.That(calls).IsEqualTo(1);
	}

	[Test]
	public async Task AnIndexNotYetBuiltSaysSo()
	{
		await AddAsync("unbuilt", facts: Fact("pf2e:level", 1));

		var result = await Wiki.QueryFactsAsync(new WikiFactQuery([new WikiFactEquals("pf2e:level", "1")], 10), WikiVisibility.All);
		await Assert.That(result.Expect<WikiFactIndexBuilding>().Status.Phase).IsEqualTo(WikiFactIndexPhase.Building);
	}

	[Test]
	public async Task APageIsNamedByItsBareKeyToo()
	{
		var wizard = await AddAsync("wizard");
		var spell = await AddAsync("shield", facts: Fact("pf2e:class", wizard.Id));
		await BuildAsync();

		var (pages, _) = await AllAsync(new WikiFactQuery([new WikiFactEquals("pf2e:class", wizard.Id["wiki_page/".Length..])], 10), WikiVisibility.All);
		await Assert.That(pages.Select(p => p.Id)).IsEquivalentTo([spell.Id]);
	}

	[Test]
	[Arguments("pf2e:unknown", "No property pf2e:unknown is defined.")]
	[Arguments("pf2e:tradition..", "pf2e:tradition is not a whole number, so it has no range.")]
	[Arguments("pf2e:level..", "A range over pf2e:level needs a bound.")]
	[Arguments("pf2e:level=three", "pf2e:level is a whole number, not 'three'.")]
	[Arguments("five", "A query takes from 1 to 4 conditions.")]
	[Arguments("take", "A query returns from 1 to 50 pages at a time.")]
	[Arguments("pf2e:tradition=long", "pf2e:tradition has no value longer than 256 bytes.")]
	[Arguments("after", "That query's continuation is not valid here.")]
	[Arguments("after:short", "That query's continuation is not valid here.")]
	[Arguments("after:long", "That query's continuation is not valid here.")]
	public async Task AQueryTheIndexCannotAnswerIsAnError(string shape, string error)
	{
		await BuildAsync();
		var level = new WikiFactEquals("pf2e:level", "1");
		var query = shape switch
		{
			"pf2e:unknown" => new WikiFactQuery([new WikiFactEquals("pf2e:unknown", "1")], 10),
			"pf2e:tradition.." => new WikiFactQuery([new WikiFactRange("pf2e:tradition", 1, 2)], 10),
			"pf2e:level.." => new WikiFactQuery([new WikiFactRange("pf2e:level", null, null)], 10),
			"pf2e:level=three" => new WikiFactQuery([new WikiFactEquals("pf2e:level", "three")], 10),
			"five" => new WikiFactQuery([level, level, level, level, level], 10),
			"take" => new WikiFactQuery([level], 51),
			"pf2e:tradition=long" => new WikiFactQuery([new WikiFactEquals("pf2e:tradition", new string('a', 257))], 10),
			// A range's property and tag with no value or page key after them, and a key longer than any entry.
			"after:short" => new WikiFactQuery([new WikiFactRange("pf2e:level", 1, 3)], 10,
				"facts:v1:" + Convert.ToBase64String([0, .. "pf2e:level"u8, 0, 1])),
			"after:long" => new WikiFactQuery([level], 10,
				"facts:v1:" + Convert.ToBase64String([0, .. "pf2e:level"u8, 0, 1, 0x80, 0, 0, 0, 0, 0, 0, 1, .. new byte[9000]])),
			_ => new WikiFactQuery([level], 10, "facts:v1:" + Convert.ToBase64String([0, 9, 9]))
		};

		var result = await Wiki.QueryFactsAsync(query, WikiVisibility.All);
		await Assert.That(result.Expect<Error<string>>().Value).IsEqualTo(error);
	}
}
