using SharpMUSH.Library;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Authentication;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// The wiki permission rule: an action needs its global scope and everything the page's namespace,
/// categories and the page itself require; requirements add up and never lift each other; wiki.admin skips
/// them; drafts are their author's and wiki.drafts holders'.
/// </summary>
public class WikiAccessServiceTests
{
	private const string LoreEdit = "lore.edit";
	private const string LoreRead = "lore.read";

	private static readonly WikiReader Player = WikiReader.From(
		[PortalPermission.WikiRead, PortalPermission.WikiCreate, PortalPermission.WikiEdit], "#42");

	private static WikiReader With(WikiReader reader, params string[] scopes) => WikiReader.From([.. reader.Scopes, .. scopes], reader.Dbref);

	private static readonly WikiReader Admin = WikiReader.From(
		[PortalPermission.WikiRead, PortalPermission.WikiDrafts, PortalPermission.WikiCreate, PortalPermission.WikiEdit,
			PortalPermission.WikiDelete, PortalPermission.WikiAdmin], "#1");

	private static async Task<(WikiStoreService Wiki, WikiAccessService Access, InMemoryRoleRegistry Roles)> BuildAsync()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var roles = InMemoryRoleRegistry.Seeded();
		foreach (var scope in new[] { LoreEdit, LoreRead })
		{
			await roles.UpsertCustomPermissionAsync(new CustomPermission(scope, "Wiki", scope, 0));
		}

