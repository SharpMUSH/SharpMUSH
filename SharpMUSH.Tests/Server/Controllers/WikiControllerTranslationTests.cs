using SharpMUSH.Library.API;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Server.Controllers;
using System.Reflection;

using static SharpMUSH.Tests.Server.Controllers.WikiControllerTestHarness;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Unit tests for the translation CRUD endpoints. The 409 cases carry the most weight: a revision
/// conflict must never be answered with a 400 (which tells the client to fix its body) and must never
/// be retried, because a retry re-applies the loser's stale markdown over the winner's.
/// </summary>
public class WikiControllerTranslationTests
{
	private static WikiTranslationSummaryDto OkTranslation(IActionResult result) =>
		(WikiTranslationSummaryDto)((OkObjectResult)result).Value!;

	private static AuthorizeAttribute? ActionAuthorize(string name) =>
		typeof(WikiTranslationsController).GetMethod(name, BindingFlags.Public | BindingFlags.Instance)!
			.GetCustomAttribute<AuthorizeAttribute>();

	[Test]
	public async Task PutTranslation_CreatesTheTranslationForAnEditor()
	{
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en");

		var result = await wiki.Translations.PutTranslation(
			"dragons", "fr",
			new UpsertTranslationRequest(
				"Dragons (fr)", "corps fr", "première", Published: true, ExpectedRevisionNumber: null),
			ns: "main", category: "general");

		var dto = OkTranslation(result);
		await Assert.That(dto.Locale).IsEqualTo("fr");
		await Assert.That(dto.Title).IsEqualTo("Dragons (fr)");
		await Assert.That(dto.RevisionNumber).IsEqualTo(1);
	}

