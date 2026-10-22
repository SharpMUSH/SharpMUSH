using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// Facts are page metadata: a page states them beside its categories, every edit of the page projects them, and
/// facts the schema refuses change nothing.
/// </summary>
public partial class IntegratedWikiMutationTests
{
	private static SemanticWikiQuery LevelEquals(long level) => Query(new SemanticPredicate.IntegerEquals("pf2e:level", level));

	private static async Task<List<string>> QueryIdsAsync(LightningDatabase provider, SemanticWikiQuery query)
		=> await SemanticWikiQueries.RunAsync(provider, "#test", _ => ValueTask.FromResult(AdminReader), query, null, CancellationToken.None) switch
		{
			SemanticQueryResult.Page page => [.. page.Rows.Select(r => r.PageId)],
			var other => throw new InvalidOperationException($"Expected a page, got {other}.")
		};

	private async Task<(SemanticWikiPrototypeHost Host, IWikiService Wiki, LightningDatabase Provider)> ActiveFactsHostAsync()
	{
		var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		if (!Settled(host)) throw new InvalidOperationException("Activation did not certify the empty package history.");
		return (host, host.Services.GetRequiredService<IWikiService>(), host.Services.GetRequiredService<LightningDatabase>());
	}

	[Test]
	public async Task FactsAPageStatesAreQueriedAndEveryEditKeepsThem()
	{
		var (host, wiki, provider) = await ActiveFactsHostAsync();
		await using var _ = host;
		var wizard = (await wiki.CreateAsync("Facts wizard", "# Wizard", "#1")).Expect<WikiPage>();
		var spell = (await wiki.CreateAsync("Facts spell", "# Spell", "#1", WikiNamespace.Main, null, ["Spells"])).Expect<WikiPage>();
		var set = (await wiki.SetFactsAsync(spell.Id, [new("pf2e:level", "3"), new("pf2e:class", wizard.Id), new("pf2e:tradition", "Arcane")]))
			.Expect<WikiPage>();
		await Assert.That(set.Facts).IsEquivalentTo([new WikiPageFact("pf2e:class", wizard.Id), new WikiPageFact("pf2e:level", "3"),
			new WikiPageFact("pf2e:tradition", "Arcane")], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(await QueryIdsAsync(provider, LevelEquals(3))).IsEquivalentTo([spell.Id]);
		var byClass = Query(new SemanticPredicate.PageEquals("pf2e:class", wizard.Id), new SemanticPredicate.KeywordEquals("pf2e:tradition", "arcane"));
		await Assert.That(await QueryIdsAsync(provider, byClass)).IsEquivalentTo([spell.Id]);

		// Every kind of edit keeps them.
		await Assert.That(await wiki.UpdateAsync(spell.Id, "# Spell, revised", "#1") is WikiPage).IsTrue();
		await Assert.That(await wiki.SetTitleAsync(spell.Id, "Facts spell, renamed") is WikiPage).IsTrue();
		await Assert.That(await wiki.SetMetadataAsync(spell.Id, ["Spells", "Evocation"], true) is WikiPage).IsTrue();
		await Assert.That(await QueryIdsAsync(provider, LevelEquals(3))).IsEquivalentTo([spell.Id]);
		await Assert.That(await QueryIdsAsync(provider, byClass)).IsEquivalentTo([spell.Id]);
		await Assert.That(((await wiki.GetByIdAsync(spell.Id)).Expect<WikiPage>()).Facts.Count).IsEqualTo(3);

		await Assert.That((await wiki.SetFactsAsync(spell.Id, [new("pf2e:level", "4")])).Value is WikiPage).IsTrue();
		await Assert.That(await QueryIdsAsync(provider, LevelEquals(3))).IsEmpty();
		await Assert.That(await QueryIdsAsync(provider, byClass)).IsEmpty();
		await Assert.That(await QueryIdsAsync(provider, LevelEquals(4))).IsEquivalentTo([spell.Id]);
		await Assert.That((await wiki.SetFactsAsync(spell.Id, [])).Value is WikiPage).IsTrue();
		await Assert.That(await QueryIdsAsync(provider, LevelEquals(4))).IsEmpty();
		await host.CloseAsync();
	}

	[Test]
	[Arguments("pf2e:unknown", "1", "No property pf2e:unknown", true)]
	[Arguments("pf2e:level", "three", "pf2e:level is a whole number", true)]
	[Arguments("pf2e:tradition", "elemental", "pf2e:tradition does not allow", true)]
	[Arguments("pf2e:class", "wiki_page/987654", "names a page that does not exist", true)]
	[Arguments("not a key", "1", "is not a property key", true)]
	[Arguments("pf2e:unknown", "1", "No property pf2e:unknown", false)]
	[Arguments("pf2e:level", "three", "pf2e:level is a whole number", false)]
	[Arguments("pf2e:tradition", "elemental", "pf2e:tradition does not allow", false)]
	[Arguments("pf2e:class", "wiki_page/987654", "names a page that does not exist", false)]
	[Arguments("not a key", "1", "is not a property key", false)]
	public async Task FactsTheSchemaRefusesAreAnErrorAndChangeNothing(string property, string value, string message, bool active)
	{
		SemanticWikiPrototypeHost host;
		if (active) host = (await ActiveFactsHostAsync()).Host;
		else { host = NewHost(); await host.OpenAsync(); }
		await using var _ = host;
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var spell = (await wiki.CreateAsync("Refused spell", "# Spell", "#1")).Expect<WikiPage>();
		await Assert.That((await wiki.SetFactsAsync(spell.Id, [new("pf2e:level", "5")])).Value is WikiPage).IsTrue();
		var before = active ? Task3Snapshot(provider) : null;
		var sequence = active ? ObserveSequence(provider) : default;

		var refused = await wiki.SetFactsAsync(spell.Id, [new("pf2e:level", "5"), new(property, value)]);
		await Assert.That(refused.Value is Error<string> { Value: var error } && error.Contains(message)).IsTrue().Because($"got {refused.Value}");
		await Assert.That(((await wiki.GetByIdAsync(spell.Id)).Expect<WikiPage>()).Facts).IsEquivalentTo([new WikiPageFact("pf2e:level", "5")]);
		if (active)
		{
			await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before!);
			await Assert.That(ObserveSequence(provider)).IsEqualTo(sequence);
			await Assert.That(await QueryIdsAsync(provider, LevelEquals(5))).IsEquivalentTo([spell.Id]);
		}
		await host.CloseAsync();
	}

