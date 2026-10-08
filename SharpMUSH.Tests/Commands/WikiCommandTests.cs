using SharpMUSH.Tests.Wiki;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Integration tests for the @wiki command: page creation, viewing, listing,
/// search, history, append, and the wizard-only protection rules. Pages are
/// stored through the same IWikiService the web portal uses.
/// </summary>
public class WikiCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// How many times <paramref name="player"/> was told something containing <paramref name="contains"/>
	/// by themselves. Sender carries PennMUSH "orator" semantics: command feedback is spoken by the
	/// executor of the command — here always the notified player themselves.
	/// </summary>
	private int SelfNotifications(SharpMUSH.Library.Models.DBRef player, string contains) =>
		WebAppFactoryArg.Notifications.DeliveriesFor(player).Count(delivery =>
			delivery.Sender == player
			&& delivery.Type == INotifyService.NotificationType.Announce
			&& delivery.Message.Contains(contains, StringComparison.Ordinal));

	/// <summary>
	/// A player holding the <c>approved</c> role, which is where a new game's <c>wiki.create</c> and
	/// <c>wiki.edit</c> come from; a player without it only reads the wiki.
	/// </summary>
	private static async Task<TestIsolationHelpers.TestPlayer> ApprovedPlayerAsync(IServiceProvider services, IMediator mediator,
		IConnectionService connectionService, string namePrefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(services, mediator, connectionService, namePrefix);
		await services.GetRequiredService<IRoleRegistryService>().AssignRoleToObjectAsync(player.DbRef.Number, BuiltInRoles.ApprovedSlug);
		return player;
	}

	private async Task ExpectNotify(SharpMUSH.Library.Models.DBRef player, string contains) =>
		await Assert.That(SelfNotifications(player, contains)).IsEqualTo(1);

	/// <summary>The negative of <see cref="ExpectNotify"/>: this player was never told <paramref name="contains"/>.</summary>
	private async Task ExpectNoNotify(SharpMUSH.Library.Models.DBRef player, string contains) =>
		await Assert.That(SelfNotifications(player, contains)).IsEqualTo(0);

	/// <summary>
	/// As <see cref="ExpectNotify"/>, for output whose sender is not the reader (the help command's own
	/// notify path), so only the recipient and the content are asserted.
	/// </summary>
	private async Task ExpectNotifyFromAnySender(SharpMUSH.Library.Models.DBRef player, string contains) =>
		await Assert.That(WebAppFactoryArg.Notifications.DeliveriesFor(player).Count(delivery =>
			delivery.Type == INotifyService.NotificationType.Announce
			&& delivery.Message.Contains(contains, StringComparison.Ordinal))).IsEqualTo(1);

	[Test]
	public async ValueTask WikiCreate_ThenView_ShowsRenderedPage()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiCreator");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/create Cmd Test Page=# Cmd Heading\n\nSome **bold** body."));
		await ExpectNotify(player.DbRef, "WIKI: Created page 'Cmd Test Page'");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki cmd test page"));
		await ExpectNotify(player.DbRef, "Wiki: Cmd Test Page [main]");
	}

	/// <summary>
	/// A player who is not approved holds neither wiki.create nor wiki.edit, and their own character's
	/// biography is no exception: they may not write it or edit it.
	/// </summary>
	[Test]
	public async ValueTask AnUnapprovedPlayer_CannotWriteOrEditTheWiki_TheirOwnBiographyIncluded()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiUnapproved");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/create Character:{player.Name}=# {player.Name}"));
		await ExpectNotify(player.DbRef, "needs wiki.create");

		// Staff wrote the biography; its own character still may not edit it.
		await WebAppFactoryArg.Services.GetRequiredService<IWikiService>().CreateAsync(player.Name, "body", "#1", WikiNamespace.Character);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/edit Character:{player.Name}=changed"));
		await ExpectNotify(player.DbRef, "needs wiki.edit");
	}

	[Test]
	public async ValueTask WikiView_UnknownPage_NotifiesNoSuchPage()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiViewer");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki absolutely_missing_page"));
		await ExpectNotify(player.DbRef, "WIKI: No such page");
	}

	[Test]
	public async ValueTask WikiList_ShowsSeededHelpNamespacePage()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiLister");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/list help"));
		await ExpectNotify(player.DbRef, "help:markdown_guide");
	}

	/// <summary>
	/// One column, one grammar. Main-namespace pages listed bare ("home") while every other namespace
	/// listed qualified ("help:markdown_guide"), so a reader could not tell from the column which
	/// spelling any given row was in. Every row is now fully qualified.
	/// </summary>
	[Test]
	public async ValueTask WikiList_QualifiesMainNamespaceRowsToo()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiMainLister");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/create Qualified Row Page=Body of the qualified row page."));
		var listing = await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@wiki/list"));

		var rows = listing.Message!.ToPlainText()
			.Split('\n')
			.Skip(1) // the "WIKI: N page(s):" header
			.Select(r => r.Trim())
			.Where(r => r.Length > 0 && !r.StartsWith('…'))
			.ToArray();

		await Assert.That(rows).IsNotEmpty();
		foreach (var row in rows)
		{
			var identifier = row.Split(' ')[0];
			await Assert.That(identifier.Split(':').Length).IsEqualTo(2);
		}

		await Assert.That(rows.Any(r => r.StartsWith("main:qualified_row_page"))).IsTrue();
	}

	/// <summary>
	/// The claim the qualification is worth anything: an identifier a listing prints has to be one
	/// <c>@wiki</c> accepts back. A listing printing unusable identifiers is the same bug in a new place.
	/// </summary>
	[Test]
	public async ValueTask WikiList_PrintedIdentifierIsAcceptedBackByView()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiRoundTrip");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/create Round Trip Page=Body of the round trip page."));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/view main:round_trip_page"));
		await ExpectNotify(player.DbRef, "Body of the round trip page");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/view help:markdown_guide"));
		await ExpectNoNotify(player.DbRef, "WIKI: No such page: help:markdown_guide");
	}

	[Test]
	public async ValueTask WikiSearch_FindsPageByContent()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiSearcher");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/create Search Fodder=The xyzzy-marker phrase lives here."));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/search xyzzy-marker"));
		// The slug alone also appears in the create confirmation; the search header ("N page(s)
		// matching '<needle>'") is unique to the search reply and confirms the page was found.
		await ExpectNotify(player.DbRef, "1 page(s) matching 'xyzzy-marker'");
	}

	[Test]
	public async ValueTask WikiAppend_AddsRevision_HistoryShowsIt()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiAppender");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/create Append Target=First paragraph."));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/append append_target=Second paragraph."));
		await ExpectNotify(player.DbRef, "now rev 2");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/history append_target"));
		await ExpectNotify(player.DbRef, "Revision history for Append Target");
	}

	[Test]
	public async ValueTask WikiProtect_NonWizard_IsDenied()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiMortal");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/create Mortal Page=content"));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/protect mortal_page"));
		await ExpectNotify(player.DbRef, "needs the wiki.admin permission");
	}

	[Test]
	public async ValueTask WikiProtect_AsGod_LocksPageAgainstMortals()
	{
		var god = WebAppFactoryArg.ExecutorDBRef;
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiLocked");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/create Locked Page=original content"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@wiki/protect locked_page"));
		await ExpectNotify(god, "'Locked Page' is now protected");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/edit locked_page=replacement content"));
		await ExpectNotify(player.DbRef, "You can't edit 'Locked Page': the page requires wiki.admin");
	}

	[Test]
	public async ValueTask WikiPin_NonWizard_IsDenied()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiPinMortal");
		var category = TestIsolationHelpers.GenerateUniqueName("pinmortal").ToLowerInvariant();

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/pin {category}"));

		await ExpectNotify(player.DbRef, "needs the wiki.admin permission");
		await Assert.That(await WikiService.GetPinnedCategoriesAsync()).DoesNotContain(category);
	}

	[Test]
	public async ValueTask WikiPin_AsGod_PinsAndUnpinsACategory()
	{
		var god = WebAppFactoryArg.ExecutorDBRef;
		var category = TestIsolationHelpers.GenerateUniqueName("pinned").ToLowerInvariant();
		var label = WikiHelpers.CategoryLabel(category);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wiki/pin Category:{category}"));
		await ExpectNotify(god, $"'{label}' is now pinned to the wiki home");
		await Assert.That(await WikiService.GetPinnedCategoriesAsync()).Contains(category);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wiki/unpin {category}"));
		await ExpectNotify(god, $"'{label}' is no longer pinned to the wiki home");
		await Assert.That(await WikiService.GetPinnedCategoriesAsync()).DoesNotContain(category);
	}

	[Test]
	public async ValueTask WikiCategory_OnABiography_KeepsCharacter()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiBioFiler");
		var title = TestIsolationHelpers.GenerateUniqueName("Bio");
		var page = (await WikiService.CreateAsync(title, "Bio.", player.DbRef.ToString(), WikiNamespace.Character, "en")).Expect<WikiPage>();

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wiki/category Character:{title}="));

		await Assert.That((await WikiService.GetByIdAsync(page.Id)).Expect<WikiPage>().Categories).IsEquivalentTo(["character"]);
	}

	[Test]
	public async ValueTask WikiRollback_RestoresEarlierRevision()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiRoller");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/create Rollback Target=original body"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/edit rollback_target=changed body"));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/rollback rollback_target=1"));
		await ExpectNotify(player.DbRef, "Restored 'Rollback Target' to r1 (now rev 3)");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki rollback_target"));
		await ExpectNotify(player.DbRef, "original body");
	}

	[Test]
	public async ValueTask WikiRollback_UnknownRevision_Notifies()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiRollMiss");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/create Rollback Missing=body"));

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/rollback rollback_missing=42"));
		await ExpectNotify(player.DbRef, "has no revision r42");
	}

	[Test]
	public async ValueTask HelpAtWiki_LoadsSharpwikiHelpfile()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiHelpReader");

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("help @wiki"));

		await ExpectNotifyFromAnySender(player.DbRef, "in-game interface to the shared wiki");
	}

	[Test]
	public async ValueTask HelpWikiFunction_LoadsFunctionEntry()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiFnHelp");

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("help wiki()"));

		await ExpectNotifyFromAnySender(player.DbRef, "Returns information about a wiki page");
	}

	[Test]
	public async ValueTask WikiCategory_ListsPagesThatNameIt()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiCategorizer");

		var gate = await SeedSourcePageAsync("Wyrmholt Gate", "A gate.");
		var places = (await WikiService.CreateAsync("Wyrmholt Places", "Places in Wyrmholt.", "#1",
			WikiNamespace.Category, "en")).Expect<WikiPage>();

		// Categories are page data set by name, not text: setting them leaves the body alone. A category's
		// name is its category page's title.
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/category wyrmholt_gate=Wyrmholt Places"));
		await ExpectNotify(player.DbRef, "WIKI: 'Wyrmholt Gate' categories: Wyrmholt Places.");
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/category Category:Wyrmholt Places=Wyrmholt Setting"));
		await Assert.That((await WikiService.GetByIdAsync(gate.Id)).Expect<WikiPage>().MarkdownSource).IsEqualTo("A gate.");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/category wyrmholt places"));
		await ExpectNotify(player.DbRef, "WIKI: Category 'Wyrmholt Places' — 1 page(s), 0 subcategory(ies):");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/category Wyrmholt_Setting"));
		await ExpectNotify(player.DbRef, "Category:Wyrmholt Places");
		// Filing a page in a new category gave it a category page titled as typed, capitals and all.
		await ExpectNotify(player.DbRef, "WIKI: Category 'Wyrmholt Setting'");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki wyrmholt_gate"));
		await ExpectNotify(player.DbRef, "Categories: Wyrmholt Places");

		// Translating the category page translates the category's name; the page's categories stay its own.
		await WikiService.UpsertTranslationAsync(
			places.Id, "fr", "Lieux de Wyrmholt", "Lieux.", "#1", null, published: true, expectedRevisionNumber: null);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki wyrmholt_gate"));
		await ExpectNotify(player.DbRef, "Categories: Lieux de Wyrmholt");
	}

	[Test]
	public async ValueTask WikiCategory_WithAnEmptyListClearsThePagesCategories()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiUncategorizer");
		var title = TestIsolationHelpers.GenerateUniqueName("Uncat");
		var page = (await WikiService.CreateAsync(title, "body", "#1", WikiNamespace.Main, "en", ["Lore", "Myth"])).Expect<WikiPage>();

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/category {page.Slug}="));

		await ExpectNotify(player.DbRef, $"WIKI: '{title}' is in no category.");
		await Assert.That((await WikiService.GetByIdAsync(page.Id)).Expect<WikiPage>().Categories).IsEmpty();
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
			title, englishBody, "#1", WikiNamespace.Main, "en");
		var page = created.Expect<WikiPage>();

		var translated = await WikiService.UpsertTranslationAsync(
			page.Id, "fr", frenchTitle, frenchBody, "#1", null, published, expectedRevisionNumber: null);
		await Assert.That(translated is WikiTranslation)
			.IsTrue()
			.Because(translated switch
			{
				WikiTranslation => "translation seeded",
				WikiWriteConflict conflict => $"seeding lost a write race: {conflict}",
				Error<string> error => error.Value
			});

		return page.Slug;
	}

	[Test]
	public async ValueTask WikiView_ServesTheExecutorsLocaleWhenATranslationExists()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiFrenchReader");
		var slug = await SeedTranslatedPageAsync(
			"Locale Dragons", "en dragon body", "Dragons Localises", "corps du dragon", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/view {slug}"));

		await ExpectNotify(player.DbRef, "corps du dragon");
	}

	[Test]
	public async ValueTask WikiView_WithSourceSwitchForcesTheSourceLocale()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiSourceReader");
		var slug = await SeedTranslatedPageAsync(
			"Source Dragons", "en source body", "Dragons Sources", "corps source fr", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/view/source {slug}"));

		// The header carries the title, so asserting on the source title also proves the source row won.
		await ExpectNotify(player.DbRef, "Wiki: Source Dragons");
	}

	[Test]
	public async ValueTask WikiView_SourceSwitchDoesNotCountAsASecondAction()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiSwitchCounter");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/create Switch Count Page=body here"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/view/source switch_count_page"));

		// SOURCE is a modifier like NOEVAL. If it stayed in the action set, this would be "too many
		// switches" and the page would never render.
		await ExpectNotify(player.DbRef, "Wiki: Switch Count Page");
	}

	[Test]
	public async ValueTask WikiView_FallsBackToTheSourceWhenTheLocaleHasNoTranslation()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiGermanReader");

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale de"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/create Untranslated Page=only english body"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/view untranslated_page"));

		await ExpectNotify(player.DbRef, "only english body");
	}

	[Test]
	public async ValueTask WikiView_DraftTranslationDoesNotLeakToAMortalOnAProtectedPage()
	{
		// The protected companion of WikiView_DraftTranslationDoesNotLeakToAMortalOnAnUnprotectedPage:
		// protection is irrelevant to draft visibility, and both must hold.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftReader");
		var slug = await SeedTranslatedPageAsync(
			"Draft Locale Page", "en visible body", "Brouillon", "corps brouillon secret", published: false);

		var page = (await WikiService.GetBySlugAsync(slug, WikiNamespace.Main)).Expect<WikiPage>();
		await WikiService.ProtectAsync(page.Id);

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/view {slug}"));

		await ExpectNotify(player.DbRef, "en visible body");
		await ExpectNoNotify(player.DbRef, "corps brouillon secret");
	}

	[Test]
	public async ValueTask WikiView_APublishedTranslationDoesNotPublishItsDraftPage()
	{
		// The two Published flags are independent, and the page's is the one that decides whether the
		// article exists publicly at all. Deriving visibility from the *served row* let a published
		// translation speak for an unpublished page: a mortal whose locale matched was handed the draft's
		// content in full, with no (draft) marker and without asking for /DRAFT.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftPageTrReader");

		var slug = await SeedTranslatedPageAsync(
			"Draft Page Published Tr", "en secret host body", "Titre Publié", "corps publié secret",
			published: true);

		var page = (await WikiService.GetBySlugAsync(slug, WikiNamespace.Main)).Expect<WikiPage>();
		var unpublished = await WikiService.SetMetadataAsync(page.Id, [], published: false);
		await Assert.That(unpublished.Value).IsTypeOf<WikiPage>();

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/view {slug}"));

		await ExpectNoNotify(player.DbRef, "corps publié secret");
		await ExpectNoNotify(player.DbRef, "en secret host body");
	}

	[Test]
	public async ValueTask WikiHistory_APublishedTranslationDoesNotExposeADraftPagesLog()
	{
		// Same defect, second surface: edit summaries are author-written prose about unpublished work.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftPageTrHist");

		var slug = await SeedTranslatedPageAsync(
			"Draft Page Published Hist", "en host body", "Titre Historique", "corps historique",
			published: true);

		var page = (await WikiService.GetBySlugAsync(slug, WikiNamespace.Main)).Expect<WikiPage>();
		var unpublished = await WikiService.SetMetadataAsync(page.Id, [], published: false);
		await Assert.That(unpublished.Value).IsTypeOf<WikiPage>();

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/history {slug}"));

		await ExpectNotify(player.DbRef, "revision history is not shown");
	}

	[Test]
	public async ValueTask WikiList_ShowsLocalizedTitles()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiFrenchLister");
		await SeedTranslatedPageAsync(
			"Listed Dragons", "en listed body", "Dragons Listes", "corps liste", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@wiki/list"));

		await ExpectNotify(player.DbRef, "Dragons Listes");
	}

	/// <remarks>
	/// <c>@wiki/create</c> is the second and last create path in the codebase, and the only one reachable
	/// from in-game. Nothing else asserted that it stamps: dropping the <c>sourceLocale</c> argument left
	/// every unit and integration test green, because an unstamped page still renders — it just resolves
	/// against a blank locale forever, since nothing normalises empty on read.
	/// <para>
	/// The creator's <c>LOCALE</c> is set to something other than the default first, which is what makes
	/// this able to fail: with both equal, "stamps the configured default" and "stamps the creator's
	/// locale" are the same assertion, and the second is the rule this codebase does not follow.
	/// </para>
	/// </remarks>
	[Test]
	public async ValueTask WikiCreate_StampsTheConfiguredSourceLocaleNotTheCreatorsLocale()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiStampCreator");
		var localization = WebAppFactoryArg.Services.GetRequiredService<IWikiLocalizationService>();
		await Assert.That(localization.DefaultLocale).IsNotEqualTo("de");

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale de"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/create Stamped At Birth=body of a stamped page"));

		var created = await WikiService.GetBySlugAsync("stamped_at_birth", WikiNamespace.Main);
		await Assert.That(created.Expect<WikiPage>().SourceLocale)
			.IsEqualTo(localization.DefaultLocale)
			.Because("a page created in-game must be stamped at birth exactly as the API path is; the "
				+ "migration backfill is not a safety net for pages created after it ran");
	}

	[Test]
	public async ValueTask WikiHistory_ShowsTheTranslationsOwnStream()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiHistoryFrench");
		var slug = await SeedTranslatedPageAsync(
			"History Dragons", "en history body", "Dragons Historiques", "corps historique", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/history {slug}"));

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
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiHistoryGerman");
		var slug = await SeedTranslatedPageAsync(
			"History Gap Dragons", "en gap body", "Dragons Ecart", "corps ecart", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale de"));
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/history {slug}"));

		await ExpectNotify(player.DbRef, "r1");
	}

	[Test]
	public async ValueTask WikiHistory_SourceSwitchShowsTheSourceStreamToATranslatedReader()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiHistorySource");
		var slug = await SeedTranslatedPageAsync(
			"History Source Dragons", "en source-history body", "Dragons Source Hist", "corps source hist",
			published: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/history/source {slug}"));

		// The source stream carries no marker and the header keeps the source title.
		await ExpectNotify(player.DbRef, "History Source Dragons");
		await ExpectNoNotify(player.DbRef, "Dragons Source Hist");
	}

	/// <summary>Creates a page and unpublishes it through the service, returning it.</summary>
	/// <remarks>
	/// Unpublishing goes through <c>SetMetadataAsync</c> rather than <c>@wiki/unpublish</c> because that
	/// switch is wizard-only and these tests need the draft to exist before a mortal ever runs a command.
	/// </remarks>
	private async Task<WikiPage> SeedUnpublishedPageAsync(
		string title, string body, WikiNamespace ns = WikiNamespace.Main)
	{
		var created = await WikiService.CreateAsync(title, body, "#1", ns, "en");
		var page = created.Expect<WikiPage>();

		var unpublished = await WikiService.SetMetadataAsync(page.Id, [], published: false);
		return unpublished.Expect<WikiPage>();
	}

	[Test]
	public async ValueTask WikiSearch_DoesNotDiscloseUnpublishedPagesToAMortal()
	{
		// @wiki/view is gated, but search paged through GetAllPagesAsync — which documents that it returns
		// unpublished pages and leaves filtering to the caller — and filtered on nothing at all, so a draft's
		// title and reference reached any player who guessed a word from its body.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftSearcher");
		var page = await SeedUnpublishedPageAsync(
			"Mortal Draft Fodder", "The grue-marker phrase lives here.");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/search grue-marker"));

		await ExpectNotify(player.DbRef, "0 page(s) matching 'grue-marker'");
		await ExpectNoNotify(player.DbRef, page.Slug);
		await ExpectNoNotify(player.DbRef, page.Title);
	}

	[Test]
	public async ValueTask WikiSearch_StillFindsUnpublishedPagesForAWizard()
	{
		// Without this, "hide every draft unconditionally" — or a search that returns nothing at all —
		// would satisfy the test above. Unpublishing is wizard-only, so a wizard must still find the draft.
		var wizard = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftWizard");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		var page = await SeedUnpublishedPageAsync(
			"Wizard Draft Fodder", "The plover-marker phrase lives here.");

		await Parser.CommandParse(wizard.Handle, ConnectionService,
			MarkupText.Plain("@wiki/search plover-marker"));

		await ExpectNotify(wizard.DbRef, "1 page(s) matching 'plover-marker'");
		await ExpectNotify(wizard.DbRef, $"{page.Slug}");
	}

	[Test]
	public async ValueTask WikiSearch_FindsAPageByItsTranslationAndMarksTheLocale()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiLocaleSearcher");
		var slug = await SeedTranslatedPageAsync(
			"Searchable Dragons", "en searchable body", "Dragons Cherchables",
			"le corps contient sangloterie", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/search sangloterie"));

		// The needle appears in no English text this page holds, so finding it at all proves the
		// translation stream was scanned; the [fr] marker is what tells the reader why.
		await ExpectNotify(player.DbRef, "1 page(s) matching 'sangloterie'");
		await ExpectNotify(player.DbRef, $"{slug}");
		await ExpectNotify(player.DbRef, "[fr]");
	}

	[Test]
	public async ValueTask WikiSearch_ReportsAPageMatchingInBothLocalesOnce()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDedupeSearcher");
		await SeedTranslatedPageAsync(
			"Dedupe Dragons", "en body with kadingir", "Dragons Dedupe",
			"corps francais avec kadingir", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/search kadingir"));

		// Two matching rows, one page. Scanning two streams without a dedupe would say "2 page(s)".
		await ExpectNotify(player.DbRef, "1 page(s) matching 'kadingir'");
		await ExpectNoNotify(player.DbRef, "[fr]");
	}

	[Test]
	public async ValueTask WikiSearch_ReportsTheReadersOwnLocaleWhenSeveralMatched()
	{
		// Same double match as above, read by a French player. The source stream is scanned first, so
		// without the tie-break the reader would be told [en] — the one locale they did not ask for.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiTieBreakSearcher");
		await SeedTranslatedPageAsync(
			"Tiebreak Dragons", "en body with garabatos", "Dragons Egalite",
			"corps francais avec garabatos", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/search garabatos"));

		await ExpectNotify(player.DbRef, "1 page(s) matching 'garabatos'");
		await ExpectNotify(player.DbRef, "[fr]");
	}

	[Test]
	public async ValueTask WikiSearch_SourceSwitchIgnoresTranslations()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiSourceSearcher");
		await SeedTranslatedPageAsync(
			"Source Only Dragons", "en source-only body", "Dragons Source Seuls",
			"corps avec zwiebelturm", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/search/source zwiebelturm"));

		await ExpectNotify(player.DbRef, "0 page(s) matching 'zwiebelturm'");
	}

	[Test]
	public async ValueTask WikiSearch_DoesNotDiscloseUnpublishedTranslationsToAMortal()
	{
		// Step 1 stopped draft *pages* leaking; this is the same invariant one level down. The host page is
		// published and findable in English — only the French draft's text must be unreachable.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftTrSearcher");
		var slug = await SeedTranslatedPageAsync(
			"Draft Translation Dragons", "en host body", "Dragons Brouillon",
			"corps brouillon avec quenouille", published: false);

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/search quenouille"));

		await ExpectNotify(player.DbRef, "0 page(s) matching 'quenouille'");
		await ExpectNoNotify(player.DbRef, slug);
	}

	[Test]
	public async ValueTask WikiSearch_StillFindsUnpublishedTranslationsForAWizard()
	{
		var wizard = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftTrWizard");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		var slug = await SeedTranslatedPageAsync(
			"Wizard Draft Translation Dragons", "en wiz host body", "Dragons Brouillon Sorcier",
			"corps brouillon avec grelinette", published: false);

		await Parser.CommandParse(wizard.Handle, ConnectionService,
			MarkupText.Plain("@wiki/search grelinette"));

		await ExpectNotify(wizard.DbRef, "1 page(s) matching 'grelinette'");
		await ExpectNotify(wizard.DbRef, slug);
	}

	[Test]
	public async ValueTask WikiList_DoesNotDiscloseUnpublishedPagesToAMortal()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftLister");
		var page = await SeedUnpublishedPageAsync("Mortal Draft Listing", "listing body");

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@wiki/list"));

		await ExpectNoNotify(player.DbRef, page.Slug);
		await ExpectNotify(player.DbRef, "WIKI: ");
	}

	[Test]
	public async ValueTask WikiList_HeaderCountDoesNotDiscloseUnpublishedPages()
	{
		// The rows were filtered but the header total was not: it came from CountPagesAsync, which counted
		// drafts. Differencing "N page(s)" against the rows told a mortal how many drafts the window holds.
		// Both readers are exercised in one test because they share a store — a wizard-only assertion in a
		// separate test would count whatever draft the mortal test had already left behind.
		// The system namespace is used by nothing else, so both counts are exact rather than relative.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftCounter");
		var wizard = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftCounterWiz");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));

		var page = await SeedUnpublishedPageAsync(
			"Draft Counting Fodder", "counting body", WikiNamespace.System);

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@wiki/list system"));
		await ExpectNotify(player.DbRef, "WIKI: 0 page(s) in namespace 'system'");
		await ExpectNoNotify(player.DbRef, page.Slug);

		// A count hard-wired to the published total would satisfy the assertion above and hide the draft
		// from the one reader entitled to see it, so the wizard's view has to be pinned in the same breath.
		await Parser.CommandParse(wizard.Handle, ConnectionService, MarkupText.Plain("@wiki/list system"));
		await ExpectNotify(wizard.DbRef, "WIKI: 1 page(s) in namespace 'system'");
		await ExpectNotify(wizard.DbRef, page.Slug);
	}

	[Test]
	public async ValueTask WikiRecent_DoesNotDiscloseUnpublishedPagesToAMortal()
	{
		// Freshly written, so it heads the UpdatedAt ordering: without the filter this is the first row.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftRecent");
		var page = await SeedUnpublishedPageAsync("Mortal Draft Recent", "recent body");

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@wiki/recent"));

		await ExpectNoNotify(player.DbRef, page.Slug);
		await ExpectNotify(player.DbRef, "Recently edited pages");
	}

	[Test]
	public async ValueTask WikiRecent_StillShowsUnpublishedPagesToAWizard()
	{
		var wizard = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftRecentWiz");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		var page = await SeedUnpublishedPageAsync("Wizard Draft Recent", "wizard recent body");

		// The widest window there is: pages other tests write meanwhile must not push this one out of it.
		await Parser.CommandParse(wizard.Handle, ConnectionService, MarkupText.Plain("@wiki/recent 50"));

		await ExpectNotify(wizard.DbRef, page.Slug);
	}

	[Test]
	public async ValueTask WikiView_DoesNotRenderAnUnpublishedBodyToAMortal()
	{
		// The body-read half of the draft hole: /list, /search and /recent were filtered, but naming the
		// page outright still rendered it in full to anybody who guessed the slug.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftBodyMortal");
		var page = await SeedUnpublishedPageAsync(
			"Mortal Draft Body", "The zork-body-marker phrase lives here.");

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki {page.Slug}"));

		await ExpectNoNotify(player.DbRef, "zork-body-marker");
		// Not a bare "no such page": the page exists, and saying otherwise would be a lie the header
		// already contradicts everywhere else a draft is named.
		await ExpectNotify(player.DbRef, "This is a draft; its body is not shown.");
	}

	[Test]
	public async ValueTask WikiView_DraftSwitchTellsAMortalNothingExtra()
	{
		// The switch must not become an oracle: a reader who may not see drafts gets byte-identical
		// output with and without it, so /DRAFT can never be used to probe for a draft's existence.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftSwitchMortal");
		var page = await SeedUnpublishedPageAsync(
			"Mortal Draft Switch Body", "The plugh-body-marker phrase lives here.");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/view/draft {page.Slug}"));

		await ExpectNoNotify(player.DbRef, "plugh-body-marker");
		await ExpectNotify(player.DbRef, "This is a draft; its body is not shown.");
		await ExpectNoNotify(player.DbRef, "Add /DRAFT to read it.");
	}

	[Test]
	public async ValueTask WikiView_WithholdsAnUnpublishedBodyFromAWizardWithoutTheSwitch()
	{
		// "By default, it should not render unpublished bodies" applies to the wizard too — the switch is
		// the opt-in, not the permission.
		var wizard = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftBodyWizDefault");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		var page = await SeedUnpublishedPageAsync(
			"Wizard Draft Default Body", "The frotz-body-marker phrase lives here.");

		await Parser.CommandParse(wizard.Handle, ConnectionService, MarkupText.Plain($"@wiki {page.Slug}"));

		await ExpectNoNotify(wizard.DbRef, "frotz-body-marker");
		await ExpectNotify(wizard.DbRef, "Add /DRAFT to read it.");
	}

	[Test]
	public async ValueTask WikiView_DraftSwitchRendersTheBodyForAWizard()
	{
		// Without this, "never render an unpublished body" would satisfy every test above.
		var wizard = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftBodyWizShown");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		var page = await SeedUnpublishedPageAsync(
			"Wizard Draft Shown Body", "The plover-body-marker phrase lives here.");

		await Parser.CommandParse(wizard.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/view/draft {page.Slug}"));

		await ExpectNotify(wizard.DbRef, "plover-body-marker");
	}

	[Test]
	public async ValueTask WikiView_DraftSwitchLeavesAPublishedPageAlone()
	{
		// /DRAFT opts into unpublished bodies; it is not a display mode, so a published page reads
		// identically with it.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftSwitchPublished");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/create Draft Switch Published=The grue-body-marker phrase lives here."));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@wiki/view/draft draft_switch_published"));

		await ExpectNotify(player.DbRef, "grue-body-marker");
		await ExpectNoNotify(player.DbRef, "is a draft");
	}

	[Test]
	public async ValueTask WikiView_DraftTranslationDoesNotLeakToAMortalOnAnUnprotectedPage()
	{
		// The gate here used to be CanEdit, which every player passes on an unprotected page — so the
		// draft translation of any unprotected page rendered in full to anyone whose LOCALE matched it.
		// The published English body must still arrive: withholding it would lose a page they may read.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftTrUnprotected");
		var slug = await SeedTranslatedPageAsync(
			"Unprotected Draft Locale Page", "en unprotected visible body", "Brouillon Libre",
			"corps brouillon libre secret", published: false);

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/view {slug}"));

		await ExpectNotify(player.DbRef, "en unprotected visible body");
		await ExpectNoNotify(player.DbRef, "corps brouillon libre secret");
	}

	[Test]
	public async ValueTask WikiHistory_DoesNotDiscloseADraftsRevisionsToAMortal()
	{
		// Edit summaries are author-written prose about unpublished content, so the revision log is
		// gated on the same switch as the body rather than on a rule of its own.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftHistoryMortal");
		var page = await SeedUnpublishedPageAsync("Mortal Draft History", "draft history body");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/history {page.Slug}"));

		await ExpectNotify(player.DbRef, "This is a draft; its revision history is not shown.");
		await ExpectNoNotify(player.DbRef, "by #1");
	}

	[Test]
	public async ValueTask WikiHistory_DraftSwitchShowsADraftsRevisionsToAWizard()
	{
		var wizard = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftHistoryWiz");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		var page = await SeedUnpublishedPageAsync("Wizard Draft History", "wizard draft history body");

		await Parser.CommandParse(wizard.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/history/draft {page.Slug}"));

		await ExpectNotify(wizard.DbRef, "by #1");
	}

	/// <summary>Creates a plain English page through the service and returns it.</summary>
	private async Task<WikiPage> SeedSourcePageAsync(string title, string body)
	{
		var created = await WikiService.CreateAsync(title, body, "#1", WikiNamespace.Main, "en");
		return created.Expect<WikiPage>();
	}

	[Test]
	public async ValueTask WikiTranslate_WritesTheNamedLocaleAndAReaderInItSeesIt()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiTranslator");
		var page = await SeedSourcePageAsync("Translatable Dragons", "en dragon body");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/translate {page.Slug}/fr=corps traduit en jeu"));
		await ExpectNotify(player.DbRef, $"Wrote the fr translation of '{page.Title}'");

		// Reading it back through @wiki proves the row landed where a French reader resolves to, which a
		// write into the source page (or into some other locale) would not satisfy...
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/view {page.Slug}"));
		await ExpectNotify(player.DbRef, "corps traduit en jeu");

		// ...and the source body being untouched is what rules out "wrote the page itself".
		var reloaded = await WikiService.GetBySlugAsync(page.Slug, WikiNamespace.Main);
		await Assert.That(reloaded.Expect<WikiPage>().MarkdownSource).IsEqualTo("en dragon body");
	}

	[Test]
	public async ValueTask WikiTranslate_WithNoLanguageRefusesAndDoesNotFallBackToLocale()
	{
		// The rule this command exists to keep: reads may use LOCALE, writes never may. The executor is
		// set to fr and then omits the tag — if the write silently borrowed LOCALE it would succeed and
		// produce exactly the French row the explicit form produces, with nobody the wiser.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiTranslatorNoLang");
		var page = await SeedSourcePageAsync("Untagged Dragons", "en untagged body");

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale fr"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/translate {page.Slug}=corps sans etiquette"));

		await ExpectNotify(player.DbRef, "a translation needs an explicit language");

		var fr = await WikiService.GetTranslationAsync(page.Id, "fr");
		await Assert.That(fr.Value)
			.IsTypeOf<NotFound>()
			.Because("an untagged write must produce no translation at all, least of all one in the "
				+ "writer's own reading locale");

		var reloaded = await WikiService.GetBySlugAsync(page.Slug, WikiNamespace.Main);
		await Assert.That(reloaded.Expect<WikiPage>().MarkdownSource).IsEqualTo("en untagged body");
	}

	[Test]
	public async ValueTask WikiTranslate_RejectsAnUnrecognisedLanguageTag()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiTranslatorBadLang");
		var page = await SeedSourcePageAsync("Bad Tag Dragons", "en bad-tag body");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/translate {page.Slug}/zzq=corps en langue inventee"));

		// The message names the offending tag rather than reporting a generic syntax error.
		await ExpectNotify(player.DbRef, "'zzq' is not a recognised BCP-47 locale tag");

		var translations = await WikiService.GetTranslationsAsync(page.Id);
		await Assert.That(translations.Count).IsEqualTo(0);
	}

	[Test]
	public async ValueTask WikiTranslate_RefusesTheSourceLocaleWithAnInstruction()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiTranslatorSameLocale");
		var page = await SeedSourcePageAsync("Shadowing Dragons", "en shadowing body");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/translate {page.Slug}/en=an English shadow of the source"));

		// No row may shadow the source. The store enforces it; what is asserted here is that the player
		// is told what to do instead, rather than shown a raw error naming a storage id.
		await ExpectNotify(player.DbRef, "use @wiki/edit to change the page itself");
		await ExpectNoNotify(player.DbRef, "rather than adding a translation");

		var reloaded = await WikiService.GetBySlugAsync(page.Slug, WikiNamespace.Main);
		await Assert.That(reloaded.Expect<WikiPage>().MarkdownSource).IsEqualTo("en shadowing body");
	}

	[Test]
	public async ValueTask WikiTranslate_UpdatesAnExistingTranslationInsteadOfConflictingWithIt()
	{
		// The second write is the point: passing a null expectedRevisionNumber would make it an
		// AlreadyExists conflict, i.e. a translation that can be created in-game and then never updated.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiRetranslator");
		var page = await SeedSourcePageAsync("Revised Dragons", "en revised body");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/translate {page.Slug}/fr=premiere version"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/translate {page.Slug}/fr=deuxieme version"));

		await ExpectNotify(player.DbRef, "now rev 2");

		var fr = (await WikiService.GetTranslationAsync(page.Id, "fr")).Expect<WikiTranslation>();
		await Assert.That(fr.MarkdownSource).IsEqualTo("deuxieme version");
		await Assert.That(fr.RevisionNumber).IsEqualTo(2);
	}

	[Test]
	public async ValueTask WikiTranslate_ObeysThePagesProtectionGate()
	{
		// A translation is an edit to the page and honours the page's own gate — no new permission.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiTranslatorMortal");
		var page = await SeedSourcePageAsync("Protected Dragons", "en protected body");
		await WikiService.ProtectAsync(page.Id);

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/translate {page.Slug}/fr=corps interdit"));

		await ExpectNotify(player.DbRef, $"You can't edit '{page.Title}': the page requires wiki.admin");

		var fr = await WikiService.GetTranslationAsync(page.Id, "fr");
		await Assert.That(fr.Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async ValueTask WikiTranslate_LetsAWizardTranslateAProtectedPage()
	{
		// Without this, "refuse every translation of a protected page" would satisfy the test above.
		var wizard = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiTranslatorWiz");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		var page = await SeedSourcePageAsync("Wizard Protected Dragons", "en wiz protected body");
		await WikiService.ProtectAsync(page.Id);

		await Parser.CommandParse(wizard.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/translate {page.Slug}/fr=corps autorise"));

		await ExpectNotify(wizard.DbRef, $"Wrote the fr translation of '{page.Title}'");

		var fr = (await WikiService.GetTranslationAsync(page.Id, "fr")).Expect<WikiTranslation>();
		await Assert.That(fr.MarkdownSource).IsEqualTo("corps autorise");
	}

	[Test]
	public async ValueTask WikiTranslate_KeepsAnExistingTranslationsDraftStateAndTitle()
	{
		// The command supplies a body and nothing else. A web translator's in-progress draft must not be
		// published, nor retitled to the source title, by somebody typing a body correction in-game.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiTranslatorDraftKeeper");
		var slug = await SeedTranslatedPageAsync(
			"Draft Keeping Dragons", "en keeper body", "Dragons Conserves", "corps initial", published: false);
		var page = (await WikiService.GetBySlugAsync(slug, WikiNamespace.Main)).Expect<WikiPage>();

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/translate {slug}/fr=corps corrige"));

		var fr = (await WikiService.GetTranslationAsync(page.Id, "fr")).Expect<WikiTranslation>();
		await Assert.That(fr.MarkdownSource).IsEqualTo("corps corrige");
		await Assert.That(fr.Published).IsFalse();
		await Assert.That(fr.Title).IsEqualTo("Dragons Conserves");
	}

	[Test]
	public async ValueTask WikiTranslate_MakesANewTranslationReadableRatherThanADraft()
	{
		// @wiki has no per-translation publish switch, so a draft created here would be unreachable
		// in-game forever — including by the translator who wrote it.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiTranslatorFresh");
		var page = await SeedSourcePageAsync("Fresh Dragons", "en fresh body");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/translate {page.Slug}/fr=corps tout neuf"));

		var fr = (await WikiService.GetTranslationAsync(page.Id, "fr")).Expect<WikiTranslation>();
		await Assert.That(fr.Published).IsTrue();
		await Assert.That(fr.Title).IsEqualTo(page.Title);
	}

	[Test]
	public async ValueTask WikiTranslate_RefusesATargetCarryingMoreThanOneSlash()
	{
		// Two slashes have no unambiguous reading, and picking one occurrence would be a guess about
		// where a player's page name ends — the one thing this syntax exists to avoid.
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiTranslatorTwoSlash");
		var page = await SeedSourcePageAsync("Ambiguous Dragons", "en ambiguous body");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/translate {page.Slug}/fr/de=corps ambigu"));

		await ExpectNotify(player.DbRef, "has more than one '/'");

		var translations = await WikiService.GetTranslationsAsync(page.Id);
		await Assert.That(translations.Count).IsEqualTo(0);
	}

	/// <summary>
	/// The rendered notification, in a client format that can express a clickable command. Asserting on
	/// plain text as <see cref="ExpectNotify"/> does could never see this: the ANSI form of a command link
	/// is plain underlined text, because OSC 8 can only navigate to a URL.
	/// </summary>
	private async Task ExpectNotifyRendered(SharpMUSH.Library.Models.DBRef player, MarkupFormat format, string contains) =>
		await Assert.That(WebAppFactoryArg.Notifications.RawFor(player).Count(msg => RendersToContain(msg, format, contains)))
			.IsEqualTo(1);

	/// <summary>
	/// A <c>[[Page Name]]</c> link in a page body is clickable when <c>@wiki</c> displays it: the target
	/// the shared renderer discards becomes <c>@wiki &lt;namespace&gt;:&lt;category&gt;:&lt;slug&gt;</c>,
	/// which is the fully qualified reference every wiki listing already prints and which resolves back to
	/// the page the link named.
	/// </summary>
	[Test]
	public async ValueTask WikiView_WikiLinkInBody_IsAClickableCommandLink()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiLinkReader");
		// Seeded through the service rather than @wiki/create so the [[...]] brackets reach the page body
		// verbatim, without the command's softcode evaluation having an opinion about them.
		var page = await SeedSourcePageAsync("Linking Dragons", "See [[Help:Markdown Guide]] for details.");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki {page.Slug}"));

		await ExpectNotifyRendered(player.DbRef, MarkupFormat.Html, "xch_cmd=\"@wiki help:markdown_guide\"");
		await ExpectNotifyRendered(player.DbRef, MarkupFormat.Pueblo, "XCH_CMD=\"@wiki help:markdown_guide\"");
		// The display text is the page title, never the raw target, and the brackets never leak.
		await ExpectNotify(player.DbRef, "See Markdown Guide for details.");
	}

	/// <summary>
	/// The markdown a page is stored with, containing every construct the renderer would otherwise
	/// transform: headings, a table, inline markup, a fenced code block, and softcode punctuation.
	/// </summary>
	private const string RawSourceBody =
		"# Heading\n\nSome **bold** text and a [[Wiki Link]].\n\n"
		+ "| A | B |\n|---|---|\n| 1 | 2 |\n\n"
		+ "```sharp\nthink [add(1,2)] %0 $foo\n```\n\n- item one\n- item two";

	/// <summary>
	/// <c>@wiki/md</c> answers with the stored source, byte for byte. <c>wiki(&lt;page&gt;, markdown)</c>
	/// could already do this for softcode; the command could not, so reading the source of a page before
	/// editing it meant going through a function to read your own wiki.
	/// </summary>
	[Test]
	public async ValueTask WikiView_Md_ShowsStoredMarkdownVerbatim()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiMdReader");
		var page = await SeedSourcePageAsync("Raw Source Dragons", RawSourceBody);

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/md {page.Slug}"));

		// Every line intact and in order: nothing rendered, reflowed, re-indented or wrapped.
		await ExpectNotify(player.DbRef, RawSourceBody);
		// The header still identifies the page, so a raw body is not an anonymous blob of text.
		await ExpectNotify(player.DbRef, $"Wiki: {page.Title} [main]");
	}

	/// <summary>
	/// Raw markdown is full of <c>[</c>, <c>%</c> and <c>$</c>. It has to arrive as text.
	/// </summary>
	[Test]
	public async ValueTask WikiView_Md_DoesNotEvaluateTheSourceAsSoftcode()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiMdLiteral");
		var page = await SeedSourcePageAsync("Literal Dragons", "Call [add(1,2)] with %0 and $foo.");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/md {page.Slug}"));

		await ExpectNotify(player.DbRef, "Call [add(1,2)] with %0 and $foo.");
		await ExpectNoNotify(player.DbRef, "Call 3 with");
	}

	/// <summary>
	/// <c>/md</c> is a modifier and composes with the other modifiers rather than replacing them:
	/// <c>/source/md</c> is the source locale's markdown, which is what a translator wants in front of
	/// them before writing <c>@wiki/translate</c>.
	/// </summary>
	[Test]
	public async ValueTask WikiView_SourceMd_ShowsTheSourceLocalesMarkdown()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiMdTranslator");
		var slug = await SeedTranslatedPageAsync(
			"Md Locale Dragons", "# English source", "Dragons Md", "# Source francaise", published: true);

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale fr"));

		// Without /source the reader's own locale is served, still as raw markdown.
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/view/md {slug}"));
		await ExpectNotify(player.DbRef, "# Source francaise");

		// With it, the locale the page was written in.
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/view/source/md {slug}"));
		await ExpectNotify(player.DbRef, "# English source");
	}

	/// <summary>
	/// <c>/md</c> is a presentation choice made after permission, never instead of it: asking for a
	/// draft's source is still asking for a draft.
	/// </summary>
	[Test]
	public async ValueTask WikiView_Md_StillWithholdsADraftWithoutTheDraftSwitch()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiMdDraftReader");
		var draft = await SeedUnpublishedPageAsync(
			"Md Draft Dragons", "Contains the plugh-md-marker token.");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wiki/md {draft.Slug}"));

		await ExpectNoNotify(player.DbRef, "plugh-md-marker");
		await ExpectNotify(player.DbRef, "WIKI: This is a draft; its body is not shown.");
	}

	/// <summary>
	/// True when the message went out as markup whose rendering in <paramref name="format"/> contains
	/// <paramref name="contains"/>.
	/// </summary>
	private static bool RendersToContain(SharpMessage msg, MarkupFormat format, string contains) =>
		msg is MString markup && markup.Render(format).Contains(contains);

	private async Task<string> AsPlayerAsync(SharpMUSH.Library.Models.DBRef player, string code)
		=> (await WebAppFactoryArg.FunctionParserFor(player).FunctionParse(MarkupText.Plain(code)))!.Message!.ToPlainText();

	/// <summary>A page filed in a category of its own, created and filed by <paramref name="player"/>.</summary>
	private async Task<(string Title, string Slug, string Category)> FiledPageAsync(TestIsolationHelpers.TestPlayer player, string prefix)
	{
		var title = TestIsolationHelpers.GenerateUniqueName(prefix);
		var category = WikiHelpers.CategoryKey(TestIsolationHelpers.GenerateUniqueName(prefix + "Cat"));
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/create {title}=body"));
		var page = (await WikiService.GetBySlugAsync(WikiHelpers.Slugify(title))).Expect<WikiPage>();
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/category {page.Slug}={category}"));
		return (title, page.Slug, category);
	}

	[Test]
	public async ValueTask WikiRequire_CategoryRequirementClosesEditsToPlayersWithoutIt()
	{
		var god = WebAppFactoryArg.ExecutorDBRef;
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiRequired");
		var (title, slug, category) = await FiledPageAsync(player, "Gated");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wiki/require category {category}=edit media.admin"));
		await ExpectNotify(god, $"WIKI: Category {category} now requires:");

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/edit {slug}=changed"));
		await ExpectNotify(player.DbRef, $"You can't edit '{title}': category {category} requires media.admin");
		await Assert.That((await WikiService.GetBySlugAsync(slug)).Expect<WikiPage>().MarkdownSource).IsEqualTo("body");

		await Assert.That(await AsPlayerAsync(player.DbRef, $"wikiaccess({slug}, edit)")).IsEqualTo("0");
		await Assert.That(await AsPlayerAsync(player.DbRef, $"wikiaccess({slug}, read)")).IsEqualTo("1");
		await Assert.That(await AsPlayerAsync(god, $"wikiaccess({slug}, edit, *{player.Name})")).IsEqualTo("0");
		await Assert.That(await AsPlayerAsync(god, $"wikiaccess({slug}, edit)")).IsEqualTo("1");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wiki/access {slug}=*{player.Name}"));
		await ExpectNotify(god, $"edit    category {category} requires media.admin");
	}

	[Test]
	public async ValueTask WikiRequire_ReadRequirementHidesThePageAsIfMissing()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiHidden");
		var (_, slug, category) = await FiledPageAsync(player, "Hidden");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wiki/require category {category}=read media.admin"));

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki {slug}"));
		await ExpectNotify(player.DbRef, $"WIKI: No such page: {slug}");
		await Assert.That(await AsPlayerAsync(player.DbRef, $"wiki({slug}, title)")).IsEqualTo(SharpMUSH.Library.Definitions.ErrorMessages.Returns.NoSuchWikiPage);
		await Assert.That(await AsPlayerAsync(player.DbRef, $"wikicategory({category})")).IsEqualTo(string.Empty);
	}

	[Test]
	public async ValueTask WikiAccess_ADraftAnswersAsAMissingPage()
	{
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiDraftAsker");
		var title = TestIsolationHelpers.GenerateUniqueName("DraftAccess");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wiki/create {title}=body"));
		var slug = WikiHelpers.Slugify(title);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wiki/unpublish {slug}"));

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/access {slug}"));
		await ExpectNotify(player.DbRef, $"WIKI: No such page, namespace or category: {slug}");
	}

	[Test]
	public async ValueTask WikiRequire_NeedsWikiAdminAndAKnownPermission()
	{
		var god = WebAppFactoryArg.ExecutorDBRef;
		var player = await ApprovedPlayerAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiRequirer");
		var category = WikiHelpers.CategoryKey(TestIsolationHelpers.GenerateUniqueName("Req"));

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki/require category {category}=edit media.admin"));
		await ExpectNotify(player.DbRef, "Setting requirements needs the wiki.admin permission");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wiki/require category {category}=edit no.such.permission"));
		await ExpectNotify(god, "WIKI: No such permission: no.such.permission.");

		await Assert.That((await WikiService.GetRequirementsAsync()).For(WikiRuleTarget.ForCategory(category))).IsNull();
	}
}
