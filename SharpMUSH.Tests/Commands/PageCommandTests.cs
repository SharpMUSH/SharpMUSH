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
			var messages = await PageSelfAsync(player, "Test");

			await Assert.That(messages.Length).IsEqualTo(2);
			await Assert.That(messages[0]).IsEqualTo($"You paged {player.Name} with 'Test'");
			await Assert.That(messages[1]).IsEqualTo($"{player.Name} pages: Test");
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
			var messages = await PageSelfAsync(player, ":Test");

			await Assert.That(messages.Length).IsEqualTo(2);
			await Assert.That(messages[0]).IsEqualTo($"Long distance to {player.Name}: {player.Name} Test");
			await Assert.That(messages[1]).IsEqualTo($"From afar, {player.Name} Test");
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
			var messages = await PageSelfAsync(player, ";Test");

			await Assert.That(messages.Length).IsEqualTo(2);
			await Assert.That(messages[0]).IsEqualTo($"Long distance to {player.Name}: {player.Name}Test");
			await Assert.That(messages[1]).IsEqualTo($"From afar, {player.Name}Test");
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

			await Assert.That(Notifications.For(recipient.DbRef).Skip(recipientStart)).IsEquivalentTo([
				$"IN:Test|:||{recipientDbRef}|From afar, {sender.Name} Test"
			]);
			await Assert.That(Notifications.For(sender.DbRef).Skip(senderStart)).IsEquivalentTo([
				$"OUT:Test|:||{recipientDbRef}|Long distance to {recipient.Name}: {sender.Name} Test"
			]);
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
			await Assert.That(Notifications.For(sender.DbRef).Skip(senderStart)).IsEquivalentTo([
				$"You paged {recipientList} with 'Test'"
			]);
			await Assert.That(Notifications.For(first.DbRef).Skip(firstStart)).IsEquivalentTo([
				$"{sender.Name} pages {recipientList}: Test"
			]);
			await Assert.That(Notifications.For(second.DbRef).Skip(secondStart)).IsEquivalentTo([
				$"{sender.Name} pages {recipientList}: Test"
			]);
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

			var messages = await PageSelfAsync(player, "Test");

			await Assert.That(messages.Length).IsEqualTo(2);
			await Assert.That(messages[0]).IsEqualTo($"OUT:{player.DbRef}");
			await Assert.That(messages[1]).IsEqualTo($"IN:{player.DbRef}");
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
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
			await Assert.That(Notifications.For(sender.DbRef).Skip(secondPageStart)).IsEquivalentTo([
				$"OUT:{second.DbRef}"
			]);

			var senderRepageStart = Notifications.CountFor(sender.DbRef);
			var firstRepageStart = Notifications.CountFor(first.DbRef);
			var secondRepageStart = Notifications.CountFor(second.DbRef);
			await PageAsync(sender, string.Empty, "Again");

			await Assert.That(Notifications.For(sender.DbRef).Skip(senderRepageStart)).IsEquivalentTo([
				$"OUT:{second.DbRef}"
			]);
			await Assert.That(Notifications.For(first.DbRef).Skip(firstRepageStart)).IsEmpty();
			await Assert.That(Notifications.For(second.DbRef).Skip(secondRepageStart)).IsEquivalentTo([
				$"{sender.Name} pages: Again"
			]);
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

			await Assert.That(Notifications.For(sender.DbRef).Skip(senderStart)).IsEquivalentTo([
				$"You last paged {second.Name}."
			]);
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(first.Handle);
			await ConnectionService.Disconnect(second.Handle);
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

			var messages = Notifications.For(recipient.DbRef).Skip(recipientStart).ToArray();
			await Assert.That(messages.Length).IsEqualTo(1);
			await Assert.That(messages[0]).IsEqualTo(
				$"IN:#{recipient.DbRef.Number}|#{recipient.DbRef.Number}|#{sender.DbRef.Number}");
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
		var playerObject = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Known;

		return new PagePlayer(player.DbRef, player.Handle, playerObject, playerObject.Object().Name);
	}

	private async Task<string[]> PageSelfAsync(PagePlayer player, string message)
	{
		var start = Notifications.CountFor(player.DbRef);
		await PageAsync(player, "me", message);

		return [.. Notifications.For(player.DbRef).Skip(start)];
	}

	private async Task PageAsync(PagePlayer sender, string recipients, string message)
		=> await CommandAsync(sender, $"page {recipients}={message}");

	private async Task CommandAsync(PagePlayer sender, string command)
	{
		var parser = WebAppFactoryArg.CommandParserFor(sender.DbRef, sender.Handle);
		await parser.CommandParse(sender.Handle, ConnectionService, MarkupText.Plain(command));
	}

	private sealed record PagePlayer(DBRef DbRef, long Handle, AnySharpObject Object, string Name);
}