		return (wiki, new WikiAccessService(wiki, roles, new PermissionResolver(), Substitute.For<IAccountStore>()), roles);
	}

	private static Dictionary<WikiAction, IReadOnlyList<string>> Require(WikiAction action, params string[] scopes)
		=> new() { [action] = scopes };

	private static async Task<WikiPage> PageAsync(WikiStoreService wiki, string title, params string[] categories)
	{
		var page = (await wiki.CreateAsync(title, "body", "#1")).Expect<WikiPage>();
		return (await wiki.SetMetadataAsync(page.Id, categories, published: true)).Expect<WikiPage>();
	}

	/// <summary>
	/// An account edits its own characters' biographies by owning them, whichever character is acting:
	/// no wiki.create or wiki.edit, and no requirement on the namespace or the page, stands in the way.
	/// </summary>
	[Test]
	public async Task AnAccountEditsItsOwnCharactersBiographies_WithoutAnyPermission()
	{
		var (wiki, access, _) = await BuildAsync();
		var bio = (await wiki.CreateAsync("Tomas Reyes", "body", "#1", WikiNamespace.Character)).Expect<WikiPage>();
		var other = (await wiki.CreateAsync("Ilsa Varn", "body", "#1", WikiNamespace.Character)).Expect<WikiPage>();
		await access.SetRequirementsAsync(Admin, "#1", WikiRuleTarget.ForPage(bio.Id), WikiRequirementSet.Protection);

		// Playing Pell, whose account also holds Tomas; the account has no wiki permission at all.
		var owner = WikiReader.From([], "#317", ["tomas_reyes", "pell_marsh"]);

		var edit = await access.DecideAsync(owner, bio, WikiAction.Edit);
		await Assert.That(edit.Allowed).IsTrue();
		await Assert.That(edit.Describe()).IsEqualTo("allowed (your own character's biography)");
		await Assert.That((await access.ForPageAsync(owner, bio)).Edit).IsTrue();
		await Assert.That((await access.DecideCreateAsync(owner, "character", [], "Pell Marsh")).Allowed).IsTrue();
		await Assert.That((await access.DecideCategoriesAsync(owner, bio, ["lore"])).Allowed).IsTrue();

		await Assert.That((await access.DecideAsync(owner, bio, WikiAction.Delete)).Allowed).IsFalse()
			.Because("owning a biography is reading, writing and editing it, not deleting it");
		await Assert.That((await access.DecideAsync(owner, other, WikiAction.Edit)).Allowed).IsFalse()
			.Because("Ilsa is on another account");
		await Assert.That((await access.DecideCreateAsync(owner, "main", [], "Tomas Reyes")).Allowed).IsFalse()
			.Because("only the character namespace holds biographies");
	}

	[Test]
	public async Task ACategoryRequirementIsAddedToTheGlobalScope()
	{
		var (wiki, access, _) = await BuildAsync();
		var page = await PageAsync(wiki, "Dragons", "lore");
		(await access.SetRequirementsAsync(Admin, "#1", WikiRuleTarget.ForCategory("Lore"), Require(WikiAction.Edit, LoreEdit)))
			.Expect<WikiRequirements>();

		var refused = await access.DecideAsync(Player, page, WikiAction.Edit);
		await Assert.That(refused.Allowed).IsFalse();
		await Assert.That(refused.Describe()).IsEqualTo("category lore requires lore.edit");

		await Assert.That((await access.DecideAsync(With(Player, LoreEdit), page, WikiAction.Edit)).Allowed).IsTrue();
		await Assert.That((await access.DecideAsync(WikiReader.From([PortalPermission.WikiRead, LoreEdit], "#42"), page, WikiAction.Edit)).Describe())
			.IsEqualTo("needs wiki.edit")
			.Because("a requirement adds to the global scope; it does not stand in for it");
	}

	[Test]
	public async Task EveryLevelMustPassAndDeletingNeedsWhatEditingDoes()
	{
		var (wiki, access, _) = await BuildAsync();
		var page = await PageAsync(wiki, "Dragons", "lore");
		await access.SetRequirementsAsync(Admin, "#1", WikiRuleTarget.ForCategory("lore"), Require(WikiAction.Edit, LoreEdit));
		await access.SetRequirementsAsync(Admin, "#1", WikiRuleTarget.ForPage(page.Id), WikiRequirementSet.Protection);

		await Assert.That((await access.DecideAsync(With(Player, LoreEdit), page, WikiAction.Edit)).Describe())
			.IsEqualTo("the page requires wiki.admin");
		await Assert.That((await access.DecideAsync(With(Player, LoreEdit, PortalPermission.WikiDelete), page, WikiAction.Delete)).Allowed)
			.IsFalse();

		var bypass = await access.DecideAsync(Admin, page, WikiAction.Delete);
		await Assert.That(bypass.Allowed).IsTrue();
		await Assert.That(bypass.Bypassed).IsTrue();
	}

	[Test]
	public async Task AReadRequirementHidesThePageEverywhere()
	{
		var (wiki, access, _) = await BuildAsync();
		var secret = await PageAsync(wiki, "Secret", "lore");
		var open = await PageAsync(wiki, "Open");
		await access.SetRequirementsAsync(Admin, "#1", WikiRuleTarget.ForCategory("lore"), Require(WikiAction.Read, LoreRead));

		await Assert.That(await access.CanSeeAsync(Player, secret)).IsFalse();
		await Assert.That((await access.DecideAsync(With(Player, LoreEdit), secret, WikiAction.Edit)).Allowed)
			.IsFalse().Because("every action needs what reading the page needs");
		await Assert.That(await access.CanSeeAsync(With(Player, LoreRead), secret)).IsTrue();

		var visibility = await access.VisibilityAsync(Player);
		var listed = await wiki.GetAllPagesAsync(0, 50, visibility: visibility);
		await Assert.That(listed.Select(p => p.Id)).Contains(open.Id);
		await Assert.That(listed.Select(p => p.Id)).DoesNotContain(secret.Id);
		await Assert.That(await wiki.CountPagesAsync(null, visibility)).IsEqualTo(listed.Count);
	}

	[Test]
	public async Task AnAnonymousReaderHoldsWhatTheEveryoneRoleGrants()
	{
		var (wiki, access, roles) = await BuildAsync();
		var page = await PageAsync(wiki, "Public");

		var anonymous = await access.AnonymousAsync();
		await Assert.That(anonymous.Has(PortalPermission.WikiRead)).IsTrue();
		await Assert.That(anonymous.Has(PortalPermission.WikiEdit)).IsFalse();
		await Assert.That(await access.CanSeeAsync(anonymous, page)).IsTrue();

		var everyone = (await roles.GetRoleAsync(BuiltInRoles.EveryoneSlug)).Expect<SharpRole>();
		everyone.Permissions[PortalPermission.WikiRead] = PermissionState.Deny;
		await roles.UpsertRoleAsync(everyone);

		await Assert.That(await access.CanSeeAsync(await access.AnonymousAsync(), page)).IsFalse();
	}

	[Test]
	public async Task DraftsAreTheAuthorsAndWikiDraftsHolders()
	{
		var (wiki, access, _) = await BuildAsync();
		var draft = (await wiki.CreateAsync("Draft", "body", "#7")).Expect<WikiPage>();
		draft = (await wiki.SetMetadataAsync(draft.Id, [], published: false)).Expect<WikiPage>();

		await Assert.That(await access.CanSeeAsync(Player, draft)).IsFalse();
		await Assert.That(await access.CanSeeAsync(WikiReader.From(Player.Scopes, "#7"), draft)).IsTrue();
		await Assert.That(await access.CanSeeAsync(With(Player, PortalPermission.WikiDrafts), draft)).IsTrue();
	}

	[Test]
	public async Task FilingAPageNeedsTheEditUnderItsOldAndItsNewCategories()
	{
		var (wiki, access, _) = await BuildAsync();
		var plain = await PageAsync(wiki, "Plain");
		var filed = await PageAsync(wiki, "Filed", "lore");
		await access.SetRequirementsAsync(Admin, "#1", WikiRuleTarget.ForCategory("lore"), Require(WikiAction.Edit, LoreEdit));

		await Assert.That((await access.DecideCategoriesAsync(Player, plain, ["Lore"])).Allowed)
			.IsFalse().Because("a page cannot be moved into a category its new rules would close to the mover");
		await Assert.That((await access.DecideCategoriesAsync(Player, filed, [])).Allowed)
			.IsFalse().Because("nor out of one its current rules close to them");
		await Assert.That((await access.DecideCategoriesAsync(With(Player, LoreEdit), plain, ["lore"])).Allowed).IsTrue();
	}

	[Test]
	public async Task CreatingNeedsTheNamespaceAndTheStartingCategories()
	{
		var (_, access, _) = await BuildAsync();
		await access.SetRequirementsAsync(Admin, "#1", WikiRuleTarget.ForCategory("lore"), Require(WikiAction.Create, LoreEdit));

		await Assert.That((await access.DecideCreateAsync(Player, "main", [])).Allowed).IsTrue();
		await Assert.That((await access.DecideCreateAsync(Player, "main", ["Lore"])).Allowed).IsFalse();

		await access.SetRequirementsAsync(Admin, "#1", WikiRuleTarget.ForNamespace(WikiNamespace.System),
			Require(WikiAction.Create, PortalPermission.WikiAdmin));
		await Assert.That((await access.DecideCreateAsync(Player, "System", [])).Describe())
			.IsEqualTo("namespace system requires wiki.admin");
	}

	/// <summary>
	/// A biography is filed in Character whatever the request names, so Character's requirements apply to
	/// creating one and to changing its categories even when the request leaves Character out.
	/// </summary>
	[Test]
	public async Task TheNamespaceCategory_CountsTowardCreateAndCategoryRequirements()
	{
		var (wiki, access, _) = await BuildAsync();
		var bio = (await wiki.CreateAsync("Ilsa Varn", "body", "#1", WikiNamespace.Character)).Expect<WikiPage>();
		await access.SetRequirementsAsync(Admin, "#1", WikiRuleTarget.ForCategory(WikiHelpers.CharacterCategory),
			new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Create] = [LoreEdit], [WikiAction.Edit] = [LoreEdit] });

		await Assert.That((await access.DecideCreateAsync(Player, "character", [])).Describe())
			.IsEqualTo("category character requires lore.edit");
		await Assert.That((await access.DecideCreateAsync(Player, "main", [])).Allowed).IsTrue();
		await Assert.That((await access.DecideCreateAsync(With(Player, LoreEdit), "character", [])).Allowed).IsTrue();
		await Assert.That((await access.DecideCategoriesAsync(Player, bio, [])).Allowed).IsFalse();
	}

	[Test]
	public async Task OnlyWikiAdminSetsRequirementsAndOnlyToKnownPermissions()
	{
		var (_, access, _) = await BuildAsync();
		var target = WikiRuleTarget.ForCategory("lore");

		await Assert.That((await access.SetRequirementsAsync(Player, "#42", target, Require(WikiAction.Edit, LoreEdit))).Value)
			.IsTypeOf<Error<string>>();
		await Assert.That((await access.SetRequirementsAsync(Admin, "#1", target, Require(WikiAction.Edit, "nope.never"))).Value)
			.IsTypeOf<Error<string>>();
		await Assert.That((await access.SetRequirementsAsync(Admin, "#1", WikiRuleTarget.ForPage("wiki_page/404"), Require(WikiAction.Edit, LoreEdit))).Value)
			.IsTypeOf<NotFound>();

		var set = (await access.SetRequirementsAsync(Admin, "#1", target, Require(WikiAction.Edit, LoreEdit))).Expect<WikiRequirements>();
		set = (await access.SetRequirementsAsync(Admin, "#1", target, Require(WikiAction.Read, LoreRead))).Expect<WikiRequirements>();
		await Assert.That(set.For(target)!.For(WikiAction.Edit)).IsEquivalentTo([LoreEdit])
			.Because("an action left out keeps what it had");
		set = (await access.SetRequirementsAsync(Admin, "#1", target, new Dictionary<WikiAction, IReadOnlyList<string>>
		{
			[WikiAction.Edit] = [], [WikiAction.Read] = []
		})).Expect<WikiRequirements>();
		await Assert.That(set.For(target)).IsNull();
	}
}
