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

	private async Task<PagePlayer> CreatePlayerAsync(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		var playerObject = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<AnySharpObject>();

		return new PagePlayer(player.DbRef, player.Handle, playerObject, playerObject.Object().Name);
	}

	private async Task<string[]> PageSelfAsync(PagePlayer player, string message, params string[] selectedMessages)
	{
		var start = Notifications.CountFor(player.DbRef);
		await PageAsync(player, "me", message);

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
