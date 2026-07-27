using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Controllers;

using static SharpMUSH.Tests.Server.Controllers.WikiControllerTestHarness;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Unit tests for <c>?lang=</c> on the reader-facing wiki routes. The draft cases matter most: the
/// controller decides <c>includeDrafts</c>, so a mistake here leaks unfinished translations to the
/// public even though <see cref="WikiLocalizationService"/> itself is correct.
/// </summary>
public class WikiControllerLocaleTests
{
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

	[Test]
	public async Task GetRecentChanges_WithLangReturnsLocalizedTitlesOneRowPerPage()
	{
		var (controller, storage) = BuildAnonymous();
		var alpha = (await storage.CreateAsync("Alpha", "a", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.CreateAsync("Beta", "b", "#1", WikiNamespace.Main, "general", "en");
		await storage.UpsertTranslationAsync(alpha.Id, "fr", "Alpha (fr)", "a-fr", "#2", null, published: true, expectedRevisionNumber: null);

		var result = await controller.GetRecentChanges(count: 20, lang: "fr");

		var dtos = ((IEnumerable<WikiController.WikiPageDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(dtos.Count)
			.IsEqualTo(2)
			.Because("a localized listing must not return N rows per page");
		await Assert.That(dtos.Single(d => d.Slug == "alpha").Title).IsEqualTo("Alpha (fr)");
		await Assert.That(dtos.Single(d => d.Slug == "alpha").IsFallback).IsFalse();
		await Assert.That(dtos.Single(d => d.Slug == "beta").Title).IsEqualTo("Beta");
		await Assert.That(dtos.Single(d => d.Slug == "beta").IsFallback).IsTrue();
	}

	[Test]
	public async Task ListAllPages_WithLangKeepsTheTotalCountHeaderSemantics()
	{
		var (controller, storage) = BuildWithClaims(PortalPermission.WikiRead);
		await storage.CreateAsync("Alpha", "a", "#1", WikiNamespace.Main, "general", "en");
		await storage.CreateAsync("Beta", "b", "#1", WikiNamespace.Main, "general", "en");

		var result = await controller.ListAllPages(skip: 0, take: 50, ns: null, lang: "fr");

		var dtos = ((IEnumerable<WikiController.WikiPageDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(dtos.Count).IsEqualTo(2);
		await Assert.That(controller.Response.Headers["X-Total-Count"].ToString()).IsEqualTo("2");
	}

	[Test]
	public async Task ListNamespacePages_DraftTranslationDoesNotChangeAListedTitle()
	{
		var (controller, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Help Intro", "h", "#1", WikiNamespace.Help, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "Intro (brouillon)", "h-fr", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await controller.ListNamespacePages("help", skip: 0, take: 50, lang: "fr");

		var dtos = ((IEnumerable<WikiController.WikiPageDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(dtos.Single().Title)
			.IsEqualTo("Help Intro")
			.Because("an unpublished translation must not surface its title in a public listing");
	}

	[Test]
	public async Task ListCategoryPages_WithLangReturnsLocalizedTitles()
	{
		var (controller, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Alpha", "a", "#1", WikiNamespace.Main, "lore", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "Alpha (fr)", "a-fr", "#2", null, published: true, expectedRevisionNumber: null);

		var result = await controller.ListCategoryPages("lore", skip: 0, take: 50, lang: "fr");

		var dtos = ((IEnumerable<WikiController.WikiPageDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(dtos.Single().Title).IsEqualTo("Alpha (fr)");
	}

	[Test]
	public async Task ListTagPages_WithLangReturnsLocalizedTitles()
	{
		var (controller, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Alpha", "a", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.SetMetadataAsync(page.Id, "general", ["dragons"], published: true);
		await storage.UpsertTranslationAsync(page.Id, "fr", "Alpha (fr)", "a-fr", "#2", null, published: true, expectedRevisionNumber: null);

		var result = await controller.ListTagPages("dragons", skip: 0, take: 50, lang: "fr");

		var dtos = ((IEnumerable<WikiController.WikiPageDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(dtos.Single().Title).IsEqualTo("Alpha (fr)");
	}

	[Test]
	public async Task ListNamespacePages_StillHidesUnpublishedPagesFromAnonymousReaders()
	{
		// LocalizedListAsync must keep calling FilterVisible first; a localized listing that forgot the
		// page-level gate would leak drafts while every locale assertion stayed green.
		var (controller, storage) = BuildAnonymous();
		var draft = (await storage.CreateAsync("Secret", "s", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.SetMetadataAsync(draft.Id, "general", [], published: false);
		await storage.CreateAsync("Public", "p", "#1", WikiNamespace.Main, "general", "en");

		var result = await controller.ListNamespacePages("main", skip: 0, take: 50, lang: "fr");

		var dtos = ((IEnumerable<WikiController.WikiPageDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(dtos.Select(d => d.Slug)).IsEquivalentTo(new[] { "public" });
	}
}
