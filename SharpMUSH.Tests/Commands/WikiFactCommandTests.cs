using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models.Wiki;
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
}
