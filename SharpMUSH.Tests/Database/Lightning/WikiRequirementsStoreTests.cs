using System.Text.Json.Nodes;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>Wiki requirement sets in the Lightning store, and the migration that turned protection into one.</summary>
public class WikiRequirementsStoreTests : LightningDatabaseFixture
{
	private IWikiStore Wiki => Db;

	private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private async Task<WikiPage> AddAsync(string slug)
	{
		var page = new WikiPage("", slug, slug, "main", "md", "<p>md</p>", "md", "#1", "#1", T0, T0, 1);
		return (await Wiki.CreatePageAsync(page)).Expect<WikiPage>();
	}

	private static WikiRequirementSet Set(WikiRuleTarget target, WikiAction action, params string[] scopes)
		=> new(target, new Dictionary<WikiAction, IReadOnlyList<string>> { [action] = scopes }, "#1", T0);

	[Test]
	public async Task SetsRoundTripAndAnEmptySetIsRemoved()
	{
		var page = await AddAsync("lore_page");
		var category = Set(WikiRuleTarget.ForCategory("lore"), WikiAction.Edit, "lore.edit");
		var pageSet = Set(WikiRuleTarget.ForPage(page.Id), WikiAction.Read, "lore.read", "wiki.read");

		(await Wiki.SetRequirementsAsync(category)).Expect<None>();
		(await Wiki.SetRequirementsAsync(pageSet)).Expect<None>();
		await ReopenAsync();

		var stored = new WikiRequirements(await Wiki.GetRequirementsAsync());
		await Assert.That(stored.For(category.Target)!.For(WikiAction.Edit)).IsEquivalentTo(["lore.edit"]);
		await Assert.That(stored.For(pageSet.Target)!.For(WikiAction.Read)).IsEquivalentTo(["lore.read", "wiki.read"]);
		await Assert.That(stored.For(pageSet.Target)!.UpdatedAt).IsEqualTo(T0);

		(await Wiki.SetRequirementsAsync(Set(category.Target, WikiAction.Edit))).Expect<None>();
		await Assert.That(new WikiRequirements(await Wiki.GetRequirementsAsync()).For(category.Target)).IsNull();
	}

	[Test]
	public async Task APageSetNeedsThePageAndGoesWithIt()
	{
		await Assert.That((await Wiki.SetRequirementsAsync(Set(WikiRuleTarget.ForPage("wiki_page/999999"), WikiAction.Edit, "wiki.admin"))).Value)
			.IsTypeOf<NotFound>();

		var page = await AddAsync("doomed");
		(await Wiki.SetRequirementsAsync(Set(WikiRuleTarget.ForPage(page.Id), WikiAction.Edit, "wiki.admin"))).Expect<None>();
		await Wiki.DeletePageAsync(page.Id);

		await Assert.That(new WikiRequirements(await Wiki.GetRequirementsAsync()).HasPageRules(page.Id)).IsFalse();
	}

	[Test]
	public async Task ANewWorldClosesTheSystemNamespaceToAllButWikiAdmin()
	{
		var system = new WikiRequirements(await Wiki.GetRequirementsAsync()).For(WikiRuleTarget.ForNamespace(WikiNamespace.System));

		await Assert.That(system).IsNotNull();
		foreach (var action in new[] { WikiAction.Create, WikiAction.Edit, WikiAction.Delete })
			await Assert.That(system!.For(action)).IsEquivalentTo([PortalPermission.WikiAdmin]);
		await Assert.That(system!.For(WikiAction.Read)).IsEmpty();
	}

	/// <summary>
	/// A world written while pages carried a protected flag: each protected page gets the page requirement
	/// protection now is, the flag leaves the row, and an unprotected page gets nothing.
	/// </summary>
	[Test]
	public async Task MigrationTurnsTheProtectedFlagIntoAPageRequirement()
	{
		var guarded = await AddAsync("guarded");
		var open = await AddAsync("open");
		var guardedKey = Keys.Dbref(long.Parse(guarded.Id["wiki_page/".Length..]));
		await Db.Store.WriteAsync(tx =>
		{
			tx.TryGet(Tables.WikiPage, guardedKey, out var bytes);
			var row = JsonNode.Parse(bytes)!.AsObject();
			row["IsProtected"] = true;
			tx.Put(Tables.WikiPage, guardedKey, System.Text.Encoding.UTF8.GetBytes(row.ToJsonString()));
		});
		await ForgetIndexAsync(LightningDatabase.WikiRequirementsMigrationId, Tables.WikiRequirement);

		await Db.Migrate();

		var requirements = new WikiRequirements(await Wiki.GetRequirementsAsync());
		var page = requirements.For(WikiRuleTarget.ForPage(guarded.Id));
		await Assert.That(page).IsNotNull();
		await Assert.That(page!.For(WikiAction.Edit)).IsEquivalentTo([PortalPermission.WikiAdmin]);
		await Assert.That(page.For(WikiAction.Delete)).IsEquivalentTo([PortalPermission.WikiAdmin]);
		await Assert.That(requirements.HasPageRules(open.Id)).IsFalse();
		await Assert.That(requirements.For(WikiRuleTarget.ForNamespace(WikiNamespace.System))).IsNotNull();

		var rewritten = Db.Store.Read(tx => tx.TryGet(Tables.WikiPage, guardedKey, out var bytes)
			? System.Text.Encoding.UTF8.GetString(bytes)
			: string.Empty);
		await Assert.That(rewritten).DoesNotContain("IsProtected");
	}
}
