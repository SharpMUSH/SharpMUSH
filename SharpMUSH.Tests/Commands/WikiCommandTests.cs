using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Integration tests for the @wiki command: page creation, viewing, listing,
/// search, history, append, and the wizard-only protection rules. Pages are
/// stored through the same IWikiService the web portal uses.
/// </summary>
[NotInParallel] // integration test over shared services + the session-shared Notify substitute
public class WikiCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task ExpectNotify(SharpMUSH.Library.Models.DBRef player, string contains)
	{
		// Sender carries PennMUSH "orator" semantics: command feedback is spoken by
		// the executor of the command — here always the notified player themselves.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(player), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains(contains)) ||
				(msg.IsT1 && msg.AsT1.Contains(contains))), TestHelpers.MatchingObject(player), INotifyService.NotificationType.Announce);
	}

	/// <summary>The negative of <see cref="ExpectNotify"/>: this player was never told <paramref name="contains"/>.</summary>
	private async Task ExpectNoNotify(SharpMUSH.Library.Models.DBRef player, string contains)
	{
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(player), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains(contains)) ||
				(msg.IsT1 && msg.AsT1.Contains(contains))), TestHelpers.MatchingObject(player), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask WikiCreate_ThenView_ShowsRenderedPage()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiCreator");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/create Cmd Test Page=# Cmd Heading\n\nSome **bold** body."));
		await ExpectNotify(player.DbRef, "WIKI: Created page 'Cmd Test Page'");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki cmd test page"));
		await ExpectNotify(player.DbRef, "Wiki: Cmd Test Page [main]");
	}

	[Test]
	public async ValueTask WikiView_UnknownPage_NotifiesNoSuchPage()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiViewer");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki absolutely_missing_page"));
		await ExpectNotify(player.DbRef, "WIKI: No such page");
	}

	[Test]
	public async ValueTask WikiList_ShowsSeededHelpNamespacePage()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiLister");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/list help"));
		await ExpectNotify(player.DbRef, "help:general:markdown_guide");
	}

	[Test]
	public async ValueTask WikiSearch_FindsPageByContent()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiSearcher");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/create Search Fodder=The xyzzy-marker phrase lives here."));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/search xyzzy-marker"));
		// The slug alone also appears in the create confirmation; the search header ("N page(s)
		// matching '<needle>'") is unique to the search reply and confirms the page was found.
		await ExpectNotify(player.DbRef, "1 page(s) matching 'xyzzy-marker'");
	}

	[Test]
	public async ValueTask WikiAppend_AddsRevision_HistoryShowsIt()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiAppender");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/create Append Target=First paragraph."));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/append append_target=Second paragraph."));
		await ExpectNotify(player.DbRef, "now rev 2");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/history append_target"));
		await ExpectNotify(player.DbRef, "Revision history for Append Target");
	}

	[Test]
	public async ValueTask WikiProtect_NonWizard_IsDenied()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiMortal");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/create Mortal Page=content"));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/protect mortal_page"));
		await ExpectNotify(player.DbRef, "wizard-only");
	}

	[Test]
	public async ValueTask WikiProtect_AsGod_LocksPageAgainstMortals()
	{
		var god = WebAppFactoryArg.ExecutorDBRef;
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiLocked");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/create Locked Page=original content"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@wiki/protect locked_page"));
		await ExpectNotify(god, "now protected");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/edit locked_page=replacement content"));
		await ExpectNotify(player.DbRef, "protected. Only wizards may edit it");
	}

	[Test]
	public async ValueTask WikiRollback_RestoresEarlierRevision()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiRoller");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/create Rollback Target=original body"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/edit rollback_target=changed body"));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/rollback rollback_target=1"));
		await ExpectNotify(player.DbRef, "Restored 'Rollback Target' to r1 (now rev 3)");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki rollback_target"));
		await ExpectNotify(player.DbRef, "original body");
	}

	[Test]
	public async ValueTask WikiRollback_UnknownRevision_Notifies()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiRollMiss");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/create Rollback Missing=body"));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/rollback rollback_missing=42"));
		await ExpectNotify(player.DbRef, "has no revision r42");
	}

	[Test]
	public async ValueTask HelpAtWiki_LoadsSharpwikiHelpfile()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiHelpReader");

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("help @wiki"));

		// help output is sent by the help command's own notify path (sender may differ),
		// so only the recipient and content are asserted here.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("in-game interface to the shared wiki")) ||
				(msg.IsT1 && msg.AsT1.Contains("in-game interface to the shared wiki"))),
				Arg.Any<SharpMUSH.Library.DiscriminatedUnions.AnySharpObject?>(),
				INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpWikiFunction_LoadsFunctionEntry()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiFnHelp");

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("help wiki()"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Returns information about a wiki page")) ||
				(msg.IsT1 && msg.AsT1.Contains("Returns information about a wiki page"))),
				Arg.Any<SharpMUSH.Library.DiscriminatedUnions.AnySharpObject?>(),
				INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask WikiTag_SetsNormalizedTags()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiTagger");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/create Tagged Page=content"));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/tag tagged_page=Magic FIRE magic"));
		await ExpectNotify(player.DbRef, "set to: fire, magic");
	}

	private IWikiService WikiService => WebAppFactoryArg.Services.GetRequiredService<IWikiService>();

	/// <summary>
	/// Creates a page through the service and adds one published translation, returning the slug.
	/// </summary>
	/// <remarks>
	/// <c>expectedRevisionNumber: null</c> is create-only, correct for every call here: each is the first
	/// write for its locale, so there is no revision to compare against. A second write to the same locale
	/// would have to pass the number it loaded rather than reuse this helper.
	/// </remarks>
	private async Task<string> SeedTranslatedPageAsync(
		string title, string englishBody, string frenchTitle, string frenchBody, bool published)
	{
		var created = await WikiService.CreateAsync(
			title, englishBody, "#1", WikiNamespace.Main, "general", "en");
		await Assert.That(created.IsT0).IsTrue();
		var page = created.AsT0;

		var translated = await WikiService.UpsertTranslationAsync(
			page.Id, "fr", frenchTitle, frenchBody, "#1", null, published, expectedRevisionNumber: null);
		await Assert.That(translated.IsT0)
			.IsTrue()
			.Because(translated.Match(
				_ => "translation seeded",
				conflict => $"seeding lost a write race: {conflict}",
				error => error.Value));

		return page.Slug;
	}

	[Test]
	public async ValueTask WikiView_ServesTheExecutorsLocaleWhenATranslationExists()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiFrenchReader");
		var slug = await SeedTranslatedPageAsync(
			"Locale Dragons", "en dragon body", "Dragons Localises", "corps du dragon", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@wiki/view {slug}"));

		await ExpectNotify(player.DbRef, "corps du dragon");
	}

	[Test]
	public async ValueTask WikiView_WithSourceSwitchForcesTheSourceLocale()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiSourceReader");
		var slug = await SeedTranslatedPageAsync(
			"Source Dragons", "en source body", "Dragons Sources", "corps source fr", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@wiki/view/source {slug}"));

		// The header carries the title, so asserting on the source title also proves the source row won.
		await ExpectNotify(player.DbRef, "Wiki: Source Dragons");
	}

	[Test]
	public async ValueTask WikiView_SourceSwitchDoesNotCountAsASecondAction()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiSwitchCounter");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/create Switch Count Page=body here"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/view/source switch_count_page"));

		// SOURCE is a modifier like NOEVAL. If it stayed in the action set, this would be "too many
		// switches" and the page would never render.
		await ExpectNotify(player.DbRef, "Wiki: Switch Count Page");
	}

	[Test]
	public async ValueTask WikiView_FallsBackToTheSourceWhenTheLocaleHasNoTranslation()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiGermanReader");

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("@locale de"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/create Untranslated Page=only english body"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/view untranslated_page"));

		await ExpectNotify(player.DbRef, "only english body");
	}

	[Test]
	public async ValueTask WikiView_DraftTranslationDoesNotLeakToAReaderWhoCannotEdit()
	{
		// The page is protected, so a mortal cannot edit it and therefore must not see its draft
		// translation. Without the protection there is no in-game reader who fails the edit gate.
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftReader");
		var slug = await SeedTranslatedPageAsync(
			"Draft Locale Page", "en visible body", "Brouillon", "corps brouillon secret", published: false);

		var page = (await WikiService.GetBySlugAsync(slug, "general", WikiNamespace.Main)).AsT0;
		await WikiService.SetProtectionAsync(page.Id, isProtected: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@wiki/view {slug}"));

		await ExpectNotify(player.DbRef, "en visible body");
		await ExpectNoNotify(player.DbRef, "corps brouillon secret");
	}

	[Test]
	public async ValueTask WikiList_ShowsLocalizedTitles()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiFrenchLister");
		await SeedTranslatedPageAsync(
			"Listed Dragons", "en listed body", "Dragons Listes", "corps liste", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("@wiki/list"));

		await ExpectNotify(player.DbRef, "Dragons Listes");
	}

	/// <remarks>
	/// <c>@wiki/create</c> is the second and last create path in the codebase, and the only one reachable
	/// from in-game. Nothing else asserted that it stamps: dropping the <c>sourceLocale</c> argument left
	/// every unit and integration test green, because an unstamped page still renders — it just resolves
	/// against a blank locale forever, since nothing normalises empty on read.
	/// </remarks>
	[Test]
	public async ValueTask WikiCreate_StampsTheConfiguredSourceLocale()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiStampCreator");
		var localization = WebAppFactoryArg.Services.GetRequiredService<IWikiLocalizationService>();

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/create Stamped At Birth=body of a stamped page"));

		var created = await WikiService.GetBySlugAsync("stamped_at_birth", "general", WikiNamespace.Main);
		await Assert.That(created.IsT0).IsTrue();
		await Assert.That(created.AsT0.SourceLocale)
			.IsEqualTo(localization.DefaultLocale)
			.Because("a page created in-game must be stamped at birth exactly as the API path is; the "
				+ "migration backfill is not a safety net for pages created after it ran");
	}

	[Test]
	public async ValueTask WikiHistory_ShowsTheTranslationsOwnStream()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiHistoryFrench");
		var slug = await SeedTranslatedPageAsync(
			"History Dragons", "en history body", "Dragons Historiques", "corps historique", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@wiki/history {slug}"));

		// The header names the stream, which is the only thing that distinguishes "showed the fr stream"
		// from "showed the source stream" when both happen to hold a single revision 1.
		await ExpectNotify(player.DbRef, "(fr)");
	}

	[Test]
	public async ValueTask WikiHistory_FallsBackToTheSourceStreamWhenTheLocaleHasNoTranslation()
	{
		// Regression: resolving the *requested* locale rather than the *served* one asked the store for a
		// "de" stream that does not exist and printed an empty history, for a page @wiki/view renders
		// perfectly well in English. A read must not fail for locale reasons.
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiHistoryGerman");
		var slug = await SeedTranslatedPageAsync(
			"History Gap Dragons", "en gap body", "Dragons Ecart", "corps ecart", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("@locale de"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@wiki/history {slug}"));

		await ExpectNotify(player.DbRef, "r1");
	}

	[Test]
	public async ValueTask WikiHistory_SourceSwitchShowsTheSourceStreamToATranslatedReader()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiHistorySource");
		var slug = await SeedTranslatedPageAsync(
			"History Source Dragons", "en source-history body", "Dragons Source Hist", "corps source hist",
			published: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@wiki/history/source {slug}"));

		// The source stream carries no marker and the header keeps the source title.
		await ExpectNotify(player.DbRef, "History Source Dragons");
		await ExpectNoNotify(player.DbRef, "Dragons Source Hist");
	}

	/// <summary>Creates a page and unpublishes it through the service, returning it.</summary>
	/// <remarks>
	/// Unpublishing goes through <c>SetMetadataAsync</c> rather than <c>@wiki/unpublish</c> because that
	/// switch is wizard-only and these tests need the draft to exist before a mortal ever runs a command.
	/// </remarks>
	private async Task<WikiPage> SeedUnpublishedPageAsync(string title, string body)
	{
		var created = await WikiService.CreateAsync(title, body, "#1", WikiNamespace.Main, "general", "en");
		await Assert.That(created.IsT0).IsTrue();
		var page = created.AsT0;

		var unpublished = await WikiService.SetMetadataAsync(page.Id, page.Category, page.Tags, published: false);
		await Assert.That(unpublished.IsT0).IsTrue();

		return unpublished.AsT0;
	}

	[Test]
	public async ValueTask WikiSearch_DoesNotDiscloseUnpublishedPagesToAMortal()
	{
		// @wiki/view is gated, but search paged through GetAllPagesAsync — which documents that it returns
		// unpublished pages and leaves filtering to the caller — and filtered on nothing at all, so a draft's
		// title and reference reached any player who guessed a word from its body.
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftSearcher");
		var page = await SeedUnpublishedPageAsync(
			"Mortal Draft Fodder", "The grue-marker phrase lives here.");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/search grue-marker"));

		await ExpectNotify(player.DbRef, "0 page(s) matching 'grue-marker'");
		await ExpectNoNotify(player.DbRef, page.Slug);
		await ExpectNoNotify(player.DbRef, page.Title);
	}

	[Test]
	public async ValueTask WikiSearch_StillFindsUnpublishedPagesForAWizard()
	{
		// Without this, "hide every draft unconditionally" — or a search that returns nothing at all —
		// would satisfy the test above. Unpublishing is wizard-only, so a wizard must still find the draft.
		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftWizard");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {wizard.DbRef}=WIZARD"));
		var page = await SeedUnpublishedPageAsync(
			"Wizard Draft Fodder", "The plover-marker phrase lives here.");

		await Parser.CommandParse(wizard.Handle, ConnectionService,
			MModule.single("@wiki/search plover-marker"));

		await ExpectNotify(wizard.DbRef, "1 page(s) matching 'plover-marker'");
		await ExpectNotify(wizard.DbRef, $"{page.Slug}");
	}

	[Test]
	public async ValueTask WikiList_DoesNotDiscloseUnpublishedPagesToAMortal()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftLister");
		var page = await SeedUnpublishedPageAsync("Mortal Draft Listing", "listing body");

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("@wiki/list"));

		await ExpectNoNotify(player.DbRef, page.Slug);
		await ExpectNotify(player.DbRef, "WIKI: ");
	}

	[Test]
	public async ValueTask WikiRecent_DoesNotDiscloseUnpublishedPagesToAMortal()
	{
		// Freshly written, so it heads the UpdatedAt ordering: without the filter this is the first row.
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftRecent");
		var page = await SeedUnpublishedPageAsync("Mortal Draft Recent", "recent body");

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("@wiki/recent"));

		await ExpectNoNotify(player.DbRef, page.Slug);
		await ExpectNotify(player.DbRef, "Recently edited pages");
	}

	[Test]
	public async ValueTask WikiRecent_StillShowsUnpublishedPagesToAWizard()
	{
		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftRecentWiz");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {wizard.DbRef}=WIZARD"));
		var page = await SeedUnpublishedPageAsync("Wizard Draft Recent", "wizard recent body");

		await Parser.CommandParse(wizard.Handle, ConnectionService, MModule.single("@wiki/recent"));

		await ExpectNotify(wizard.DbRef, page.Slug);
	}
}
