using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Integration tests for the @wiki command: page creation, viewing, listing,
/// search, history, append, and the wizard-only protection rules. Pages are
/// stored through the same IWikiService the web portal uses.
/// </summary>
[NotInParallel] // shared NotifyService substitute + ClearReceivedCalls must not race
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

	[Test]
	public async ValueTask WikiCreate_ThenView_ShowsRenderedPage()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiCreator");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/create Cmd Test Page=# Cmd Heading\n\nSome **bold** body."));
		await ExpectNotify(player.DbRef, "WIKI: Created page 'Cmd Test Page'");

		NotifyService.ClearReceivedCalls();
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

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/search xyzzy-marker"));
		await ExpectNotify(player.DbRef, "search_fodder");
	}

	[Test]
	public async ValueTask WikiAppend_AddsRevision_HistoryShowsIt()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiAppender");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/create Append Target=First paragraph."));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/append append_target=Second paragraph."));
		await ExpectNotify(player.DbRef, "now rev 2");

		NotifyService.ClearReceivedCalls();
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

		NotifyService.ClearReceivedCalls();
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

		// God (#1, connection handle 1) protects the page …
		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@wiki/protect locked_page"));
		await ExpectNotify(god, "now protected");

		// … and the mortal can no longer edit it.
		NotifyService.ClearReceivedCalls();
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

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/rollback rollback_target=1"));
		await ExpectNotify(player.DbRef, "Restored 'Rollback Target' to r1 (now rev 3)");

		// The restored body is served on view again.
		NotifyService.ClearReceivedCalls();
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

		NotifyService.ClearReceivedCalls();
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

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@wiki/tag tagged_page=Magic FIRE magic"));
		await ExpectNotify(player.DbRef, "set to: fire, magic");
	}
}
