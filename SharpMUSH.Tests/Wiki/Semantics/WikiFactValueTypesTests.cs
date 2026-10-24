using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Wiki.Semantics;
using SharpMUSH.Tests.Database.Lightning;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// Text, decimal and date properties: what a page may state for each, how a query matches, ranges over and sorts
/// them, and how facets count them.
/// </summary>
public class WikiFactValueTypesTests : LightningDatabaseFixture
{
	private static PropertyDefinition Property(string id, SemanticType type, Cardinality cardinality = Cardinality.Single)
		=> new(id, WikiFactProperties.SchemaVersion, type, cardinality, []);

	private static readonly PropertyDefinition Weight = Property("gear:weight", SemanticType.Decimal);
	private static readonly PropertyDefinition Founded = Property("lore:founded", SemanticType.Date);
	private static readonly PropertyDefinition Motto = Property("lore:motto", SemanticType.Text, Cardinality.Multiple);

	private async Task<WikiStoreService> WikiAsync(params PropertyDefinition[] properties)
	{
		var wiki = new WikiStoreService(Db, new WikiMarkdigPipeline());
		foreach (var property in properties)
			_ = (await wiki.SetFactPropertyAsync(property)).Expect<PropertyDefinition>();
		return wiki;
	}

	private static async Task<WikiPage> PageAsync(WikiStoreService wiki, string title, params WikiPageFact[] facts)
	{
		var page = (await wiki.CreateAsync(title, "Body.", "#1")).Expect<WikiPage>();
		_ = (await wiki.SetMetadataAsync(page.Id, ["Things"], true)).Expect<WikiPage>();
		return (await wiki.SetFactsAsync(page.Id, facts)).Expect<WikiPage>();
	}

	private async Task BuildAsync()
	{
		while (!(await Db.BuildFactIndexBatchAsync(256)).Ready) { }
	}

	/// <summary>The titles of the pages <paramref name="conditions"/> find, typed as <c>wikiquery()</c> takes them.</summary>
	private static async Task<Result<List<string>>> FindAsync(WikiStoreService wiki, params string[] conditions)
		=> await WikiFactQueryText.ParseAsync(wiki, await wiki.GetFactSchemaAsync(), conditions, 50, WikiVisibility.All) switch
		{
			WikiFactQuery query => await wiki.QueryFactsAsync(query, WikiVisibility.All) switch
			{
				WikiFactQueryPage page => page.Pages.Select(p => p.Title).ToList(),
				Error<string> error => error,
				WikiFactIndexBuilding => new Error<string>("The index is building."),
			},
			Error<string> error => error,
		};

