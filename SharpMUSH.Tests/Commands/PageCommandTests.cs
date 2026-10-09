using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class PageCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	[Test]
	public async ValueTask PlainPage_UsesSpeechFormAndRecipientName()
	{
		var player = await CreatePlayerAsync("PagePlain");
		try
		{
			var outgoing = $"You paged {player.Name} with 'Test'";
			var incoming = $"{player.Name} pages: Test";
			var messages = await PageSelfAsync(player, "Test", outgoing, incoming);

			await Assert.That(messages.Length).IsEqualTo(2);
			await Assert.That(messages[0]).IsEqualTo(outgoing);
			await Assert.That(messages[1]).IsEqualTo(incoming);
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	[Test]
	public async ValueTask PosePage_StripsColonAndUsesLongDistanceForm()
	{
		var player = await CreatePlayerAsync("PagePose");
		try
		{
			var outgoing = $"Long distance to {player.Name}: {player.Name} Test";
			var incoming = $"From afar, {player.Name} Test";
			var messages = await PageSelfAsync(player, ":Test", outgoing, incoming);

			await Assert.That(messages.Length).IsEqualTo(2);
			await Assert.That(messages[0]).IsEqualTo(outgoing);
			await Assert.That(messages[1]).IsEqualTo(incoming);
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	[Test]
	public async ValueTask SemiposePage_StripsSemicolonAndOmitsGap()
	{
		var player = await CreatePlayerAsync("PageSemipose");
		try
		{
			var outgoing = $"Long distance to {player.Name}: {player.Name}Test";
			var incoming = $"From afar, {player.Name}Test";
			var messages = await PageSelfAsync(player, ";Test", outgoing, incoming);

			await Assert.That(messages.Length).IsEqualTo(2);
			await Assert.That(messages[0]).IsEqualTo(outgoing);
			await Assert.That(messages[1]).IsEqualTo(incoming);
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	[Test]
	public async ValueTask PageFormats_ReceivePennArgumentsAndReplaceDefaults()
	{
		var sender = await CreatePlayerAsync("PageFormatSender");
		var recipient = await CreatePlayerAsync("PageFormatRecipient");
		try
		{
			await AttributeService.SetAttributeAsync(recipient.Object, recipient.Object, "PAGEFORMAT",
				MarkupText.Plain("IN:%0|%1|%2|%3|%4"));
			await AttributeService.SetAttributeAsync(sender.Object, sender.Object, "OUTPAGEFORMAT",
				MarkupText.Plain("OUT:%0|%1|%2|%3|%4"));

			var senderStart = Notifications.CountFor(sender.DbRef);
			var recipientStart = Notifications.CountFor(recipient.DbRef);
			await PageAsync(sender, recipient.Name, ":Test");
			var recipientDbRef = $"#{recipient.DbRef.Number}";
			var incoming = $"IN:Test|:||{recipientDbRef}|From afar, {sender.Name} Test";
			var outgoing = $"OUT:Test|:||{recipientDbRef}|Long distance to {recipient.Name}: {sender.Name} Test";
			var defaultIncoming = $"From afar, {sender.Name} Test";
			var defaultOutgoing = $"Long distance to {recipient.Name}: {sender.Name} Test";

			await Assert.That(PageMessagesSince(recipient.DbRef, recipientStart, incoming, defaultIncoming))
				.IsEquivalentTo([incoming]);
			await Assert.That(PageMessagesSince(sender.DbRef, senderStart, outgoing, defaultOutgoing))
				.IsEquivalentTo([outgoing]);
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(recipient.Handle);
		}
	}

	[Test]
	public async ValueTask MultipleRecipients_UseReadableNamesAndPennContext()
	{
		var sender = await CreatePlayerAsync("PageMultiSender");
		var first = await CreatePlayerAsync("PageMultiFirst");
		var second = await CreatePlayerAsync("PageMultiSecond");
		try
		{
			var senderStart = Notifications.CountFor(sender.DbRef);
			var firstStart = Notifications.CountFor(first.DbRef);
			var secondStart = Notifications.CountFor(second.DbRef);

			await PageAsync(sender, $"{first.Name} {second.Name}", "Test");

			var recipientList = $"{first.Name} and {second.Name}";
			var outgoing = $"You paged {recipientList} with 'Test'";
			var incoming = $"{sender.Name} pages {recipientList}: Test";
			await Assert.That(PageMessagesSince(sender.DbRef, senderStart, outgoing)).IsEquivalentTo([outgoing]);
			await Assert.That(PageMessagesSince(first.DbRef, firstStart, incoming)).IsEquivalentTo([incoming]);
			await Assert.That(PageMessagesSince(second.DbRef, secondStart, incoming)).IsEquivalentTo([incoming]);
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(first.Handle);
			await ConnectionService.Disconnect(second.Handle);
		}
	}

	[Test]
	public async ValueTask PageFormats_SeeCurrentLastPagedRecipients()
	{
		var player = await CreatePlayerAsync("PageFormatLastPaged");
		try
		{
			await AttributeService.SetAttributeAsync(player.Object, player.Object, "PAGEFORMAT",
				MarkupText.Plain("IN:[get(me/LASTPAGED)]"));
			await AttributeService.SetAttributeAsync(player.Object, player.Object, "OUTPAGEFORMAT",
				MarkupText.Plain("OUT:[get(me/LASTPAGED)]"));

			var outgoing = $"OUT:{player.DbRef}";
			var incoming = $"IN:{player.DbRef}";
			var defaultOutgoing = $"You paged {player.Name} with 'Test'";
			var defaultIncoming = $"{player.Name} pages: Test";
			var messages = await PageSelfAsync(
				player, "Test", outgoing, incoming, defaultOutgoing, defaultIncoming);

			await Assert.That(messages.Length).IsEqualTo(2);
			await Assert.That(messages[0]).IsEqualTo(outgoing);
			await Assert.That(messages[1]).IsEqualTo(incoming);
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	[Test]
	public async ValueTask ForcedPage_OutPageFormatRunsEntirelyAsPager()
	{
		var pager = await CreatePlayerAsync("ForcedPagePager");
		var recipient = await CreatePlayerAsync("ForcedPageRecipient");
		try
		{
			await AttributeService.SetAttributeAsync(pager.Object, pager.Object, "OUTPAGEFORMAT",
				MarkupText.Plain("OUT:%!|%@|%#"));

			var pagerStart = Notifications.CountFor(pager.DbRef);
			await GodCommandAsync($"@force {pager.DbRef}=page {recipient.Name}=Test");

			var expected = $"OUT:#{pager.DbRef.Number}|#{pager.DbRef.Number}|#{pager.DbRef.Number}";
			var defaultOutgoing = $"You paged {recipient.Name} with 'Test'";
			var messages = PageMessagesSince(pager.DbRef, pagerStart, expected, defaultOutgoing);
			await Assert.That(messages.Length).IsEqualTo(1);
			await Assert.That(messages[0]).IsEqualTo(expected);
		}
		finally
		{
			await ConnectionService.Disconnect(pager.Handle);
			await ConnectionService.Disconnect(recipient.Handle);
		}
	}

	[Test]
	public async ValueTask LastPaged_UpdatesAfterEachSuccessfulPageAndDrivesRepage()
	{
		var sender = await CreatePlayerAsync("PageLastPagedSender");
		var first = await CreatePlayerAsync("PageLastPagedFirst");
		var second = await CreatePlayerAsync("PageLastPagedSecond");
		try
		{
			await AttributeService.SetAttributeAsync(sender.Object, sender.Object, "OUTPAGEFORMAT",
				MarkupText.Plain("OUT:[get(me/LASTPAGED)]"));

			await PageAsync(sender, first.Name, "First");

			var secondPageStart = Notifications.CountFor(sender.DbRef);
			await PageAsync(sender, second.Name, "Second");
			var outgoing = $"OUT:{second.DbRef}";
			var secondDefaultOutgoing = $"You paged {second.Name} with 'Second'";
			await Assert.That(PageMessagesSince(sender.DbRef, secondPageStart, outgoing, secondDefaultOutgoing))
				.IsEquivalentTo([outgoing]);

			var senderRepageStart = Notifications.CountFor(sender.DbRef);
			var firstRepageStart = Notifications.CountFor(first.DbRef);
			var secondRepageStart = Notifications.CountFor(second.DbRef);
			await PageAsync(sender, string.Empty, "Again");

			var incoming = $"{sender.Name} pages: Again";
			var repageDefaultOutgoing = $"You paged {second.Name} with 'Again'";
			await Assert.That(PageMessagesSince(sender.DbRef, senderRepageStart, outgoing, repageDefaultOutgoing))
				.IsEquivalentTo([outgoing]);
			await Assert.That(PageMessagesSince(first.DbRef, firstRepageStart, incoming)).IsEmpty();
			await Assert.That(PageMessagesSince(second.DbRef, secondRepageStart, incoming)).IsEquivalentTo([incoming]);
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(first.Handle);
			await ConnectionService.Disconnect(second.Handle);
		}
	}

	[Test]
	public async ValueTask PageList_ReportsMostRecentlyPagedRecipients()
	{
		var sender = await CreatePlayerAsync("PageListSender");
		var first = await CreatePlayerAsync("PageListFirst");
		var second = await CreatePlayerAsync("PageListSecond");
		try
		{
			await PageAsync(sender, first.Name, "First");
			await PageAsync(sender, second.Name, "Second");

			var senderStart = Notifications.CountFor(sender.DbRef);
			await CommandAsync(sender, "page/list");

			var expected = $"You last paged {second.Name}.";
			await Assert.That(PageMessagesSince(sender.DbRef, senderStart, expected)).IsEquivalentTo([expected]);
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(first.Handle);
			await ConnectionService.Disconnect(second.Handle);
		}
	}

	[Test]
	public async ValueTask PageList_WithoutHistoryUsesPennMessage()
	{
		var sender = await CreatePlayerAsync("PageListNoHistorySender");
		try
		{
			var senderStart = Notifications.CountFor(sender.DbRef);
			await CommandAsync(sender, "page/list");

			const string expected = "You haven't paged anyone since connecting.";
			await Assert.That(PageMessagesSince(sender.DbRef, senderStart, expected)).IsEquivalentTo([expected]);
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
		}
	}

	[Test]
	public async ValueTask PageList_IgnoresOperandsAndOnlyReportsStoredHistory()
	{
		var sender = await CreatePlayerAsync("PageListOperandSender");
		var previousRecipient = await CreatePlayerAsync("PageListPreviousRecipient");
		var operandRecipient = await CreatePlayerAsync("PageListOperandRecipient");
		try
		{
			await PageAsync(sender, previousRecipient.Name, "First");

			foreach (var command in new[]
			{
				$"page/list {operandRecipient.Name}",
				$"page/list {operandRecipient.Name}=Ignored"
			})
			{
				var senderStart = Notifications.CountFor(sender.DbRef);
				var operandStart = Notifications.CountFor(operandRecipient.DbRef);
				await CommandAsync(sender, command);

				var expected = $"You last paged {previousRecipient.Name}.";
				var unintendedPage = $"{sender.Name} pages: Ignored";
				await Assert.That(PageMessagesSince(sender.DbRef, senderStart, expected)).IsEquivalentTo([expected]);
				await Assert.That(PageMessagesSince(operandRecipient.DbRef, operandStart, unintendedPage)).IsEmpty();
			}
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(previousRecipient.Handle);
			await ConnectionService.Disconnect(operandRecipient.Handle);
		}
	}

	[Test]
	public async ValueTask PageList_AllStaleObjidsUsesPennMissingRecipientsMessage()
	{
		var sender = await CreatePlayerAsync("PageListAllStaleSender");
		var recipient = await CreatePlayerAsync("PageListAllStaleRecipient");
		try
		{
			var staleRecipient = new DBRef(
				recipient.DbRef.Number, recipient.DbRef.CreationMilliseconds!.Value + 1);
			await GodCommandAsync($"&LASTPAGED {sender.DbRef}={staleRecipient}");

			var senderStart = Notifications.CountFor(sender.DbRef);
			await CommandAsync(sender, "page/list");

			const string expected = "I can't find who you last paged.";
			await Assert.That(PageMessagesSince(sender.DbRef, senderStart, expected)).IsEquivalentTo([expected]);
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(recipient.Handle);
		}
	}

	/// <summary>
	/// The portal replies to a conversation by objid (<c>#N:created</c>), so a page to the full objid reaches
	/// that player, and one whose creation stamp no longer matches (the dbref was recycled) reaches nobody.
	/// </summary>
	[Test]
	public async ValueTask Page_ByObjid_ReachesThatPlayer_AndAStaleObjidReachesNobody()
	{
		var sender = await CreatePlayerAsync("PageObjidSender");
		var recipient = await CreatePlayerAsync("PageObjidRecipient");
		try
		{
			var live = TestIsolationHelpers.GenerateUniqueName("PageObjidLive");
			var stale = TestIsolationHelpers.GenerateUniqueName("PageObjidStale");
			var staleObjid = new DBRef(recipient.DbRef.Number, recipient.DbRef.CreationMilliseconds!.Value + 1);
			var start = Notifications.CountFor(recipient.DbRef);

			await PageAsync(sender, recipient.DbRef.ToString(), live);
			await PageAsync(sender, staleObjid.ToString(), stale);

			var heard = Notifications.For(recipient.DbRef).Skip(start).ToList();
			await Assert.That(heard).Contains($"{sender.Name} pages: {live}");
			await Assert.That(heard.Any(line => line.Contains(stale, StringComparison.Ordinal))).IsFalse();
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(recipient.Handle);
		}
	}

	[Test]
	public async ValueTask PageList_MixedLiveAndStaleObjidsOnlyReportsLiveRecipients()
	{
		var sender = await CreatePlayerAsync("PageListMixedSender");
		var liveRecipient = await CreatePlayerAsync("PageListMixedLiveRecipient");
		var staleRecipient = await CreatePlayerAsync("PageListMixedStaleRecipient");
		try
		{
			var staleObjid = new DBRef(
				staleRecipient.DbRef.Number, staleRecipient.DbRef.CreationMilliseconds!.Value + 1);
			await GodCommandAsync($"&LASTPAGED {sender.DbRef}={liveRecipient.DbRef} {staleObjid}");

			var senderStart = Notifications.CountFor(sender.DbRef);
			await CommandAsync(sender, "page/list");

			var expected = $"You last paged {liveRecipient.Name}.";
			await Assert.That(PageMessagesSince(sender.DbRef, senderStart, expected)).IsEquivalentTo([expected]);
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(liveRecipient.Handle);
			await ConnectionService.Disconnect(staleRecipient.Handle);
		}
	}

	[Test]
	public async ValueTask IncomingPageFormat_RunsAsRecipientWithPagerAsEnactor()
	{
		var sender = await CreatePlayerAsync("PageIdentitySender");
		var recipient = await CreatePlayerAsync("PageIdentityRecipient");
		try
		{
			await AttributeService.SetAttributeAsync(recipient.Object, recipient.Object, "PAGEFORMAT",
				MarkupText.Plain("IN:%!|%@|%#"));

			var recipientStart = Notifications.CountFor(recipient.DbRef);
			await PageAsync(sender, recipient.Name, "Test");

			var expected = $"IN:#{recipient.DbRef.Number}|#{recipient.DbRef.Number}|#{sender.DbRef.Number}";
			var defaultIncoming = $"{sender.Name} pages: Test";
			var messages = PageMessagesSince(recipient.DbRef, recipientStart, expected, defaultIncoming);
			await Assert.That(messages.Length).IsEqualTo(1);
			await Assert.That(messages[0]).IsEqualTo(expected);
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(recipient.Handle);
		}
	}

	// --- Recipient resolution (speech.c do_page:906-957) ----------------------------------------
	//
	// Every expectation below was read off a live PennMUSH 1.8.8 (pennmush @ 80a1d5b9), 2026-09-28,
	// with One in #0 and PBob connected in a room of his own:
	//
	//   > page PBob=Are you there?    You paged PBob with 'Are you there?'   → PBob: One pages: Are you there?
	//   > page Nobody=hello           I can't find who you're trying to page with: Nobody
	//                                 Unable to page: Nobody
	//   > page me=hello self          I can't find who you're trying to page with: me
	//                                 Unable to page: me
	//   > page PBo=partial            You paged PBob with 'partial'
	//   > page POff=offline test      POff is not connected. / Unable to page: POff
	//   > page PBob Nobody=mixed      I can't find ...: Nobody / Unable to page: Nobody
	//                                 You paged PBob with 'mixed'
	//   > page "Master Room"=quoted   I can't find ...: Master Room / Unable to page: "Master Room"
	//   > page PBo=ambiguous          (PBob and PBobby both connected)
	//                                 I'm not sure who you want to page with: PBo / Unable to page: PBo
	//   > page PBob=haven test        (PBob set HAVEN) PBob is not accepting any pages.
	//                                 Unable to page: PBob

	/// <summary>
	/// speech.c:908-910 resolves a recipient with <c>lookup_player</c> and <c>short_page</c>, neither of
	/// which is a room-local match, so a page reaches a connected player wherever they stand.
	/// </summary>
	[Test]
	public async ValueTask Page_ReachesAConnectedPlayerInAnotherRoom()
	{
		var sender = await CreatePlayerAsync("PageRemoteSender");
		var recipient = await CreatePlayerAsync("PageRemoteRecipient");
		try
		{
			await GodCommandAsync($"@teleport/silent {recipient.DbRef}={await DigRoomAsync("PageRemoteRoom")}");

			var senderStart = Notifications.CountFor(sender.DbRef);
			var recipientStart = Notifications.CountFor(recipient.DbRef);
			await PageAsync(sender, recipient.Name, "Are you there?");

			var outgoing = $"You paged {recipient.Name} with 'Are you there?'";
			var incoming = $"{sender.Name} pages: Are you there?";
			await Assert.That(PageMessagesSince(sender.DbRef, senderStart, outgoing)).IsEquivalentTo([outgoing]);
			await Assert.That(PageMessagesSince(recipient.DbRef, recipientStart, incoming)).IsEquivalentTo([incoming]);
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(recipient.Handle);
		}
	}

	/// <summary>speech.c:911-916 and :979-981.</summary>
	[Test]
	public async ValueTask Page_UnknownNameIsReportedAndListedAsUnableToPage()
	{
		var sender = await CreatePlayerAsync("PageUnknownSender");
		try
		{
			var missing = TestIsolationHelpers.GenerateUniqueName("PageNobody");
			var senderStart = Notifications.CountFor(sender.DbRef);
			await PageAsync(sender, missing, "hello");

			await AssertLinesAsync(sender, senderStart,
				$"I can't find who you're trying to page with: {missing}",
				$"Unable to page: {missing}");
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
		}
	}

	/// <summary>
	/// <c>do_page</c> has no <c>MAT_ME</c>: "me" is looked up as a player name like any other, so it
	/// never means the pager.
	/// </summary>
	[Test]
	public async ValueTask Page_MeDoesNotResolveToThePager()
	{
		var sender = await CreatePlayerAsync("PageMeSender");
		try
		{
			var senderStart = Notifications.CountFor(sender.DbRef);
			await PageAsync(sender, "me", "hello self");

			// Only the self-page is pinned: another test's connected player may legitimately answer to
			// the prefix "me", and which of Penn's two misses that produces is world state, not parity.
			var selfPage = $"You paged {sender.Name} with 'hello self'";
			await Assert.That(PageMessagesSince(sender.DbRef, senderStart, selfPage)).IsEmpty();
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
		}
	}

	/// <summary>bsd.c short_page (:6376): a prefix of a connected player's name is enough.</summary>
	[Test]
	public async ValueTask Page_PartialNameMatchesAConnectedPlayer()
	{
		var sender = await CreatePlayerAsync("PagePartialSender");
		var recipient = await CreatePlayerAsync("PagePartialRecipient");
		try
		{
			var senderStart = Notifications.CountFor(sender.DbRef);
			await PageAsync(sender, recipient.Name[..^1], "partial");

			var outgoing = $"You paged {recipient.Name} with 'partial'";
			await Assert.That(PageMessagesSince(sender.DbRef, senderStart, outgoing)).IsEquivalentTo([outgoing]);
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(recipient.Handle);
		}
	}

	/// <summary>bsd.c short_page returns <c>AMBIGUOUS</c> when the prefix fits two connected players.</summary>
	[Test]
	public async ValueTask Page_AmbiguousPrefixIsRefused()
	{
		var sender = await CreatePlayerAsync("PageAmbiguousSender");
		var first = await CreatePlayerAsync("PageAmbiguousTarget");
		var second = await CreatePlayerAsync("PageAmbiguousTarget");
		try
		{
			// Both names start with this and neither equals it, which is exactly short_page's AMBIGUOUS.
			// The trim matters when one unique name happens to be a prefix of the other ("…_1", "…_10"):
			// an exact match wins outright, so the shared run would not be ambiguous at all.
			var shared = string.Concat(first.Name.TakeWhile((c, i) => i < second.Name.Length && second.Name[i] == c));
			var prefix = shared.Length < Math.Min(first.Name.Length, second.Name.Length) ? shared : shared[..^1];
			var senderStart = Notifications.CountFor(sender.DbRef);
			await PageAsync(sender, prefix, "ambiguous");

			await AssertLinesAsync(sender, senderStart,
				$"I'm not sure who you want to page with: {prefix}",
				$"Unable to page: {prefix}");
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(first.Handle);
			await ConnectionService.Disconnect(second.Handle);
		}
	}

	/// <summary>speech.c:927-937 — a player who exists but holds no connection takes no page.</summary>
	[Test]
	public async ValueTask Page_OfflinePlayerIsReportedAsNotConnected()
	{
		var sender = await CreatePlayerAsync("PageOfflineSender");
		try
		{
			var offlineRef = await TestIsolationHelpers.CreateTestPlayerAsync(
				WebAppFactoryArg.Services, Mediator, "PageOfflineTarget");
			var offlineName = (await Mediator.Send(new GetObjectNodeQuery(offlineRef)))
				.Expect<AnySharpObject>().Object().Name;

			var senderStart = Notifications.CountFor(sender.DbRef);
			await PageAsync(sender, offlineName, "offline test");

			await AssertLinesAsync(sender, senderStart,
				$"{offlineName} is not connected.",
				$"Unable to page: {offlineName}");
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
		}
	}

	/// <summary>speech.c:938-943 — the HAVEN wording is "any pages", not "your pages".</summary>
	[Test]
	public async ValueTask Page_HavenRecipientIsNotAcceptingAnyPages()
	{
		var sender = await CreatePlayerAsync("PageHavenSender");
		var recipient = await CreatePlayerAsync("PageHavenRecipient");
		try
		{
			await GodCommandAsync($"@set {recipient.DbRef}=HAVEN");

			var senderStart = Notifications.CountFor(sender.DbRef);
			await PageAsync(sender, recipient.Name, "haven test");

			await AssertLinesAsync(sender, senderStart,
				$"{recipient.Name} is not accepting any pages.",
				$"Unable to page: {recipient.Name}");
		}
		finally
		{
			await GodCommandAsync($"@set {recipient.DbRef}=!HAVEN");
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(recipient.Handle);
		}
	}

	/// <summary>
	/// speech.c:979-981 — one <c>Unable to page:</c> line for the whole scan, and the good recipients
	/// are paged all the same.
	/// </summary>
	[Test]
	public async ValueTask Page_MixedGoodAndBadNamesStillPagesTheGoodOne()
	{
		var sender = await CreatePlayerAsync("PageMixedSender");
		var recipient = await CreatePlayerAsync("PageMixedRecipient");
		try
		{
			var missing = TestIsolationHelpers.GenerateUniqueName("PageMixedNobody");
			var senderStart = Notifications.CountFor(sender.DbRef);
			await PageAsync(sender, $"{recipient.Name} {missing}", "mixed");

			await AssertLinesAsync(sender, senderStart,
				$"I can't find who you're trying to page with: {missing}",
				$"Unable to page: {missing}",
				$"You paged {recipient.Name} with 'mixed'");
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(recipient.Handle);
		}
	}

	/// <summary>
	/// strutil.c <c>next_in_list</c> takes a quoted name whole, and <c>safe_str_space</c> (:956) puts it
	/// back in quotes in the <c>Unable to page:</c> line.
	/// </summary>
	[Test]
	public async ValueTask Page_QuotedNameWithASpaceStaysOneNameAndIsRequoted()
	{
		var sender = await CreatePlayerAsync("PageQuotedSender");
		try
		{
			var missing = $"{TestIsolationHelpers.GenerateUniqueName("PageQuoted")} Person";
			var senderStart = Notifications.CountFor(sender.DbRef);
			await PageAsync(sender, $"\"{missing}\"", "quoted");

			await AssertLinesAsync(sender, senderStart,
				$"I can't find who you're trying to page with: {missing}",
				$"Unable to page: \"{missing}\"");
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
		}
	}

	/// <summary>
	/// <c>lookup_player</c> refuses anything that is not a player and <c>short_page</c> only ever walks
	/// connected players, so a thing the pager is carrying — which a room-local match would find first —
	/// is not a recipient.
	/// </summary>
	[Test]
	public async ValueTask Page_DoesNotMatchANonPlayerTheSenderIsCarrying()
	{
		var sender = await CreatePlayerAsync("PageThingSender");
		try
		{
			var thingName = TestIsolationHelpers.GenerateUniqueName("PageThing");
			var thing = (await TestIsolationHelpers.CreateObjectCommandAsync(
				WebAppFactoryArg.CommandParser, ConnectionService, thingName)).Message.ToPlainText().Trim();
			await GodCommandAsync($"@teleport/silent {thing}={sender.DbRef}");

			var senderStart = Notifications.CountFor(sender.DbRef);
			await PageAsync(sender, thingName, "object");

			await AssertLinesAsync(sender, senderStart,
				$"I can't find who you're trying to page with: {thingName}",
				$"Unable to page: {thingName}");
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
		}
	}

	private async Task AssertLinesAsync(PagePlayer receiver, int start, params string[] expected)
	{
		var messages = PageMessagesSince(receiver.DbRef, start, expected);

		await Assert.That(messages.Length).IsEqualTo(expected.Length);
		for (var i = 0; i < expected.Length; i++)
		{
			await Assert.That(messages[i]).IsEqualTo(expected[i]);
		}
	}

	private async Task<string> DigRoomAsync(string prefix)
	{
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		var dug = await WebAppFactoryArg.CommandParser.CommandParse(
			1, ConnectionService, MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));

		return dug.Message.ToPlainText().Trim();
	}

	private async Task<PagePlayer> CreatePlayerAsync(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		var playerObject = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<AnySharpObject>();

		return new PagePlayer(player.DbRef, player.Handle, playerObject, playerObject.Object().Name);
	}

	/// <summary>
	/// A page to oneself, by name. <c>do_page</c> has no <c>MAT_ME</c> — see
	/// <see cref="Page_MeDoesNotResolveToThePager"/> — so this is how PennMUSH spells it:
	/// <c>page One=self by name</c> echoes both the outgoing and the incoming line.
	/// </summary>
	private async Task<string[]> PageSelfAsync(PagePlayer player, string message, params string[] selectedMessages)
	{
		var start = Notifications.CountFor(player.DbRef);
		await PageAsync(player, player.Name, message);

		return PageMessagesSince(player.DbRef, start, selectedMessages);
	}

	private string[] PageMessagesSince(DBRef recipient, int start, params string[] selectedMessages)
		=> [.. Notifications.For(recipient)
			.Skip(start)
			.Where(message => selectedMessages.Contains(message, StringComparer.Ordinal))];

	private async Task PageAsync(PagePlayer sender, string recipients, string message)
		=> await CommandAsync(sender, $"page {recipients}={message}");

	private async Task CommandAsync(PagePlayer sender, string command)
	{
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		var parser = WebAppFactoryArg.CommandParserFor(sender.DbRef, sender.Handle);
		await parser.CommandParse(sender.Handle, ConnectionService, MarkupText.Plain(command));
	}

	private async Task GodCommandAsync(string command)
	{
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		await WebAppFactoryArg.CommandParser.CommandParse(
			1, ConnectionService, MarkupText.Plain(command));
	}

	private sealed record PagePlayer(DBRef DbRef, long Handle, AnySharpObject Object, string Name);
}
