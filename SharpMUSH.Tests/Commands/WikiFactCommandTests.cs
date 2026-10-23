using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Tests.Commands;

/// <summary><c>@wiki/fact</c> and <c>wikifact()</c>: a page's facts set, shown and read in-game.</summary>
public class WikiFactCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IWikiService WikiService => WebAppFactoryArg.Services.GetRequiredService<IWikiService>();

	[Before(Test)]
	public Task DefineFactProperties() => WikiFactTestProperties.DefineAsync(WikiService);

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix, bool approved)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		if (!approved) return player;
		await WebAppFactoryArg.Services.GetRequiredService<IRoleRegistryService>().AssignRoleToObjectAsync(player.DbRef.Number, BuiltInRoles.ApprovedSlug);
		await Mediator.Send(new InvalidateGrantsCommand(player.DbRef.Number));
		return player;
	}

	private async Task<WikiPage> PageAsync(string prefix, bool published = true)
	{
		var title = TestIsolationHelpers.GenerateUniqueName(prefix);
		var page = (await WikiService.CreateAsync(title, "Body.", "#1")).Expect<WikiPage>();
		return published ? page : (await WikiService.SetMetadataAsync(page.Id, [], false)).Expect<WikiPage>();
	}

	private Task RunAsync(TestIsolationHelpers.TestPlayer player, string command)
		=> Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command)).AsTask();

	private int Told(TestIsolationHelpers.TestPlayer player, string contains)
		=> WebAppFactoryArg.Notifications.DeliveriesFor(player.DbRef).Count(d => d.Message.Contains(contains, StringComparison.Ordinal));

	private async Task<IReadOnlyList<WikiPageFact>> FactsAsync(WikiPage page) => (await WikiService.GetByIdAsync(page.Id)).Expect<WikiPage>().Facts;

	[Test]
	public async Task AFactIsSetShownReadAndCleared()
	{
		var player = await PlayerAsync("WikiFacter", approved: true);
		var spell = await PageAsync("FactSpell");
		var wizard = await PageAsync("FactWizard");

		await RunAsync(player, $"@wiki/fact {spell.Slug}/pf2e:level=3");
		await Assert.That(Told(player, $"WIKI: '{spell.Title}' pf2e:level: 3.")).IsEqualTo(1);
		// A page is named by its title and stored as its id.
		await RunAsync(player, $"@wiki/fact {spell.Slug}/pf2e:class={wizard.Title}");
		await Assert.That(Told(player, $"WIKI: '{spell.Title}' pf2e:class: {wizard.Title}.")).IsEqualTo(1);
		await RunAsync(player, $"@wiki/fact {spell.Slug}/pf2e:tradition=arcane|primal");
		await Assert.That(Told(player, $"WIKI: '{spell.Title}' pf2e:tradition: arcane | primal.")).IsEqualTo(1);
		await Assert.That(await FactsAsync(spell)).IsEquivalentTo([new WikiPageFact("pf2e:class", wizard.Id), new WikiPageFact("pf2e:level", "3"),
			new WikiPageFact("pf2e:tradition", "arcane"), new WikiPageFact("pf2e:tradition", "primal")]);

		await RunAsync(player, $"@wiki/fact {spell.Slug}");
		await Assert.That(Told(player, $"Facts: {spell.Title}")).IsEqualTo(1);
		// Once when it was set, once in the panel.
		await Assert.That(Told(player, "arcane | primal")).IsEqualTo(2);

		var marker = TestIsolationHelpers.GenerateUniqueName("facts");
		await RunAsync(player, $"think {marker} [wikifact({spell.Slug})] [wikifact({spell.Slug}, pf2e:class)] [wikifact({spell.Slug}, pf2e:tradition, |)]");
		await Assert.That(Told(player, $"{marker} pf2e:class pf2e:level pf2e:tradition main:{wizard.Slug} arcane|primal")).IsEqualTo(1);

		await RunAsync(player, $"@wiki/fact {spell.Slug}/pf2e:tradition=");
		await Assert.That(Told(player, $"WIKI: '{spell.Title}' states no pf2e:tradition.")).IsEqualTo(1);
		await Assert.That((await FactsAsync(spell)).Select(f => f.Property)).IsEquivalentTo(["pf2e:class", "pf2e:level"]);
	}

	[Test]
	[Arguments("pf2e:level=three", "WIKI: pf2e:level is a whole number, not 'three'.")]
	[Arguments("pf2e:unknown=1", "WIKI: No property pf2e:unknown is defined.")]
	[Arguments("pf2e:class=No Such Page Anywhere", "WIKI: pf2e:class names a page that does not exist: 'No Such Page Anywhere'.")]
	[Arguments("pf2e:level=1|2", "WIKI: pf2e:level takes one value.")]
	[Arguments("pf2e:price=1e3", "WIKI: pf2e:price is a number like 1.5, not '1e3'.")]
	[Arguments("pf2e:published=August 2019", "WIKI: pf2e:published is a date like 2026-10-23, not 'August 2019'.")]
	public async Task AFactTheSchemaRefusesIsToldAndChangesNothing(string fact, string told)
	{
		var player = await PlayerAsync("WikiFactRefused", approved: true);
		var spell = await PageAsync("RefusedFactSpell");
		await RunAsync(player, $"@wiki/fact {spell.Slug}/pf2e:level=5");

		await RunAsync(player, $"@wiki/fact {spell.Slug}/{fact}");
		await Assert.That(Told(player, told)).IsEqualTo(1);
		await Assert.That(await FactsAsync(spell)).IsEquivalentTo([new WikiPageFact("pf2e:level", "5")]);
	}

	[Test]
	public async Task StaffDefineChangeShowAndRemoveAProperty()
	{
		var god = WebAppFactoryArg.ExecutorDBRef;
		var id = $"test:{TestIsolationHelpers.GenerateUniqueName("rank").ToLowerInvariant()}";
		int GodTold(string contains)
			=> WebAppFactoryArg.Notifications.DeliveriesFor(god).Count(d => d.Message.Contains(contains, StringComparison.Ordinal));
		Task GodRunsAsync(string command) => Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

		await GodRunsAsync($"@wiki/property {id}=keyword");
		await Assert.That(GodTold($"WIKI: Defined {id}: keyword.")).IsEqualTo(1);
		await GodRunsAsync($"@wiki/property {id}/values=Captain|Sergeant");
		await GodRunsAsync($"@wiki/property {id}/multiple=yes");
		await GodRunsAsync($"@wiki/property {id}/label=Rank");
		await GodRunsAsync($"@wiki/property {id}/description=Where a member stands.");
		await Assert.That(GodTold($"WIKI: Changed {id}.")).IsEqualTo(4);
		var defined = WikiFactEntry.Definition(await WikiService.GetFactSchemaAsync(), id)!;
		await Assert.That(defined with { AllowedKeywords = [] }).IsEqualTo(new PropertyDefinition(id,
			WikiFactProperties.SchemaVersion, SemanticType.Keyword, Cardinality.Multiple, [], "Rank", "Where a member stands."));
		await Assert.That(defined.AllowedKeywords.Select(k => k.Value)).IsEquivalentTo(["captain", "sergeant"]);

		await GodRunsAsync($"@wiki/property {id}");
		await Assert.That(GodTold($"Property: {id}")).IsEqualTo(1);
		await Assert.That(GodTold("captain | sergeant")).IsEqualTo(1);
		await GodRunsAsync($"@wiki/property {id}/multiple=maybe");
		await Assert.That(GodTold("WIKI: Multiple takes yes or no.")).IsEqualTo(1);

		// A page keeps the facts of a property that is removed.
		var page = await PageAsync("PropertyPage");
		(await WikiService.SetFactsAsync(page.Id, [new(id, "captain")])).Expect<WikiPage>();
		await GodRunsAsync($"@wiki/property {id}=");
		await Assert.That(GodTold($"WIKI: Removed {id}. Pages that state it keep their facts.")).IsEqualTo(1);
		await Assert.That(WikiFactEntry.Definition(await WikiService.GetFactSchemaAsync(), id)).IsNull();
		await Assert.That(await FactsAsync(page)).IsEquivalentTo([new WikiPageFact(id, "captain")]);
	}

	[Test]
	public async Task OnlyWikiAdminChangesAProperty()
	{
		var player = await PlayerAsync("WikiPropertyMortal", approved: true);
		var id = $"test:{TestIsolationHelpers.GenerateUniqueName("mortal").ToLowerInvariant()}";

		await RunAsync(player, $"@wiki/property {id}=number");
		await Assert.That(Told(player, "WIKI: Permission denied. That needs the wiki.admin permission.")).IsEqualTo(1);
		await Assert.That(WikiFactEntry.Definition(await WikiService.GetFactSchemaAsync(), id)).IsNull();
		// Anyone may read them.
		await RunAsync(player, "@wiki/property pf2e:level");
		await Assert.That(Told(player, "Property: pf2e:level")).IsEqualTo(1);
	}

	[Test]
	public async Task OnlyAnEditorSetsFacts()
	{
		var player = await PlayerAsync("WikiFactReader", approved: false);
		var spell = await PageAsync("ReadOnlyFactSpell");

		await RunAsync(player, $"@wiki/fact {spell.Slug}/pf2e:level=2");
		await Assert.That(Told(player, $"You can't edit '{spell.Title}'")).IsEqualTo(1);
		await Assert.That(await FactsAsync(spell)).IsEmpty();
	}

	[Test]
	public async Task AFactNamingAPageTheReaderMayNotReadIsLeftOut()
	{
		var player = await PlayerAsync("WikiFactHidden", approved: false);
		var spell = await PageAsync("HiddenTargetSpell");
		var draft = await PageAsync("DraftClass", published: false);
		await Assert.That((await WikiService.SetFactsAsync(spell.Id, [new("pf2e:class", draft.Id), new("pf2e:level", "1")])).Value is WikiPage).IsTrue();

		var marker = TestIsolationHelpers.GenerateUniqueName("hidden");
		await RunAsync(player, $"think {marker} [wikifact({spell.Slug})]/[wikifact({spell.Slug}, pf2e:class)]/");
		await Assert.That(Told(player, $"{marker} pf2e:level//")).IsEqualTo(1);
		await RunAsync(player, $"@wiki/fact {spell.Slug}");
		await Assert.That(Told(player, draft.Title)).IsEqualTo(0);
	}

	[Test]
	public async Task APageReferenceTheEditorCouldNotSeeOutlivesTheirEdit()
	{
		var spell = await PageAsync("UnseenFactSpell");
		var draftClass = await PageAsync("UnseenClass", published: false);
		var draftAncestry = await PageAsync("UnseenAncestry", published: false);
		var ancestry = await PageAsync("SeenAncestry");
		var stored = (await WikiService.SetFactsAsync(spell.Id,
			[new("pf2e:ancestry", draftAncestry.Id), new("pf2e:class", draftClass.Id), new("pf2e:level", "1")])).Expect<WikiPage>().Facts;

		// What a reader without drafts sends back after editing what they were shown: the level, changed, and an
		// ancestry of their own in place of the one they could not see, which takes one value.
		var kept = await WikiFactEntry.KeepingUnseenAsync(WikiService, await WikiService.GetFactSchemaAsync(), stored,
			[new("pf2e:level", "2"), new("pf2e:ancestry", ancestry.Id)], WikiVisibility.PublishedOnly);

		await Assert.That(kept).IsEquivalentTo(new WikiPageFact[]
			{ new("pf2e:level", "2"), new("pf2e:ancestry", ancestry.Id), new("pf2e:class", draftClass.Id) });
	}

	// The rebuild empties the shared world's index, so no test that queries it runs meanwhile.
	[Test, NotInParallel("wiki-fact-index")]
	public async Task TheFactIndexReportsItselfAndOnlyWikiAdminRebuildsIt()
	{
		var builder = WebAppFactoryArg.Services.GetRequiredService<IWikiFactIndexBuilder>();
		await builder.Idle;
		var mortal = await PlayerAsync("FactIndexMortal", approved: true);
		await RunAsync(mortal, "@wiki/fact/index");
		await Assert.That(Told(mortal, "WIKI: The fact index is ready: ")).IsEqualTo(1);

		await RunAsync(mortal, "@wiki/fact/index rebuild");
		await Assert.That(Told(mortal, "needs the wiki.admin permission")).IsEqualTo(1);
		await RunAsync(mortal, "@wiki/fact/index again");
		await Assert.That(Told(mortal, "Usage: @wiki/fact/index [rebuild]")).IsEqualTo(1);

		var wizard = await PlayerAsync("FactIndexWizard", approved: true);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		await RunAsync(wizard, "@wiki/fact/index rebuild");
		await Assert.That(Told(wizard, "WIKI: The fact index is clearing; it builds again after.")).IsEqualTo(1);
		await builder.Idle;
		await Assert.That((await Mediator.Send(new SharpMUSH.Library.Queries.Database.Wiki.GetWikiFactIndexStatusQuery())).Ready).IsTrue();
	}
}
