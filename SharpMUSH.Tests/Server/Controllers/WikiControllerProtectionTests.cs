using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;
using System.Security.Claims;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Unit tests for the page-protection enforcement on <see cref="WikiController.UpdatePage"/>:
/// protected pages must reject edits from non-Wizard users with 403, while Wizard
/// users (and anyone, for unprotected pages) can edit normally.
/// </summary>
public class WikiControllerProtectionTests
{
	private static WikiController MakeController(InMemoryWikiService wiki, params string[] roles)
	{
		var controller = new WikiController(
			wiki,
			Substitute.For<IPrerenderCacheService>(),
			NullLogger<WikiController>.Instance);

		var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "#42") };
		claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

		controller.ControllerContext = new ControllerContext
		{
			HttpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
			}
		};
		return controller;
	}

	private static async Task<(InMemoryWikiService Wiki, string Slug)> SeedProtectedPage(bool isProtected)
	{
		var wiki = new InMemoryWikiService(new WikiMarkdigPipeline());
		var created = await wiki.CreateAsync("Protected Page", "# original", "#1");
		var page = created.AsT0;
		if (isProtected)
		{
			await wiki.SetProtectionAsync(page.Id, true);
		}
		return (wiki, page.Slug);
	}

	[Test]
	public async Task UpdatePage_ProtectedPage_NonWizard_Returns403()
	{
		var (wiki, slug) = await SeedProtectedPage(isProtected: true);
		var controller = MakeController(wiki, nameof(PortalRole.Player));

		var result = await controller.UpdatePage(slug, new WikiController.UpdatePageRequest("# changed", null));

		await Assert.That(result).IsTypeOf<ForbidResult>();

		// Content must be untouched.
		var page = await wiki.GetBySlugAsync(slug);
		await Assert.That(page.AsT0.MarkdownSource).IsEqualTo("# original");
	}

	[Test]
	public async Task UpdatePage_ProtectedPage_Wizard_Succeeds()
	{
		var (wiki, slug) = await SeedProtectedPage(isProtected: true);
		var controller = MakeController(wiki, nameof(PortalRole.Wizard));

		var result = await controller.UpdatePage(slug, new WikiController.UpdatePageRequest("# changed", null));

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task UpdatePage_UnprotectedPage_NonWizard_Succeeds()
	{
		var (wiki, slug) = await SeedProtectedPage(isProtected: false);
		var controller = MakeController(wiki, nameof(PortalRole.Player));

		var result = await controller.UpdatePage(slug, new WikiController.UpdatePageRequest("# changed", null));

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}
}
