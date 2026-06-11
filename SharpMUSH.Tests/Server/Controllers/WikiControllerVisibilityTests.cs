using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;
using System.Security.Claims;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Unit tests for the unpublished-page (draft) visibility rules on <see cref="WikiController"/>:
/// anonymous callers must receive 404 for unpublished pages and never see drafts in
/// listings, while authenticated callers see everything.
/// </summary>
public class WikiControllerVisibilityTests
{
	private static WikiController MakeController(InMemoryWikiService wiki, bool authenticated)
	{
		var controller = new WikiController(
			wiki,
			Substitute.For<IPrerenderCacheService>(),
			NullLogger<WikiController>.Instance);

		// An identity without an authentication type reports IsAuthenticated == false.
		var identity = authenticated
			? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "#42")], "test")
			: new ClaimsIdentity();

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

		var result = await controller.GetPage(slug);

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}

	[Test]
	public async Task GetPage_Unpublished_Authenticated_Returns200()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var controller = MakeController(wiki, authenticated: true);

		var result = await controller.GetPage(slug);

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task GetPage_Published_Anonymous_Returns200()
	{
		var wiki = new InMemoryWikiService(new WikiMarkdigPipeline());
		var created = await wiki.CreateAsync("Public Page", "# public", "#1");
		var controller = MakeController(wiki, authenticated: false);

		var result = await controller.GetPage(created.AsT0.Slug);

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
}
