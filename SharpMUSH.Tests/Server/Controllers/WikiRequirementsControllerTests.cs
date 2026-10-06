using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// <c>/api/wiki/requirements</c> and what a requirement does to the rest of the wiki API: a read requirement
/// takes the page out of every answer, an edit requirement refuses the edit, and the page DTO says which
/// buttons the reader gets.
/// </summary>
public class WikiRequirementsControllerTests
{
	private static readonly string[] Admin =
		[PortalPermission.WikiAdmin, PortalPermission.WikiDrafts, PortalPermission.WikiCreate, PortalPermission.WikiEdit, PortalPermission.WikiDelete];

	private static WikiEndpoints As(WikiStoreService wiki, params string[] scopes)
		=> WikiControllerTestHarness.Build(wiki, authenticated: true, "#42", scopes).Wiki;

	private static async Task<(WikiStoreService Wiki, WikiPage Page)> SeedAsync()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var page = (await wiki.CreateAsync("Dragons", "# body", "#1")).Expect<WikiPage>();
		page = (await wiki.SetMetadataAsync(page.Id, ["lore"], published: true)).Expect<WikiPage>();
		return (wiki, page);
	}

	private static SetRequirementsRequest Require(string action, params string[] scopes)
		=> new(new Dictionary<string, IReadOnlyList<string>> { [action] = scopes });

	[Test]
	public async Task PutSetsACategoryRequirementThatRefusesAnEdit()
	{
		var (wiki, _) = await SeedAsync();

		var put = await As(wiki, Admin).Requirements.Put("category", "Lore", Require("edit", PortalPermission.MediaAdmin));
		var set = (WikiRequirementSetDto)((OkObjectResult)put).Value!;
		await Assert.That(set.Key).IsEqualTo("lore");
		await Assert.That(set.Required["edit"]).IsEquivalentTo([PortalPermission.MediaAdmin]);

		var editor = As(wiki, PortalPermission.WikiEdit);
		await Assert.That(await editor.Pages.UpdatePage("dragons", new UpdatePageRequest("# changed", null)))
			.IsTypeOf<ForbidResult>();
		var dto = (WikiPageDto)((OkObjectResult)await editor.Pages.GetPage("main", "dragons")).Value!;
		await Assert.That(dto.Access).IsEqualTo(new WikiAccessDto(Read: true, Edit: false, Delete: false, Manage: false));

		var allowed = As(wiki, PortalPermission.WikiEdit, PortalPermission.MediaAdmin);
		await Assert.That(await allowed.Pages.UpdatePage("dragons", new UpdatePageRequest("# changed", null)))
			.IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task PutRefusesAnUnknownActionOrPermission()
	{
		var (wiki, _) = await SeedAsync();
		var admin = As(wiki, Admin);

		await Assert.That(await admin.Requirements.Put("category", "lore", Require("smite", PortalPermission.WikiAdmin)))
			.IsTypeOf<BadRequestObjectResult>();
		await Assert.That(await admin.Requirements.Put("category", "lore", Require("edit", "no.such.scope")))
			.IsTypeOf<BadRequestObjectResult>();
		await Assert.That(await admin.Requirements.Put("namespace", "nowhere", Require("edit", PortalPermission.WikiAdmin)))
			.IsTypeOf<NotFoundResult>();
		await Assert.That(await admin.Requirements.Put("page", "wiki_page/404", Require("edit", PortalPermission.WikiAdmin)))
			.IsTypeOf<NotFoundResult>();
	}

	[Test]
	public async Task AReadRequirementTakesThePageOutOfEveryAnswer()
	{
		var (wiki, page) = await SeedAsync();
		await As(wiki, Admin).Requirements.Put("page", page.Id, Require("read", PortalPermission.MediaAdmin));

		var reader = As(wiki);
		await Assert.That(await reader.Pages.GetPage("main", "dragons")).IsTypeOf<NotFoundResult>();
		await Assert.That(await reader.Revisions.GetRevisions("dragons")).IsTypeOf<NotFoundResult>();
		await Assert.That(await reader.Requirements.GetForPage("dragons")).IsTypeOf<NotFoundResult>();
		var listed = (IEnumerable<WikiPageSummaryDto>)((OkObjectResult)await reader.Browse.ListAllPages()).Value!;
		await Assert.That(listed.Select(p => p.Slug)).DoesNotContain("dragons");
		await Assert.That(reader.Browse.Response.Headers["X-Total-Count"].ToString()).IsEqualTo("0");
		await Assert.That((WikiPageCountsDto)((OkObjectResult)await reader.Browse.GetCounts()).Value!)
			.IsEqualTo(new WikiPageCountsDto(Total: 0, Published: 0, Drafts: 0, Restricted: 0));

		var dto = (WikiPageDto)((OkObjectResult)await As(wiki, Admin).Pages.GetPage("main", "dragons")).Value!;
		await Assert.That(dto.IsRestricted).IsTrue();
		await Assert.That(dto.Access!.Manage).IsTrue();
	}

	[Test]
	public async Task APageUnderACategoryRequirementSaysPermissionsApply()
	{
		var (wiki, _) = await SeedAsync();
		var reader = As(wiki);
		var dto = (WikiPageDto)((OkObjectResult)await reader.Pages.GetPage("main", "dragons")).Value!;
		await Assert.That(dto.IsRestricted).IsFalse();

		await As(wiki, Admin).Requirements.Put("category", "lore", Require("edit", PortalPermission.MediaAdmin));
		dto = (WikiPageDto)((OkObjectResult)await reader.Pages.GetPage("main", "dragons")).Value!;
		await Assert.That(dto.IsRestricted).IsTrue().Because("the page inherits its category's edit requirement");
	}

	[Test]
	public async Task ACategoryPageClosedToTheReaderKeepsItsNameFromThem()
	{
		var (wiki, _) = await SeedAsync();
		var category = (await wiki.CreateAsync("Lore", "Stories.", "#1", WikiNamespace.Category, "en")).Expect<WikiPage>();
		await wiki.SetMetadataAsync(category.Id, [], published: true);

		var names = (IReadOnlyDictionary<string, string>)((OkObjectResult)await As(wiki).Browse.GetCategoryNames()).Value!;
		await Assert.That(names["lore"]).IsEqualTo("Lore");

		await As(wiki, Admin).Requirements.Put("page", category.Id, Require("read", PortalPermission.MediaAdmin));
		names = (IReadOnlyDictionary<string, string>)((OkObjectResult)await As(wiki).Browse.GetCategoryNames()).Value!;
		await Assert.That(names.ContainsKey("lore")).IsFalse();
	}

	[Test]
	public async Task APageListsWhatItInheritsAndTheCallerIsToldWhy()
	{
		var (wiki, page) = await SeedAsync();
		var admin = As(wiki, Admin);
		await admin.Requirements.Put("category", "lore", Require("edit", PortalPermission.MediaAdmin));
		await admin.Pages.UpdatePage("dragons", new UpdatePageRequest("# by admin", null));

		var requirements = (WikiPageRequirementsDto)((OkObjectResult)await As(wiki).Requirements.GetForPage("dragons")).Value!;
		await Assert.That(requirements.Page.Key).IsEqualTo(page.Id);
		await Assert.That(requirements.Page.Required).IsEmpty();
		await Assert.That(requirements.Inherited.Select(set => (set.Scope, set.Key))).IsEquivalentTo([("category", "lore")]);

		var why = (List<WikiAccessExplanationDto>)((OkObjectResult)await As(wiki, PortalPermission.WikiEdit).Requirements.GetAccess("dragons")).Value!;
		await Assert.That(why.Single(answer => answer.Action == "edit"))
			.IsEqualTo(new WikiAccessExplanationDto("edit", false, "category lore requires media.admin"));
		await Assert.That(why.Single(answer => answer.Action == "read").Allowed).IsTrue();
	}

	[Test]
	public async Task ProtectingIsAPageRequirementOfWikiAdmin()
	{
		var (wiki, page) = await SeedAsync();

		await Assert.That(await As(wiki, Admin).Admin.SetProtection("dragons", new SetProtectionRequest(true))).IsTypeOf<OkResult>();

		var set = (await wiki.GetRequirementsAsync()).For(WikiRuleTarget.ForPage(page.Id));
		await Assert.That(set!.For(WikiAction.Edit)).IsEquivalentTo([PortalPermission.WikiAdmin]);
		await Assert.That(await As(wiki, PortalPermission.WikiEdit).Admin.SetMetadata("dragons", new SetMetadataRequest(["lore"], true)))
			.IsTypeOf<ForbidResult>();

		await As(wiki, Admin).Admin.SetProtection("dragons", new SetProtectionRequest(false));
		await Assert.That((await wiki.GetRequirementsAsync()).HasPageRules(page.Id)).IsFalse();
	}
}
