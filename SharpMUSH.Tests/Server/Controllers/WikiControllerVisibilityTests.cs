using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;
using System.Security.Claims;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Unit tests for the unpublished-page (draft) visibility rules on <see cref="WikiController"/>:
/// anonymous callers (and accounts without the wiki.read scope) must receive 404 for unpublished
/// pages and never see drafts in listings; callers holding wiki.read see everything; and a draft's
/// own author always sees it, even without wiki.read.
/// </summary>
public class WikiControllerVisibilityTests
{
	/// <summary>
	/// Builds a controller for a caller. <paramref name="canReadDrafts"/> grants the wiki.read scope
	/// (any Player+ member by default); when false and <paramref name="callerDbref"/> is set, the
	/// caller is authenticated but only sees drafts they authored. Pass authenticated: false for an
	/// anonymous caller.
	/// </summary>
	private static WikiController MakeController(
		InMemoryWikiService wiki, bool authenticated, bool canReadDrafts = true, string callerDbref = "#42")
	{
		var controller = new WikiController(
			wiki,
			Substitute.For<IPrerenderCacheService>(),
			NullLogger<WikiController>.Instance);

		// An identity without an authentication type reports IsAuthenticated == false.
		ClaimsIdentity identity;
		if (!authenticated)
		{
			identity = new ClaimsIdentity();
		}
		else
		{
			var claims = new List<Claim> { new(GameHub.CharacterDbrefClaim, callerDbref) };
			if (canReadDrafts)
				claims.Add(new Claim(PortalPermission.ClaimType, PortalPermission.WikiRead));
			identity = new ClaimsIdentity(claims, "test");
		}

		controller.ControllerContext = new ControllerContext
		{
			HttpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(identity)
			}
		};
		return controller;
	}

	private static async Task<(InMemoryWikiService Wiki, string Slug)> SeedUnpublishedPage()
	{
		var wiki = new InMemoryWikiService(new WikiMarkdigPipeline());
		var created = await wiki.CreateAsync("Draft Page", "# draft", "#1");
		var page = created.AsT0;
		await wiki.SetMetadataAsync(page.Id, null, [], published: false);
		return (wiki, page.Slug);
	}

	[Test]
	public async Task GetPage_Unpublished_Anonymous_Returns404()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var controller = MakeController(wiki, authenticated: false);

		var result = await controller.GetPage("main", "general", slug);

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}

	[Test]
	public async Task GetPage_Unpublished_Authenticated_Returns200()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var controller = MakeController(wiki, authenticated: true);

		var result = await controller.GetPage("main", "general", slug);

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task GetPage_Published_Anonymous_Returns200()
	{
		var wiki = new InMemoryWikiService(new WikiMarkdigPipeline());
		var created = await wiki.CreateAsync("Public Page", "# public", "#1");
		var controller = MakeController(wiki, authenticated: false);

		var result = await controller.GetPage("main", "general", created.AsT0.Slug);

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task GetRecentChanges_Anonymous_ExcludesUnpublished()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var controller = MakeController(wiki, authenticated: false);

		var result = await controller.GetRecentChanges();

		var ok = result as OkObjectResult;
		await Assert.That(ok).IsNotNull();
		var pages = ((IEnumerable<WikiController.WikiPageDto>)ok!.Value!).ToList();
		await Assert.That(pages.Any(p => p.Slug == slug)).IsFalse();
	}

	[Test]
	public async Task GetRecentChanges_Authenticated_IncludesUnpublished()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var controller = MakeController(wiki, authenticated: true);

		var result = await controller.GetRecentChanges();

		var ok = result as OkObjectResult;
		await Assert.That(ok).IsNotNull();
		var pages = ((IEnumerable<WikiController.WikiPageDto>)ok!.Value!).ToList();
		await Assert.That(pages.Any(p => p.Slug == slug)).IsTrue();
	}

	[Test]
	public async Task ListNamespacePages_Anonymous_ExcludesUnpublished()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var controller = MakeController(wiki, authenticated: false);

		var result = await controller.ListNamespacePages("main");

		var ok = result as OkObjectResult;
		await Assert.That(ok).IsNotNull();
		var pages = ((IEnumerable<WikiController.WikiPageDto>)ok!.Value!).ToList();
		await Assert.That(pages.Any(p => p.Slug == slug)).IsFalse();
	}

	[Test]
	public async Task ListAllPages_Anonymous_ExcludesUnpublishedAndOmitsTotalHeader()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var controller = MakeController(wiki, authenticated: false);

		var result = await controller.ListAllPages();

		var ok = result as OkObjectResult;
		await Assert.That(ok).IsNotNull();
		var pages = ((IEnumerable<WikiController.WikiPageDto>)ok!.Value!).ToList();
		await Assert.That(pages.Any(p => p.Slug == slug)).IsFalse();

		// The total count includes drafts, so it is withheld from anonymous callers to
		// avoid leaking how many unpublished pages exist.
		var header = controller.Response.Headers["X-Total-Count"].ToString();
		await Assert.That(header).IsEqualTo("");
	}

	[Test]
	public async Task ListAllPages_Authenticated_IncludesUnpublishedAndSetsTotalHeader()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var controller = MakeController(wiki, authenticated: true);

		var result = await controller.ListAllPages();

		var ok = result as OkObjectResult;
		await Assert.That(ok).IsNotNull();
		var pages = ((IEnumerable<WikiController.WikiPageDto>)ok!.Value!).ToList();
		await Assert.That(pages.Any(p => p.Slug == slug)).IsTrue();

		var header = controller.Response.Headers["X-Total-Count"].ToString();
		await Assert.That(header).IsEqualTo("1");
	}

	[Test]
	public async Task GetPage_Unpublished_Author_Returns200_EvenWithoutWikiRead()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var controller = MakeController(wiki, authenticated: true, canReadDrafts: false, callerDbref: "#1");

		var result = await controller.GetPage("main", "general", slug);

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task GetPage_Unpublished_AuthenticatedNonAuthorWithoutWikiRead_Returns404()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var controller = MakeController(wiki, authenticated: true, canReadDrafts: false, callerDbref: "#99");

		var result = await controller.GetPage("main", "general", slug);

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}
}