	[Test]
	public async Task DecimalsCompareAsNumbersWhicheverWayTheyAreWritten()
	{
		var wiki = await WikiAsync(Weight);
		await PageAsync(wiki, "Feather", new WikiPageFact(Weight.Id, "0.1"));
		await PageAsync(wiki, "Rope", new WikiPageFact(Weight.Id, "1.50"));
		await PageAsync(wiki, "Debt", new WikiPageFact(Weight.Id, "-2.25"));
		await PageAsync(wiki, "Anvil", new WikiPageFact(Weight.Id, "10"));
		await BuildAsync();

		await Assert.That((await FindAsync(wiki, "gear:weight=1.5")).Expect<List<string>>()).IsEquivalentTo(["Rope"]);
		await Assert.That((await FindAsync(wiki, "gear:weight=-3..1.5", "sort=gear:weight")).Expect<List<string>>())
			.IsEquivalentTo(["Debt", "Feather", "Rope"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That((await FindAsync(wiki, "gear:weight>1.5", "sort=gear:weight", "order=desc")).Expect<List<string>>())
			.IsEquivalentTo(["Anvil"]);
		await Assert.That((await FindAsync(wiki, "gear:weight<0")).Expect<List<string>>()).IsEquivalentTo(["Debt"]);
		await Assert.That((await FindAsync(wiki, "gear:weight=*", "sort=gear:weight", "order=desc")).Expect<List<string>>())
			.IsEquivalentTo(["Anvil", "Rope", "Feather", "Debt"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task DatesAreDaysInCalendarOrder()
	{
		var wiki = await WikiAsync(Founded);
		await PageAsync(wiki, "Old town", new WikiPageFact(Founded.Id, "1999-12-31"));
		await PageAsync(wiki, "New town", new WikiPageFact(Founded.Id, "2026-03-01"));
		await PageAsync(wiki, "Mid town", new WikiPageFact(Founded.Id, "2026-01-15"));
		await BuildAsync();

		await Assert.That((await FindAsync(wiki, "lore:founded=2026-01-01..", "sort=lore:founded")).Expect<List<string>>())
			.IsEquivalentTo(["Mid town", "New town"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That((await FindAsync(wiki, "lore:founded>2026-01-15")).Expect<List<string>>()).IsEquivalentTo(["New town"]);
		await Assert.That((await FindAsync(wiki, "lore:founded<=2026-01-15", "sort=lore:founded", "order=desc")).Expect<List<string>>())
			.IsEquivalentTo(["Mid town", "Old town"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task TextIsKeptAsWrittenAndMatchedWhateverItsCase()
	{
		var wiki = await WikiAsync(Motto);
		var tower = await PageAsync(wiki, "Tower", new WikiPageFact(Motto.Id, "Knowledge Above All"));
		await PageAsync(wiki, "Keep", new WikiPageFact(Motto.Id, "Hold fast"));
		await BuildAsync();

		await Assert.That(tower.Facts.Single().Value).IsEqualTo("Knowledge Above All");
		await Assert.That((await FindAsync(wiki, "lore:motto=knowledge above all")).Expect<List<string>>()).IsEquivalentTo(["Tower"]);
		await Assert.That((await FindAsync(wiki, "lore:motto!=HOLD FAST", "lore:motto=*")).Expect<List<string>>()).IsEquivalentTo(["Tower"]);
		await Assert.That((await FindAsync(wiki, "lore:motto>a")).Expect<Error<string>>().Value)
			.IsEqualTo("lore:motto is not a number or a date, so it has no range.");
	}

	[Test]
	[Arguments("gear:weight", "1e3", "gear:weight is a number like 1.5, not '1e3'.")]
	[Arguments("gear:weight", "1,000", "gear:weight is a number like 1.5, not '1,000'.")]
	[Arguments("lore:founded", "2026-1-5", "lore:founded is a date like 2026-10-23, not '2026-1-5'.")]
	[Arguments("lore:founded", "2026-02-30", "lore:founded is a date like 2026-10-23, not '2026-02-30'.")]
	public async Task AValueItsTypeCannotHoldIsRefused(string property, string value, string error)
	{
		var wiki = await WikiAsync(Weight, Founded);
		var page = (await wiki.CreateAsync("Refused", "Body.", "#1")).Expect<WikiPage>();

		await Assert.That((await wiki.SetFactsAsync(page.Id, [new WikiPageFact(property, value)])).Expect<Error<string>>().Value).IsEqualTo(error);
		await BuildAsync();
		await Assert.That((await FindAsync(wiki, $"{property}={value}")).Expect<Error<string>>().Value).IsEqualTo(error);
	}

	[Test]
	public async Task AKeywordPropertyMadeTextIsMatchedWhateverItsCase()
	{
		var wiki = await WikiAsync(Property("lore:motto", SemanticType.Keyword));
		await PageAsync(wiki, "Tower", new WikiPageFact(Motto.Id, "Knowledge"));
		await BuildAsync();
		await Assert.That((await FindAsync(wiki, "lore:motto=knowledge")).Expect<List<string>>()).IsEmpty();

		_ = (await wiki.SetFactPropertyAsync(Motto)).Expect<PropertyDefinition>();
		await Assert.That((await FindAsync(wiki, "lore:motto=knowledge")).Expect<List<string>>()).IsEquivalentTo(["Tower"]);
	}

	[Test]
	public async Task FacetsCountDatesAndDecimalsInRangesOfAsManyValues()
	{
		var wiki = await WikiAsync(Weight, Founded, Motto);
		for (var i = 0; i < 12; i++)
			await PageAsync(wiki, $"Thing {i}", new WikiPageFact(Weight.Id, (i * i / 4.0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
				new WikiPageFact(Founded.Id, new DateOnly(2026, 1, 1).AddDays(i * 10).ToString("yyyy-MM-dd")),
				new WikiPageFact(Motto.Id, i % 2 == 0 ? "Even" : "ODD"), new WikiPageFact(Motto.Id, i % 2 == 0 ? "even" : "odd"));

		var facets = (await WikiFactFacets.CountAsync(wiki, "Things", [], WikiVisibility.All)).Expect<WikiFacets>();
		string Shown(string property) => string.Join(", ", facets.Facets.Single(f => f.Property == property).Values.Select(v => $"{v.Value}({v.Count})"));

		// Twelve days ten apart: six ranges of two, each from one stated day to another.
		await Assert.That(Shown(Founded.Id)).IsEqualTo("2026-01-01..2026-01-11(2), 2026-01-21..2026-01-31(2), 2026-02-10..2026-02-20(2), "
			+ "2026-03-02..2026-03-12(2), 2026-03-22..2026-04-01(2), 2026-04-11..2026-04-21(2)");
		// Squares crowd the low end, so the decimals are cut two values to a range rather than by width.
		await Assert.That(Shown(Weight.Id)).IsEqualTo("0..0.25(2), 1..2.25(2), 4..6.25(2), 9..12.25(2), 16..20.25(2), 25..30.25(2)");
		// A text spelled two ways is one value, shown as the first page spelled it.
		await Assert.That(Shown(Motto.Id)).IsEqualTo("Even(6), ODD(6)");

		var chosen = (await WikiFactFacets.CountAsync(wiki, "Things", ["lore:founded=2026-01-21..2026-01-31", "gear:weight=2.25"],
			WikiVisibility.All)).Expect<WikiFacets>();
		await Assert.That(chosen.Pages).IsEqualTo(1);
	}

	[Test]
	public async Task DecimalKeysOrderAsTheNumbersDo()
	{
		double[] numbers = [double.MinValue, -1e300, -2.5, -1, -double.Epsilon, 0, double.Epsilon, 0.1, 1, 1.5, 1e300, double.MaxValue];
		var keys = numbers.Select(WikiFactQueries.DecimalKey).ToList();

		await Assert.That(keys).IsEquivalentTo(keys.Order().ToList(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(keys.Select(WikiFactQueries.DecimalOf)).IsEquivalentTo(numbers, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(WikiFactQueries.DecimalKey(-0.0)).IsEqualTo(WikiFactQueries.DecimalKey(0.0));
	}

	[Test]
	[Arguments(0.00001, "0.00001")]
	[Arguments(-1.25e-7, "-0.000000125")]
	[Arguments(1e15, "1000000000000000")]
	[Arguments(1.5, "1.5")]
	public async Task ADecimalIsWrittenWithoutAnExponent(double number, string written)
		=> await Assert.That(WikiFactQueries.WriteDecimal(number)).IsEqualTo(written);

	[Test]
	public async Task EveryDecimalIsWrittenAsAValueThatReadsBackToIt()
	{
		double[] numbers = [double.MinValue, -1e300, -1e-5, -double.Epsilon, 0, double.Epsilon, 1.234e-20, 0.1, 1e21, double.MaxValue];
		await Assert.That(numbers.Select(n => WikiFactQueries.Decimal(WikiFactQueries.WriteDecimal(n)))).IsEquivalentTo(
			numbers.Select(n => (double?)n), TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}
}
