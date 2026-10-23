using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Commands.WikiCommand;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary><c>@wiki/query</c> and <c>wikiquery()</c>: the pages whose facts meet every condition, in-game.</summary>
// Kept apart from the test that rebuilds the shared world's fact index, which would answer these with "building".
[NotInParallel("wiki-fact-index")]
public class WikiQueryCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IWikiService WikiService => WebAppFactoryArg.Services.GetRequiredService<IWikiService>();

	[Before(Test)]
	public Task DefineFactProperties() => WikiFactTestProperties.DefineAsync(WikiService);

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		await WebAppFactoryArg.Services.GetRequiredService<IRoleRegistryService>().AssignRoleToObjectAsync(player.DbRef.Number, BuiltInRoles.ApprovedSlug);
		await Mediator.Send(new InvalidateGrantsCommand(player.DbRef.Number));
		return player;
	}

	/// <summary>A page in <paramref name="category"/> stating <paramref name="facts"/>.</summary>
	private async Task<WikiPage> PageAsync(string prefix, string category, bool published, params WikiPageFact[] facts)
	{
		var page = (await WikiService.CreateAsync(TestIsolationHelpers.GenerateUniqueName(prefix), "Body.", "#1")).Expect<WikiPage>();
		await WikiService.SetMetadataAsync(page.Id, [category], published);
		return (await WikiService.SetFactsAsync(page.Id, facts)).Expect<WikiPage>();
	}

	private Task RunAsync(TestIsolationHelpers.TestPlayer player, string command)
		=> Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command)).AsTask();

	private int Told(TestIsolationHelpers.TestPlayer player, string contains)
		=> WebAppFactoryArg.Notifications.DeliveriesFor(player.DbRef).Count(d => d.Message.Contains(contains, StringComparison.Ordinal));

	[Test]
	public async Task AQueryFindsThePagesMeetingEveryCondition()
	{
		await WebAppFactoryArg.Services.GetRequiredService<IWikiFactIndexBuilder>().Idle;
		var player = await PlayerAsync("WikiQuerier");
		var category = TestIsolationHelpers.GenerateUniqueName("QuerySpells");
		var wizard = await PageAsync("QueryWizard", category, true);
		var cantrip = await PageAsync("QueryCantrip", category, true, new("pf2e:level", "0"), new("pf2e:tradition", "arcane"));
		var shield = await PageAsync("QueryShield", category, true, new("pf2e:level", "1"), new("pf2e:tradition", "arcane"), new("pf2e:class", wizard.Id));
		await PageAsync("QueryHeal", category, true, new("pf2e:level", "1"), new("pf2e:tradition", "divine"));
		var fireball = await PageAsync("QueryFireball", category, true, new("pf2e:level", "3"), new("pf2e:tradition", "arcane"));
		await PageAsync("QueryDraft", category, false, new("pf2e:level", "1"), new("pf2e:tradition", "arcane"));
		string Reference(WikiPage page) => $"main:{page.Slug}";

		var marker = TestIsolationHelpers.GenerateUniqueName("query");
		await RunAsync(player, $"think {marker} [wikiquery(category={category}, pf2e:tradition=arcane, pf2e:level=..2)]/" +
			$"[wikiquery(category={category}, pf2e:class={wizard.Title})]/[wikiquery(category={category}, pf2e:level=7..)]/");
		await Assert.That(Told(player, $"{marker} {Reference(cantrip)} {Reference(shield)}/{Reference(shield)}//")).IsEqualTo(1);

		await RunAsync(player, $"@wiki/query category={category}, pf2e:level=1..3, pf2e:tradition=arcane");
		await Assert.That(Told(player, "2 page(s) where category=")).IsEqualTo(1);
		// A listing row is the page's reference, then its title.
		await Assert.That(Told(player, $"{Reference(shield)} ")).IsEqualTo(1);
		await Assert.That(Told(player, $"{Reference(fireball)} ")).IsEqualTo(1);
	}

	[Test]
	public async Task OperatorsSortAndShownValuesAnswerAsTyped()
	{
		await WebAppFactoryArg.Services.GetRequiredService<IWikiFactIndexBuilder>().Idle;
		var player = await PlayerAsync("WikiQueryOperators");
		var category = TestIsolationHelpers.GenerateUniqueName("QueryOps");
		var wizard = await PageAsync("OpsWizard", category, true);
		var low = await PageAsync("OpsLow", category, true, new("pf2e:level", "1"), new("pf2e:tradition", "arcane"), new("pf2e:class", wizard.Id));
		var middle = await PageAsync("OpsMiddle", category, true, new("pf2e:level", "3"), new("pf2e:tradition", "divine"));
		var high = await PageAsync("OpsHigh", category, true, new("pf2e:level", "5"), new("pf2e:tradition", "occult"), new("pf2e:tradition", "arcane"));
		string Reference(WikiPage page) => $"main:{page.Slug}";
		async Task ExpectAsync(string conditions, params WikiPage[] pages)
		{
			var marker = TestIsolationHelpers.GenerateUniqueName("ops");
			await RunAsync(player, $"think {marker} [wikiquery(category={category}, {conditions})]");
			await Assert.That(Told(player, $"{marker} {string.Join(" ", pages.Select(Reference))}")).IsEqualTo(1);
		}

		await ExpectAsync("pf2e:level>1, sort=pf2e:level", middle, high);
		await ExpectAsync("pf2e:level<=3, sort=pf2e:level, order=desc", middle, low);
		await ExpectAsync("pf2e:tradition=divine|occult, sort=pf2e:level", middle, high);
		await ExpectAsync("pf2e:class=*", low);
		await ExpectAsync("pf2e:level=*, pf2e:class!=*, sort=pf2e:level", middle, high);
		await ExpectAsync("pf2e:tradition!=arcane, pf2e:level>=0", middle);

		// A comparison after a value is a condition of its own, not more of that value.
		await RunAsync(player, $"@wiki/query category={category}, pf2e:tradition=arcane|divine, pf2e:level>0, sort=pf2e:level, show=pf2e:tradition|pf2e:class");
		var listing = WebAppFactoryArg.Notifications.RawFor(player.DbRef)
			.Select(message => message switch { MString markup => markup.ToPlainText(), string text => text })
			.Single(m => m.Contains($"3 page(s) where category={category}", StringComparison.Ordinal));
		// The shown properties take the place of the revision and date, a page value by its title.
		await Assert.That(listing).Contains("pf2e:tradition").And.Contains("pf2e:class").And.DoesNotContain("Edited");
		await Assert.That(listing).Contains("OpsWizard_").And.Contains("arcane ·").And.Contains("occult");
		await Assert.That(listing.IndexOf(Reference(low), StringComparison.Ordinal))
			.IsLessThan(listing.IndexOf(Reference(middle), StringComparison.Ordinal));

		var refused = TestIsolationHelpers.GenerateUniqueName("shown");
		await RunAsync(player, $"think {refused} [wikiquery(category={category}, pf2e:level=*, show=pf2e:tradition)]");
		await Assert.That(Told(player, $"{refused} #-1 INVALID WIKI QUERY")).IsEqualTo(1);
	}

	[Test]
	public async Task DecimalsDatesAndTextAnswerAsTyped()
	{
		await WebAppFactoryArg.Services.GetRequiredService<IWikiFactIndexBuilder>().Idle;
		var player = await PlayerAsync("WikiQueryTypes");
		var category = TestIsolationHelpers.GenerateUniqueName("QueryTypes");
		var cheap = await PageAsync("TypesCheap", category, true, new("pf2e:price", "0.5"), new("pf2e:published", "2019-08-01"), new("pf2e:motto", "Run Away"));
		var dear = await PageAsync("TypesDear", category, true, new("pf2e:price", "12.75"), new("pf2e:published", "2023-08-02"));
		async Task ExpectAsync(string conditions, params WikiPage[] pages)
		{
			var marker = TestIsolationHelpers.GenerateUniqueName("types");
			await RunAsync(player, $"think {marker} [wikiquery(category={category}, {conditions})]");
			await Assert.That(Told(player, $"{marker} {string.Join(" ", pages.Select(p => $"main:{p.Slug}"))}")).IsEqualTo(1);
		}

		await ExpectAsync("pf2e:price<1.25", cheap);
		await ExpectAsync("pf2e:price=0.50", cheap);
		await ExpectAsync("pf2e:published=2020-01-01..", dear);
		await ExpectAsync("pf2e:price=*, sort=pf2e:published, order=desc", dear, cheap);
		await ExpectAsync("pf2e:motto=run away", cheap);

		await RunAsync(player, $"@wiki/query category={category}, pf2e:price=*, sort=pf2e:price, show=pf2e:price|pf2e:published");
		var listing = WebAppFactoryArg.Notifications.RawFor(player.DbRef)
			.Select(message => message switch { MString markup => markup.ToPlainText(), string text => text })
			.Single(m => m.Contains($"2 page(s) where category={category}", StringComparison.Ordinal));
		// Values are shown as the page wrote them.
		await Assert.That(listing).Contains("0.5").And.Contains("12.75").And.Contains("2019-08-01").And.Contains("2023-08-02");
	}

	[Test]
	public async Task ATitleWithACommaIsOneConditionAndTheListingIsInTheReadersLocale()
	{
		await WebAppFactoryArg.Services.GetRequiredService<IWikiFactIndexBuilder>().Idle;
		var player = await PlayerAsync("WikiQueryLocale");
		var tag = Guid.NewGuid().ToString("N")[..6];
		var elder = await PageAsync($"Elder, {tag}", "QueryLocale", true);
		var spell = await PageAsync("QuerySpell", "QueryLocale", true, new WikiPageFact("pf2e:class", elder.Id));
		await Assert.That(await WikiService.UpsertTranslationAsync(spell.Id, "fr", $"Sort {tag}", "Corps.", "#1", null, true,
			expectedRevisionNumber: null) is WikiTranslation).IsTrue();

		await RunAsync(player, "@locale fr");
		await RunAsync(player, $"@wiki/query pf2e:class={elder.Title}");
		await Assert.That(Told(player, $"Sort {tag}")).IsEqualTo(1);
		await RunAsync(player, $"@wiki/query/source pf2e:class={elder.Title}");
		// Both listings name the page; only the first gave its French title.
		await Assert.That(Told(player, $"main:{spell.Slug} ")).IsEqualTo(2);
		await Assert.That(Told(player, $"Sort {tag}")).IsEqualTo(1);
	}

	[Test]
	[Arguments("pf2e:unknown=1", "WIKI: No property pf2e:unknown is defined.")]
	[Arguments("pf2e:level=1..x", "WIKI: pf2e:level is a whole number; '1..x' is not a range like '1..3'.")]
	[Arguments("pf2e:class=No Such Query Page", "WIKI: pf2e:class names a page that does not exist: 'No Such Query Page'.")]
	[Arguments("level", "WIKI: 'level' is not a condition like 'pf2e:level=1..3'.")]
	[Arguments("namespace=nowhere", "WIKI: No namespace nowhere exists.")]
	[Arguments("pf2e:tradition>2", "WIKI: pf2e:tradition is not a number or a date, so it has no range.")]
	[Arguments("pf2e:level>=x", "WIKI: pf2e:level is a whole number, not 'x'.")]
	[Arguments("pf2e:level!=1", "WIKI: A query needs a condition a page meets by stating a value; != alone finds no pages.")]
	[Arguments("pf2e:level=1, order=desc", "WIKI: order needs a sort, as in 'sort=pf2e:level'.")]
	[Arguments("pf2e:level=1, order=up", "WIKI: order is asc or desc.")]
	[Arguments("pf2e:level=1, sort=pf2e:class", "WIKI: pf2e:class names pages, so it does not sort them; sort by a number or a keyword.")]
	[Arguments("pf2e:level=1, show=pf2e:unknown", "WIKI: No property pf2e:unknown is defined.")]
	[Arguments("pf2e:tradition=arcane|", "WIKI: pf2e:tradition has an empty value in 'arcane|'.")]
	public async Task AQueryTheWikiCannotAnswerIsTold(string condition, string told)
	{
		await WebAppFactoryArg.Services.GetRequiredService<IWikiFactIndexBuilder>().Idle;
		var player = await PlayerAsync("WikiQueryRefused");

		await RunAsync(player, $"@wiki/query {condition}");
		await Assert.That(Told(player, told)).IsEqualTo(1);
		var marker = TestIsolationHelpers.GenerateUniqueName("refused");
		await RunAsync(player, $"think {marker} [wikiquery({condition})]");
		await Assert.That(Told(player, $"{marker} #-1 INVALID WIKI QUERY")).IsEqualTo(1);
	}

	[Test]
	public async Task AQueryListsUpToItsLimitAndSaysWhetherMoreMatch()
	{
		await WebAppFactoryArg.Services.GetRequiredService<IWikiFactIndexBuilder>().Idle;
		var category = TestIsolationHelpers.GenerateUniqueName("QueryLimit");
		var made = new List<string>();
		for (var i = 0; i < 3; i++) made.Add((await PageAsync("QueryLimited", category, true, new WikiPageFact("pf2e:level", "2"))).Id);
		string[] conditions = [$"category={category}", "pf2e:level=2"];

		var cut = (await QueryWiki.RunAsync(WikiService, conditions, WikiVisibility.All, 2)).Expect<WikiFactQueryListing>();
		await Assert.That(cut.More).IsTrue();
		await Assert.That(cut.Pages.Count).IsEqualTo(2);
		await Assert.That(made).Contains(cut.Pages[0].Id).And.Contains(cut.Pages[1].Id);
		var all = (await QueryWiki.RunAsync(WikiService, conditions, WikiVisibility.All, 3)).Expect<WikiFactQueryListing>();
		await Assert.That(all.More).IsFalse();
		await Assert.That(all.Pages.Select(p => p.Id)).IsEquivalentTo(made);
	}

	[Test]
	public async Task AFifthConditionIsRefusedBeforeThePageItNamesIsLookedFor()
	{
		var refused = await QueryWiki.RunAsync(WikiService,
			["pf2e:level=1", "pf2e:level=2", "pf2e:level=3", "pf2e:level=4", $"pf2e:class={Guid.NewGuid():N}"], WikiVisibility.All, 10);
		await Assert.That(refused.Expect<Error<string>>().Value).IsEqualTo("A query takes from 1 to 4 conditions.");
	}
}
