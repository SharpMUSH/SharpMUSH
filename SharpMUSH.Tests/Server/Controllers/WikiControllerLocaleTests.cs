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
/// Unit tests for <c>?lang=</c> on the reader-facing wiki routes. The draft cases matter most: the
/// controller decides <c>includeDrafts</c>, so a mistake here leaks unfinished translations to the
/// public even though <see cref="WikiLocalizationService"/> itself is correct.
/// </summary>
public class WikiControllerLocaleTests
{
	/// <summary>
	/// Builds a controller over a real <see cref="WikiLocalizationService"/> against the same storage
	/// instance. A substitute would return nulls and quietly turn every localization assertion green.
	/// </summary>
	private static (WikiController Controller, InMemoryWikiService Storage) Build(
		bool authenticated, params string[] scopes)
	{
		var storage = new InMemoryWikiService(new WikiMarkdigPipeline());
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create());
		var localization = new WikiLocalizationService(
			storage, new WikiLocaleResolver(monitor), NullLogger<WikiLocalizationService>.Instance);

		var controller = new WikiController(
			storage,
			localization,
			Substitute.For<IPrerenderCacheService>(),
			NullLogger<WikiController>.Instance);

		// An identity without an authentication type reports IsAuthenticated == false.
		var identity = authenticated
			? new ClaimsIdentity(
				new List<Claim> { new(GameHub.CharacterDbrefClaim, "#42") }
					.Concat(scopes.Select(s => new Claim(PortalPermission.ClaimType, s))),
				"test")
			: new ClaimsIdentity();

		controller.ControllerContext = new ControllerContext
		{
			HttpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(identity)
			}
		};

		return (controller, storage);
	}

	private static (WikiController Controller, InMemoryWikiService Storage) BuildAnonymous() =>
		Build(authenticated: false);

	private static (WikiController Controller, InMemoryWikiService Storage) BuildWithClaims(params string[] scopes) =>
		Build(authenticated: true, scopes);

	private static WikiController.WikiPageDto OkDto(IActionResult result) =>
		(WikiController.WikiPageDto)((OkObjectResult)result).Value!;

	[Test]
	public async Task GetPage_WithNoLangParameter_ServesTheSourceLocaleWithoutABanner()
	{
		var (controller, storage) = BuildAnonymous();
		await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en");

		var result = await controller.GetPage("main", "general", "dragons", lang: null);

		var dto = OkDto(result);
		await Assert.That(dto.Locale).IsEqualTo("en");
		await Assert.That(dto.RequestedLocale).IsEqualTo("en");
		await Assert.That(dto.IsFallback).IsFalse();
		await Assert.That(dto.MarkdownSource).IsEqualTo("en body");
	}

	[Test]
	public async Task GetPage_WithLangServesAPublishedTranslation()
	{
		var (controller, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "corps fr", "#2", null, published: true, expectedRevisionNumber: null);

		var result = await controller.GetPage("main", "general", "dragons", lang: "fr");

		var dto = OkDto(result);
		await Assert.That(dto.Title).IsEqualTo("Dragons (fr)");
		await Assert.That(dto.MarkdownSource).IsEqualTo("corps fr");
		await Assert.That(dto.Locale).IsEqualTo("fr");
		await Assert.That(dto.IsFallback).IsFalse();
		await Assert.That(dto.AvailableLocales.Order()).IsEquivalentTo(new[] { "en", "fr" });
	}

	[Test]
	public async Task GetPage_DraftTranslationDoesNotLeakToAnAnonymousReader()
	{
		var (controller, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "Brouillon", "corps brouillon", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await controller.GetPage("main", "general", "dragons", lang: "fr");

		var dto = OkDto(result);
		await Assert.That(dto.MarkdownSource).DoesNotContain("brouillon");
		await Assert.That(dto.Title).IsEqualTo("Dragons");
		await Assert.That(dto.Locale).IsEqualTo("en");
		await Assert.That(dto.IsFallback).IsTrue();
		await Assert.That(dto.AvailableLocales)
			.IsEquivalentTo(new[] { "en" })
			.Because("advertising a language the reader cannot see would be a dead chip and an hreflang lie");
	}

	[Test]
	public async Task GetPage_DraftTranslationIsVisibleToAnEditor()
	{
		var (controller, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "Brouillon", "corps brouillon", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await controller.GetPage("main", "general", "dragons", lang: "fr");

		var dto = OkDto(result);
		await Assert.That(dto.MarkdownSource).IsEqualTo("corps brouillon");
		await Assert.That(dto.Locale).IsEqualTo("fr");
		await Assert.That(dto.Published).IsFalse();
	}

	[Test]
	public async Task GetPage_DraftTranslationIsVisibleToAReaderWhoHoldsWikiRead()
	{
		// wiki.read is the draft-*page* scope; it also carries draft translations, because someone who may
		// already read every unpublished page gains nothing from being denied their translations. What must
		// not happen is the reverse: a plain reader with neither scope seeing one. That is the case above;
		// this one pins that wiki.read is deliberately included rather than accidentally.
		var (controller, storage) = BuildWithClaims(PortalPermission.WikiRead);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "Brouillon", "corps brouillon", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await controller.GetPage("main", "general", "dragons", lang: "fr");

		await Assert.That(OkDto(result).Locale).IsEqualTo("fr");
	}

	[Test]
	public async Task GetPage_DraftTranslationDoesNotLeakToAnAuthenticatedReaderWithNoWikiScopes()
	{
		var (controller, storage) = BuildWithClaims();
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "Brouillon", "corps brouillon", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await controller.GetPage("main", "general", "dragons", lang: "fr");

		var dto = OkDto(result);
		await Assert.That(dto.Locale)
			.IsEqualTo("en")
			.Because("being logged in is not permission to preview somebody else's unfinished translation");
		await Assert.That(dto.MarkdownSource).DoesNotContain("brouillon");
	}

	[Test]
	[Arguments("not a locale")]
	[Arguments("")]
	[Arguments("zz-ZZ")]
	public async Task GetPage_MalformedLangIsNeverA400(string lang)
	{
		var (controller, storage) = BuildAnonymous();
		await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en");

		var result = await controller.GetPage("main", "general", "dragons", lang);

		var dto = OkDto(result);
		await Assert.That(dto.Locale)
			.IsEqualTo("en")
			.Because("a malformed lang tag is treated as absent, never rejected");
	}

	[Test]
	public async Task GetPage_UnpublishedPageStillReturns404ForAnonymousReaders()
	{
		var (controller, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Secret", "hidden", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.SetMetadataAsync(page.Id, "general", [], published: false);

		var result = await controller.GetPage("main", "general", "secret", lang: "fr");

		await Assert.That(result).IsTypeOf<NotFoundResult>()
			.Because("localization must not weaken the existing page-level visibility gate");
	}

	[Test]
	public async Task GetCharacterPage_WithLangServesTheTranslation()
	{
		var (controller, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync(
			"Mannaz", "en bio", "#1", WikiNamespace.Character, WikiHelpers.DefaultCategory, "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "Mannaz (fr)", "bio fr", "#2", null, published: true, expectedRevisionNumber: null);

		var result = await controller.GetCharacterPage("mannaz", lang: "fr");

		var dto = OkDto(result);
		await Assert.That(dto.Locale).IsEqualTo("fr");
		await Assert.That(dto.MarkdownSource).IsEqualTo("bio fr");
	}
}
