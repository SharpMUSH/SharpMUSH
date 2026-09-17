using SharpMUSH.Library.API;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Wiki;
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
	/// Builds the wiki endpoints for a caller. <paramref name="canReadDrafts"/> grants the wiki.read
	/// scope (any Player+ member by default); when false and <paramref name="callerDbref"/> is set,
	/// the caller is authenticated but only sees drafts they authored. Pass authenticated: false for
	/// an anonymous caller.
	/// </summary>
	private static WikiEndpoints MakeEndpoints(
		InMemoryWikiService wiki, bool authenticated, bool canReadDrafts = true, string callerDbref = "#42") =>
		WikiControllerTestHarness.Build(wiki, authenticated, callerDbref,
			canReadDrafts ? [PortalPermission.WikiRead] : []).Wiki;

	private static async Task<(InMemoryWikiService Wiki, string Slug)> SeedUnpublishedPage()
	{
		var wiki = new InMemoryWikiService(new WikiMarkdigPipeline());
		var page = (await wiki.CreateAsync("Draft Page", "# draft", "#1")).Expect<WikiPage>();
		await wiki.SetMetadataAsync(page.Id, null, [], published: false);
		return (wiki, page.Slug);
	}

	[Test]
	public async Task GetPage_Unpublished_Anonymous_Returns404()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var endpoints = MakeEndpoints(wiki, authenticated: false);

		var result = await endpoints.Pages.GetPage("main", "general", slug);

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}

	[Test]
	public async Task GetPage_Unpublished_Authenticated_Returns200()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var endpoints = MakeEndpoints(wiki, authenticated: true);

		var result = await endpoints.Pages.GetPage("main", "general", slug);

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task GetPage_Published_Anonymous_Returns200()
	{
		var wiki = new InMemoryWikiService(new WikiMarkdigPipeline());
		var created = (await wiki.CreateAsync("Public Page", "# public", "#1")).Expect<WikiPage>();
		var endpoints = MakeEndpoints(wiki, authenticated: false);

		var result = await endpoints.Pages.GetPage("main", "general", created.Slug);

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task GetRecentChanges_Anonymous_ExcludesUnpublished()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var endpoints = MakeEndpoints(wiki, authenticated: false);

		var result = await endpoints.Browse.GetRecentChanges();

		var ok = result as OkObjectResult;
		await Assert.That(ok).IsNotNull();
		var pages = ((IEnumerable<WikiPageDto>)ok!.Value!).ToList();
		await Assert.That(pages.Any(p => p.Slug == slug)).IsFalse();
	}

	[Test]
	public async Task GetRecentChanges_Authenticated_IncludesUnpublished()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var endpoints = MakeEndpoints(wiki, authenticated: true);

		var result = await endpoints.Browse.GetRecentChanges();

		var ok = result as OkObjectResult;
		await Assert.That(ok).IsNotNull();
		var pages = ((IEnumerable<WikiPageDto>)ok!.Value!).ToList();
		await Assert.That(pages.Any(p => p.Slug == slug)).IsTrue();
	}

	[Test]
	public async Task ListNamespacePages_Anonymous_ExcludesUnpublished()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var endpoints = MakeEndpoints(wiki, authenticated: false);

		var result = await endpoints.Browse.ListNamespacePages("main");

		var ok = result as OkObjectResult;
		await Assert.That(ok).IsNotNull();
		var pages = ((IEnumerable<WikiPageDto>)ok!.Value!).ToList();
		await Assert.That(pages.Any(p => p.Slug == slug)).IsFalse();
	}

	[Test]
	public async Task ListAllPages_Anonymous_ExcludesUnpublishedFromRowsAndFromTheTotalHeader()
	{
		// One draft and one published page, so "1" distinguishes the published count both from the
		// drafts-inclusive total (2) and from an empty store. The header used to be withheld entirely
		// because the only count available was the drafts-inclusive one.
		var (wiki, slug) = await SeedUnpublishedPage();
		await wiki.CreateAsync("Public Page", "# public", "#1");
		var endpoints = MakeEndpoints(wiki, authenticated: false);

		var result = await endpoints.Browse.ListAllPages();

		var ok = result as OkObjectResult;
		await Assert.That(ok).IsNotNull();
		var pages = ((IEnumerable<WikiPageDto>)ok!.Value!).ToList();
		await Assert.That(pages.Any(p => p.Slug == slug)).IsFalse();

		var header = endpoints.Browse.Response.Headers["X-Total-Count"].ToString();
		await Assert.That(header)
			.IsEqualTo("1")
			.Because("a total that counted the draft would let an anonymous caller difference it "
				+ "against the one row they received and learn a draft exists");
		await Assert.That(header).IsEqualTo(pages.Count.ToString());
	}

	[Test]
	public async Task ListAllPages_Authenticated_IncludesUnpublishedInRowsAndInTheTotalHeader()
	{
		// The mirror of the test above: hiding drafts from the count unconditionally would satisfy it
		// while blinding the one caller entitled to see them.
		var (wiki, slug) = await SeedUnpublishedPage();
		await wiki.CreateAsync("Public Page", "# public", "#1");
		var endpoints = MakeEndpoints(wiki, authenticated: true);

		var result = await endpoints.Browse.ListAllPages();

		var ok = result as OkObjectResult;
		await Assert.That(ok).IsNotNull();
		var pages = ((IEnumerable<WikiPageDto>)ok!.Value!).ToList();
		await Assert.That(pages.Any(p => p.Slug == slug)).IsTrue();

		var header = endpoints.Browse.Response.Headers["X-Total-Count"].ToString();
		await Assert.That(header).IsEqualTo("2");
	}

	[Test]
	public async Task GetPage_Unpublished_Author_Returns200_EvenWithoutWikiRead()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var endpoints = MakeEndpoints(wiki, authenticated: true, canReadDrafts: false, callerDbref: "#1");

		var result = await endpoints.Pages.GetPage("main", "general", slug);

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task GetPage_Unpublished_AuthenticatedNonAuthorWithoutWikiRead_Returns404()
	{
		var (wiki, slug) = await SeedUnpublishedPage();
		var endpoints = MakeEndpoints(wiki, authenticated: true, canReadDrafts: false, callerDbref: "#99");

		var result = await endpoints.Pages.GetPage("main", "general", slug);

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}
}
