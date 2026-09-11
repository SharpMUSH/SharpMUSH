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
/// Unit tests for the page-protection enforcement on <see cref="WikiController.UpdatePage"/>:
/// protected pages must reject edits from callers lacking the wiki.admin (moderation) scope with
/// 403, while wiki.admin holders (and anyone, for unprotected pages) can edit normally.
/// </summary>
public class WikiControllerProtectionTests
{
	// Callers are identified by their granted permission scopes (the protected-page check authorizes
	// on the wiki.admin claim, not on a role name).
	private static WikiController MakeController(InMemoryWikiService wiki, params string[] scopes)
	{
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create());
		var controller = new WikiController(
			wiki,
			new WikiLocalizationService(
				wiki, new WikiLocaleResolver(monitor), NullLogger<WikiLocalizationService>.Instance),
			Substitute.For<IPrerenderCacheService>(),
			NullLogger<WikiController>.Instance);

		var claims = new List<Claim> { new(GameHub.CharacterDbrefClaim, "#42") };
		claims.AddRange(scopes.Select(s => new Claim(PortalPermission.ClaimType, s)));

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
		if (await wiki.CreateAsync("Protected Page", "# original", "#1") is not WikiPage page)
		{
			throw new InvalidOperationException("Seeding the protected page failed.");
		}
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
		var controller = MakeController(wiki);

		var result = await controller.UpdatePage(slug, new WikiController.UpdatePageRequest("# changed", null));

		await Assert.That(result).IsTypeOf<ForbidResult>();

		var page = await Assert.That((await wiki.GetBySlugAsync(slug, "general")).Value).IsTypeOf<WikiPage>();
		await Assert.That(page!.MarkdownSource).IsEqualTo("# original");
	}

	[Test]
	public async Task UpdatePage_ProtectedPage_Wizard_Succeeds()
	{
		var (wiki, slug) = await SeedProtectedPage(isProtected: true);
		var controller = MakeController(wiki, PortalPermission.WikiAdmin);

		var result = await controller.UpdatePage(slug, new WikiController.UpdatePageRequest("# changed", null));

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task UpdatePage_UnprotectedPage_NonWizard_Succeeds()
	{
		var (wiki, slug) = await SeedProtectedPage(isProtected: false);
		var controller = MakeController(wiki);

		var result = await controller.UpdatePage(slug, new WikiController.UpdatePageRequest("# changed", null));

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}
}
