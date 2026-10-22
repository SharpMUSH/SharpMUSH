using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// A read-requirement change on an active projection moves every page its target applies to into the visibility
/// class its new rules decide, in the requirement's own job, or is refused before any write when too many pages
/// would move. Readers then see exactly what the access service decides on the live pages.
/// </summary>
public partial class IntegratedWikiMutationTests
{
	private static IReadOnlyDictionary<WikiAction, IReadOnlyList<string>> ReadOnly(string scope)
		=> new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = [scope] };

	[Test]
	[Arguments(WikiRuleScope.Page)]
	[Arguments(WikiRuleScope.Category)]
	[Arguments(WikiRuleScope.Namespace)]
	public async Task AReadRequirementChangeMovesEveryPageItAppliesToAndBackAgain(WikiRuleScope scope)
	{
		await using var world = await QueryWorldAsync(spells: 12);
		var provider = world.Provider;
		var wiki = world.Host.Services.GetRequiredService<IWikiService>();
		var spell = world.Pages.Values.First(p => p.Order == 1);
		var target = scope switch
		{
			WikiRuleScope.Page => WikiRuleTarget.ForPage(spell.Id),
			WikiRuleScope.Category => WikiRuleTarget.ForCategory("spells"),
			_ => WikiRuleTarget.ForNamespace("main")
		};
		await Assert.That(await wiki.SetRequirementsAsync(target, ReadOnly("lore.read"), "#1") is None).IsTrue();
		await AssertIndexesMatchFacts(provider);
		await AssertQueriesMatchOracle(world, p => p.Level is >= 1 and <= 10);
		var moved = ObserveProjection(provider, spell.Id).State.State.Summary.Visibility.RuleIds;
		await Assert.That(moved).Contains(target.ToString());

		await Assert.That(await wiki.SetRequirementsAsync(target, new Dictionary<WikiAction, IReadOnlyList<string>>(), "#1") is None).IsTrue();
		await AssertIndexesMatchFacts(provider);
		await AssertQueriesMatchOracle(world, p => p.Level is >= 1 and <= 10);
		await Assert.That(ObserveProjection(provider, spell.Id).State.State.Summary.Visibility.RuleIds).DoesNotContain(target.ToString());
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task APageReadRuleChangesOnlyThatPagesProjectionRows()
	{
		await using var world = await QueryWorldAsync(spells: 6);
		var provider = world.Provider;
		var spell = world.Pages.Values.First(p => p.Order == 1);
		var previous = ObserveProjection(provider, spell.Id);
		var subject = previous.State.State.Summary.NativeId;
		var before = Task3Snapshot(provider);
		await world.Host.Services.GetRequiredService<IWikiService>().SetRequirementsAsync(WikiRuleTarget.ForPage(spell.Id), ReadOnly("lore.read"), "#1");
		var after = Task3Snapshot(provider);
		var changed = after.Except(before).Concat(before.Except(after)).Where(row => !IsFixtureVersionRow(row))
			.Select(row => row[..row.LastIndexOf(':')]).Distinct().ToArray();
		string Identity(TableDef table, byte[] key) => table.Name + ":" + Convert.ToHexString(key);
		var subjectHex = Convert.ToHexString(WikiSemanticKeys.Subject(subject));
		bool Allowed(string identity)
			=> identity == Identity(Tables.WikiRequirement, Keys.Composite("page", spell.Id))
				|| identity == Identity(AtomicWikiLayout.Summaries, WikiSemanticKeys.Subject(subject))
				// Its postings move to the new class; its forward rows and its facts do not change.
				|| identity.StartsWith(AtomicWikiLayout.Postings.Name + ":", StringComparison.Ordinal) && identity.EndsWith(subjectHex, StringComparison.Ordinal)
				|| identity.StartsWith(AtomicWikiLayout.Catalog.Name + ":", StringComparison.Ordinal)
				|| identity.StartsWith(AtomicWikiLayout.Partitions.Name + ":", StringComparison.Ordinal)
				|| identity.StartsWith(AtomicWikiLayout.Registry.Name + ":05", StringComparison.Ordinal)
				|| new[] { "02", "19", "1E", "1F" }.Any(tag => identity.StartsWith(AtomicWikiLayout.Metadata.Name + ":" + tag, StringComparison.Ordinal));
		await Assert.That(changed.Where(identity => !Allowed(identity))).IsEmpty();
		await Assert.That(changed).Contains(Identity(AtomicWikiLayout.Summaries, WikiSemanticKeys.Subject(subject)));
		var current = ObserveProjection(provider, spell.Id);
		await Assert.That(current.State.State.Summary.StateVersion).IsEqualTo(previous.State.State.Summary.StateVersion + 1);
		await Assert.That(current.State.State.ClassId).IsNotEqualTo(previous.State.State.ClassId);
		await Assert.That(current.Facts.Facts.Length).IsEqualTo(previous.Facts.Facts.Length);
		await AssertIndexesMatchFacts(provider);
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task AReadChangeOverTheCapIsRefusedBeforeAnyWrite()
	{
		await using var world = await QueryWorldAsync(spells: 6);
		var provider = world.Provider;
		provider.ReadRewritePageLimit = 3;
		var before = Task3Snapshot(provider);
		await AssertFixtureFailure(() => world.Host.Services.GetRequiredService<IWikiService>()
			.SetRequirementsAsync(WikiRuleTarget.ForNamespace("main"), ReadOnly("lore.read"), "#1"), WikiFixtureMutationFailure.WorkLimit);
		await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task AReadChangeMidReadIsAuthorizationChangedAndAPagedQueryRestarts()
	{
		await using var world = await QueryWorldAsync(spells: 8);
		var provider = world.Provider;
		var wiki = world.Host.Services.GetRequiredService<IWikiService>();
		// A read rule no page carries moves only the requirements pin.
		var unused = await ProbeReadAsync(world.Host, async (_, _) =>
			await wiki.SetRequirementsAsync(WikiRuleTarget.ForCategory("unused"), ReadOnly("lore.read"), "#1"));
		await Assert.That(unused is SemanticReadResult<string>.AuthorizationChanged).IsTrue();
		// One that moves pages moves their projection rows in the same job, so the content pin moves too, and that comes first.
		var moving = await ProbeReadAsync(world.Host, async (_, _) =>
			await wiki.SetRequirementsAsync(WikiRuleTarget.ForCategory("spells"), ReadOnly("lore.read"), "#1"));
		await Assert.That(moving is SemanticReadResult<string>.DataChanged).IsTrue();
		var first = await SemanticWikiQueries.RunAsync(provider, "#test", _ => ValueTask.FromResult(AdminReader), LevelQuery with { PageSize = 2 }, null, CancellationToken.None);
		var token = ((SemanticQueryResult.Page)first).Continuation;
		await wiki.SetRequirementsAsync(WikiRuleTarget.ForCategory("spells"), new Dictionary<WikiAction, IReadOnlyList<string>>(), "#1");
		await Assert.That(await SemanticWikiQueries.RunAsync(provider, "#test", _ => ValueTask.FromResult(AdminReader), LevelQuery with { PageSize = 2 }, token,
			CancellationToken.None) is SemanticQueryResult.Restart).IsTrue();
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task AnActivePackagePageWithAReadRequirementInstallsSettlesAndProjectsUnderItsRule()
	{
		await using var host = await ActiveHostAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		var outcome = await Coordinated(host, _ => installer.ApplyAsync(WikiPackage("active-secret", RoadPage("secret", "      view: [wiki.admin]")), MatrixRequest()));
		await Assert.That(outcome.Value is PackageOperationRan<Result<PackageApplyResult>> { Result.Value: PackageApplyResult }).IsTrue();
		await Assert.That(Settled(host)).IsTrue();
		var page = (await host.Services.GetRequiredService<IWikiService>().GetBySlugAsync("secret_road", WikiNamespace.Main)).Expect<WikiPage>();
		var projection = ObserveProjection(provider, page.Id).State;
		await Assert.That(projection.Source is PortableWikiSource).IsTrue();
		await Assert.That(projection.State.Summary.Visibility.RuleIds).Contains(WikiRuleTarget.ForPage(page.Id).ToString());
		await AssertIndexesMatchFacts(provider);
		await host.CloseAsync();
	}
}