	[Test]
	public async Task PutTranslation_RejectsShadowingTheSourceLocaleWithBadRequest()
	{
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en");

		var result = await wiki.Translations.PutTranslation(
			"dragons", "en",
			new UpsertTranslationRequest("T", "m", null, true, null),
			ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<BadRequestObjectResult>();
	}

	[Test]
	public async Task PutTranslation_RejectsAMalformedLocaleWithBadRequest()
	{
		// The write boundary is where a bad locale IS an error. Only reads treat one as absent.
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en");

		var result = await wiki.Translations.PutTranslation(
			"dragons", "not a locale",
			new UpsertTranslationRequest("T", "m", null, true, null),
			ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<BadRequestObjectResult>();
	}

	[Test]
	public async Task PutTranslation_OnAProtectedPageRequiresWikiAdmin()
	{
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.SetProtectionAsync(page.Id, isProtected: true);

		var result = await wiki.Translations.PutTranslation(
			"dragons", "fr",
			new UpsertTranslationRequest("T", "m", null, true, null),
			ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<ForbidResult>()
			.Because("a translation write is gated on the source page's IsProtected, same as a page edit");
	}

	[Test]
	public async Task PutTranslation_OnAProtectedPageSucceedsForWikiAdmin()
	{
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit, PortalPermission.WikiAdmin);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.SetProtectionAsync(page.Id, isProtected: true);

		var result = await wiki.Translations.PutTranslation(
			"dragons", "fr",
			new UpsertTranslationRequest("T", "m", null, true, null),
			ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task PutTranslation_OnAMissingPageIs404()
	{
		var (wiki, _) = BuildWithClaims(PortalPermission.WikiEdit);

		var result = await wiki.Translations.PutTranslation(
			"ghost", "fr",
			new UpsertTranslationRequest("T", "m", null, true, null),
			ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}

	[Test]
	public async Task PutTranslation_WithoutACharacterIdentityIsUnauthorized()
	{
		var (wiki, storage) = BuildAnonymous();
		await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en");

		var result = await wiki.Translations.PutTranslation(
			"dragons", "fr",
			new UpsertTranslationRequest("T", "m", null, true, null),
			ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<UnauthorizedObjectResult>()
			.Because("an edit that cannot be attributed to a character must not be written");
	}

	[Test]
	public async Task PutTranslation_ReturnsConflictOnAStaleExpectedRevision()
	{
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);
		await storage.UpsertTranslationAsync(page.Id, "fr", "v2", "corps v2", "#2", null, true, expectedRevisionNumber: 1);

		var result = await wiki.Translations.PutTranslation(
			"dragons", "fr",
			new UpsertTranslationRequest("perdu", "corps perdu", null, true, ExpectedRevisionNumber: 1),
			ns: "main", category: "general");

		await Assert.That(result)
			.IsTypeOf<ConflictObjectResult>()
			.Because("the request was well-formed; the client's correct response is to reload, not to fix its body");
		var stored = (await storage.GetTranslationAsync(page.Id, "fr")).Expect<WikiTranslation>();
		await Assert.That(stored.MarkdownSource)
			.IsEqualTo("corps v2")
			.Because("the endpoint must never retry a conflict — that re-applies the loser's stale markdown");
		await Assert.That(stored.RevisionNumber)
			.IsEqualTo(2)
			.Because("a rejected write must not consume a revision number either");
	}

	[Test]
	public async Task PutTranslation_ReturnsConflictWhenCreateOnlyHitsAnExistingTranslation()
	{
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

		var result = await wiki.Translations.PutTranslation(
			"dragons", "fr",
			new UpsertTranslationRequest("écrasé", "corps écrasé", null, true, ExpectedRevisionNumber: null),
			ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<ConflictObjectResult>();
		var stored = (await storage.GetTranslationAsync(page.Id, "fr")).Expect<WikiTranslation>();
		await Assert.That(stored.MarkdownSource)
			.IsEqualTo("corps v1")
			.Because("a create-only request that lost must not overwrite the row that already exists");
	}

	[Test]
	public async Task PutTranslation_ReturnsConflictWhenTheTranslationWasDeletedMidEdit()
	{
		// The third lost-write shape, and the one that used to fall through the phrase match and answer 400:
		// the editor loaded revision 1, somebody deleted the locale, and the save arrives with an expected
		// revision for a row that is gone. It is a race, not a malformed body, so it is a 409.
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);
		await storage.DeleteTranslationAsync(page.Id, "fr", "#3");

		var result = await wiki.Translations.PutTranslation(
			"dragons", "fr",
			new UpsertTranslationRequest("orphelin", "corps orphelin", null, true, ExpectedRevisionNumber: 1),
			ns: "main", category: "general");

		await Assert.That(result)
			.IsTypeOf<ConflictObjectResult>()
			.Because("a translation deleted mid-edit is a lost write; the client reloads rather than fixing its body");
		await Assert.That((await storage.GetTranslationAsync(page.Id, "fr")).Value).IsTypeOf<NotFound>()
			.Because("a 409 that also re-created the row would resurrect a deliberately deleted translation");
	}

	[Test]
	public async Task PutTranslation_UpdatesWithTheCurrentRevisionAndBumpsIt()
	{
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

		var result = await wiki.Translations.PutTranslation(
			"dragons", "fr",
			new UpsertTranslationRequest("v2", "corps v2", "suite", true, ExpectedRevisionNumber: 1),
			ns: "main", category: "general");

		await Assert.That(OkTranslation(result).RevisionNumber).IsEqualTo(2);
		var stored = (await storage.GetTranslationAsync(page.Id, "fr")).Expect<WikiTranslation>();
		await Assert.That(stored.MarkdownSource).IsEqualTo("corps v2");
	}

	[Test]
	public async Task CreatePage_StampsTheConfiguredDefaultAsTheSourceLocale()
	{
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit);

		await wiki.Pages.CreatePage(
			new CreatePageRequest("Dragons", "en body", Namespace: "main", Category: "general"));

		var created = (await storage.GetBySlugAsync("dragons", "general", WikiNamespace.Main)).Expect<WikiPage>();
		await Assert.That(created.SourceLocale)
			.IsEqualTo("en")
			.Because("SourceLocale is materialised at creation, not re-derived on every later read");
	}

	[Test]
	public async Task GetTranslations_HidesDraftsFromAnonymousReaders()
	{
		var (wiki, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, published: true, expectedRevisionNumber: null);
		await storage.UpsertTranslationAsync(page.Id, "de", "T", "m", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await wiki.Translations.GetTranslations("dragons", ns: "main", category: "general");

		var dtos = (IEnumerable<WikiTranslationSummaryDto>)((OkObjectResult)result).Value!;
		await Assert.That(dtos.Select(d => d.Locale)).IsEquivalentTo(new[] { "fr" });
	}

	[Test]
	public async Task GetTranslations_ShowsDraftsToAnEditor()
	{
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, published: true, expectedRevisionNumber: null);
		await storage.UpsertTranslationAsync(page.Id, "de", "T", "m", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await wiki.Translations.GetTranslations("dragons", ns: "main", category: "general");

		var dtos = (IEnumerable<WikiTranslationSummaryDto>)((OkObjectResult)result).Value!;
		await Assert.That(dtos.Select(d => d.Locale).Order()).IsEquivalentTo(new[] { "de", "fr" });
	}

	[Test]
	public async Task GetTranslations_OnAnUnpublishedPageIs404ForAnonymousReaders()
	{
		var (wiki, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Secret", "s", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.SetMetadataAsync(page.Id, "general", [], published: false);

		var result = await wiki.Translations.GetTranslations("secret", ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}

	[Test]
	public async Task DeleteTranslation_RemovesOneLocaleAndReturns204()
	{
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);
		await storage.UpsertTranslationAsync(page.Id, "de", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		var result = await wiki.Translations.DeleteTranslation("dragons", "fr", ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<NoContentResult>();
		await Assert.That((await storage.GetTranslationAsync(page.Id, "fr")).Value).IsTypeOf<NotFound>();
		await Assert.That((await storage.GetTranslationAsync(page.Id, "de")).Value).IsTypeOf<WikiTranslation>()
			.Because("deleting one locale must leave every other translation alone");
		await Assert.That((await storage.GetBySlugAsync("dragons", "general", WikiNamespace.Main)).Value).IsTypeOf<WikiPage>()
			.Because("deleting a translation is an edit, not a page deletion");
	}

	[Test]
	public async Task DeleteTranslation_OnAProtectedPageRequiresWikiAdmin()
	{
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);
		await storage.SetProtectionAsync(page.Id, isProtected: true);

		var result = await wiki.Translations.DeleteTranslation("dragons", "fr", ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<ForbidResult>();
		await Assert.That((await storage.GetTranslationAsync(page.Id, "fr")).Value).IsTypeOf<WikiTranslation>();
	}

	[Test]
	public async Task DeleteTranslation_OnAMissingLocaleIs404()
	{
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en");

		var result = await wiki.Translations.DeleteTranslation("dragons", "fr", ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}

	[Test]
	public async Task GetRevisions_WithLangReturnsThatLocaleStream()
	{
		var (wiki, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Dragons", "v1", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.UpdateAsync(page.Id, "v2", "#1");
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "fr1", "#2", null, true, expectedRevisionNumber: null);
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "fr2", "#2", null, true, expectedRevisionNumber: 1);

		var french = await wiki.Revisions.GetRevisions("dragons", 0, 20, "main", "general", lang: "fr");
		var source = await wiki.Revisions.GetRevisions("dragons", 0, 20, "main", "general", lang: null);

		var frenchDtos = ((IEnumerable<WikiRevisionDto>)((OkObjectResult)french).Value!).ToList();
		var sourceDtos = ((IEnumerable<WikiRevisionDto>)((OkObjectResult)source).Value!).ToList();
		await Assert.That(frenchDtos.Count).IsEqualTo(2);
		await Assert.That(frenchDtos.Select(d => d.MarkdownSource)).Contains("fr2");
		await Assert.That(sourceDtos.Count)
			.IsEqualTo(2)
			.Because("omitting lang must keep returning the source stream the history page already shows");
		await Assert.That(sourceDtos.Select(d => d.MarkdownSource))
			.IsEquivalentTo(new[] { "v2", "v1" })
			.Because("the source stream must never mix in translation revisions, which restart numbering at 1");
	}

	[Test]
	public async Task GetRevisions_NamingTheSourceLocaleReturnsTheSourceStream()
	{
		var (wiki, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Dragons", "v1", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "fr1", "#2", null, true, expectedRevisionNumber: null);

		var result = await wiki.Revisions.GetRevisions("dragons", 0, 20, "main", "general", lang: "en");

		var dtos = ((IEnumerable<WikiRevisionDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(dtos.Select(d => d.MarkdownSource)).IsEquivalentTo(new[] { "v1" });
	}

	[Test]
	public async Task GetRevisions_ForADraftTranslationFallsBackToTheSourceStreamForAReader()
	{
		// The reader cannot see the draft, so LocalizeAsync resolves to the source and the history they get
		// is the source's. Serving the draft's revision stream would leak its prose through the diff view.
		var (wiki, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Dragons", "v1", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "corps brouillon", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await wiki.Revisions.GetRevisions("dragons", 0, 20, "main", "general", lang: "fr");

		var dtos = ((IEnumerable<WikiRevisionDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(dtos.Select(d => d.MarkdownSource)).IsEquivalentTo(new[] { "v1" });
	}

	[Test]
	public async Task GetRevision_WithLangReturnsThatLocalesRevisionNotTheSources()
	{
		// The list route returns the fr stream, whose numbering restarts at 1. Before lang existed here,
		// GET /revisions/1?lang=fr answered with the English revision 1 — the history page would then show
		// French entries and diff English bodies, which reads as a legitimate rename rather than a bug.
		var (wiki, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Dragons", "en v1", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "corps fr", "#2", null, true, expectedRevisionNumber: null);

		var french = await wiki.Revisions.GetRevision("dragons", 1, "main", "general", lang: "fr");
		var source = await wiki.Revisions.GetRevision("dragons", 1, "main", "general", lang: null);

		var frenchDto = (WikiRevisionDto)((OkObjectResult)french).Value!;
		var sourceDto = (WikiRevisionDto)((OkObjectResult)source).Value!;
		await Assert.That(frenchDto.MarkdownSource).IsEqualTo("corps fr");
		await Assert.That(sourceDto.MarkdownSource)
			.IsEqualTo("en v1")
			.Because("omitting lang must keep serving the source stream the rollback UI already reads");
	}

	[Test]
	public async Task GetRevision_ForADraftTranslationServesTheSourceStreamToAReader()
	{
		// Same rule as GetRevisions: the reader cannot see the draft, so the stream resolves to the source.
		// Serving the draft's revision here would leak its prose one number at a time.
		var (wiki, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Dragons", "en v1", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "corps brouillon", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await wiki.Revisions.GetRevision("dragons", 1, "main", "general", lang: "fr");

		var dto = (WikiRevisionDto)((OkObjectResult)result).Value!;
		await Assert.That(dto.MarkdownSource).IsEqualTo("en v1");
	}

	[Test]
	public async Task GetRevision_IsNotFoundWhenTheRequestedLocaleStreamLacksThatNumber()
	{
		// The fallback that must NOT happen: fr has one revision, en has two, and asking fr for 2 must be
		// a 404 rather than quietly handing back the English revision 2.
		var (wiki, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en v1", "#1", WikiNamespace.Main, "general", "en")).Expect<WikiPage>();
		await storage.UpdateAsync(page.Id, "en v2", "#1");
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "corps fr", "#2", null, true, expectedRevisionNumber: null);

		var result = await wiki.Revisions.GetRevision("dragons", 2, "main", "general", lang: "fr");

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}

	[Test]
	public async Task TranslationWriteEndpointsAreGatedOnWikiEdit()
	{
		// Every test above constructs the controller directly, which bypasses the authorization filter
		// entirely — so a dropped [Authorize] would leave translation writes anonymously reachable with
		// the whole suite still green. Reflection is the only thing that notices.
		await Assert.That(ActionAuthorize(nameof(WikiTranslationsController.PutTranslation))?.Policy)
			.IsEqualTo(PortalPermission.WikiEdit);
		await Assert.That(ActionAuthorize(nameof(WikiTranslationsController.DeleteTranslation))?.Policy)
			.IsEqualTo(PortalPermission.WikiEdit);
	}

	[Test]
	public async Task TranslationReadEndpointIsNotGated()
	{
		await Assert.That(ActionAuthorize(nameof(WikiTranslationsController.GetTranslations)))
			.IsNull()
			.Because("anonymous readers need the visible-locale list to render the language chips");
	}
}