	[Test]
	public async Task OneValueForASingleValuedPropertyAndThePageKeyIsReadAsItsId()
	{
		var (host, wiki, provider) = await ActiveFactsHostAsync();
		await using var _ = host;
		var human = (await wiki.CreateAsync("Facts human", "# Human", "#1")).Expect<WikiPage>();
		var feat = (await wiki.CreateAsync("Facts feat", "# Feat", "#1")).Expect<WikiPage>();
		var twice = await wiki.SetFactsAsync(feat.Id, [new("pf2e:level", "1"), new("pf2e:level", "2")]);
		await Assert.That(twice.Value is Error<string> { Value: var error } && error.Contains("takes one value")).IsTrue().Because($"got {twice.Value}");

		var key = human.Id["wiki_page/".Length..];
		var stated = (await wiki.SetFactsAsync(feat.Id, [new("pf2e:ancestry", key), new("pf2e:level", "1")])).Expect<WikiPage>();
		await Assert.That(stated.Facts).Contains(new WikiPageFact("pf2e:ancestry", human.Id));
		await Assert.That(await QueryIdsAsync(provider, Query(new SemanticPredicate.PageEquals("pf2e:ancestry", human.Id)))).IsEquivalentTo([feat.Id]);
		await host.CloseAsync();
	}

	[Test]
	public async Task FactsStatedBeforeActivationAreProjectedByIt()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var cleric = (await wiki.CreateAsync("Early cleric", "# Cleric", "#1")).Expect<WikiPage>();
		var spell = (await wiki.CreateAsync("Early spell", "# Spell", "#1")).Expect<WikiPage>();
		// A bare page key is stored as the page's id here too, so activation finds the page it names.
		await Assert.That((await wiki.SetFactsAsync(spell.Id, [new("pf2e:level", "6"), new("pf2e:class", cleric.Id["wiki_page/".Length..])])).Value is WikiPage).IsTrue();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(await QueryIdsAsync(provider, LevelEquals(6))).IsEquivalentTo([spell.Id]);
		await Assert.That(await QueryIdsAsync(provider, Query(new SemanticPredicate.PageEquals("pf2e:class", cleric.Id)))).IsEquivalentTo([spell.Id]);
		await host.CloseAsync();
	}

	[Test]
	public async Task ActivationRefusesAFactNamingAPageDeletedSinceItWasStated()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var cleric = (await wiki.CreateAsync("Deleted cleric", "# Cleric", "#1")).Expect<WikiPage>();
		var spell = (await wiki.CreateAsync("Early refused spell", "# Spell", "#1")).Expect<WikiPage>();
		await Assert.That((await wiki.SetFactsAsync(spell.Id, [new("pf2e:class", cleric.Id)])).Value is WikiPage).IsTrue();
		await Assert.That((await wiki.DeleteAsync(cleric.Id, "#1")).Value is None).IsTrue();
		await Assert.That(async () => await host.ActivateAtomicWikiProjectionAsync()).Throws<WikiFixtureMutationException>();
		await Assert.That(host.Services.GetRequiredService<LightningDatabase>().AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Inactive);
		await host.CloseAsync();
	}
}
