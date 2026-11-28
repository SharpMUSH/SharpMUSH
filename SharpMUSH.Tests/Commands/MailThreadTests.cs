using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Mail conversations: every copy of one send carries one message id and one thread id, and a reply carries the
/// thread of what it answers. <c>@mail/reply</c>, <c>/replyall</c> and <c>/quote</c> are TinyMUX's; a message number
/// in the recipient list is PennMUSH's own reply (<c>do_mail_send</c>, <c>extmail.c:1359</c>).
/// </summary>
public class MailThreadTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	/// <summary>A player in a room of its own, so it hears nothing another test says in a shared room.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName($"{prefix}Room");
		var digResult = await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var room = DBRef.Parse(digResult.Message.ToPlainText()!.Trim());

		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, room);
	}

	private async Task<CallState> Run(TestIsolationHelpers.TestPlayer who, string command)
		=> await WebAppFactoryArg.CommandParserFor(who.DbRef, who.Handle)
			.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command));

	private async Task<List<string>> Heard(TestIsolationHelpers.TestPlayer who, Func<Task> action)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who.DbRef);
		await action();
		return [.. WebAppFactoryArg.Notifications.For(who.DbRef).Skip(before)];
	}

	private async Task<string> Evaluate(TestIsolationHelpers.TestPlayer who, string expression)
		=> (await WebAppFactoryArg.CommandParserFor(who.DbRef, who.Handle)
			.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	private async Task<SharpMail[]> Mailbox(TestIsolationHelpers.TestPlayer who)
	{
		var player = (await Mediator.Send(new GetObjectNodeQuery(who.DbRef))).Expect<SharpPlayer>();
		return await Mediator.CreateStream(new GetMailListQuery(player, "INBOX")).ToArrayAsync();
	}

	[Test]
	public async ValueTask EveryCopyOfOneSendSharesOneMessageAndThread()
	{
		var sender = await Player("MtShare");
		var first = await Player("MtShareA");
		var second = await Player("MtShareB");

		await Run(sender, $"@mail {first.Name} {second.Name}=Plans/Meet at noon.");

		var a = (await Mailbox(first)).Single();
		var b = (await Mailbox(second)).Single();
		await Assert.That(a.Message).IsNotNull();
		await Assert.That(b.Message).IsEqualTo(a.Message);
		await Assert.That(a.Thread).IsEqualTo(a.Message);
		await Assert.That(a.ReplyTo).IsNull();
		await Assert.That(a.Addressed).IsEquivalentTo([first.DbRef.ToString(), second.DbRef.ToString()]);
		await Assert.That(await Evaluate(first, "mailthread(1)")).IsEqualTo(a.Thread.ToString());
	}

	[Test]
	public async ValueTask TwoSendsAreTwoConversations()
	{
		var sender = await Player("MtTwo");
		var target = await Player("MtTwoTo");

		await Run(sender, $"@mail {target.Name}=One/First.");
		await Run(sender, $"@mail {target.Name}=Two/Second.");

		var mail = await Mailbox(target);
		await Assert.That(mail[1].Thread).IsNotEqualTo(mail[0].Thread);
	}

	[Test]
	public async ValueTask ReplyGoesToTheSenderUnderReInTheSameThread()
	{
		var sender = await Player("MtReply");
		var target = await Player("MtReplyTo");
		await Run(sender, $"@mail {target.Name}=Dinner/Are you coming?");
		var original = (await Mailbox(target)).Single();

		var heard = await Heard(target, () => Run(target, "@mail/reply 1=Yes, I'll be there."));

		await Assert.That(heard).Contains($"MAIL: You sent your message to {sender.Name}.");
		var reply = (await Mailbox(sender)).Single();
		await Assert.That(reply.Subject.ToPlainText()).IsEqualTo("Re: Dinner");
		await Assert.That(reply.Content.ToPlainText()).IsEqualTo("Yes, I'll be there.");
		await Assert.That(reply.Thread).IsEqualTo(original.Thread);
		await Assert.That(reply.ReplyTo).IsEqualTo(original.Message);
		await Assert.That(reply.Message).IsNotEqualTo(original.Message);
	}

	[Test]
	public async ValueTask ReplyToAReplyKeepsOneRe()
	{
		var sender = await Player("MtReRe");
		var target = await Player("MtReReTo");
		await Run(sender, $"@mail {target.Name}=Dinner/Are you coming?");
		await Run(target, "@mail/reply 1=Yes.");

		await Run(sender, "@mail/reply 1=Good.");

		var mail = await Mailbox(target);
		await Assert.That(mail[1].Subject.ToPlainText()).IsEqualTo("Re: Dinner");
		await Assert.That(mail[1].Thread).IsEqualTo(mail[0].Thread);
	}

	/// <summary>
	/// PennMUSH's reply: a message number in the list mails that message's sender, under its subject marked
	/// <c>Re: </c> (<c>extmail.c:1375</c>), unless the send gives a subject of its own (<c>extmail.c:1373</c>).
	/// </summary>
	[Test]
	public async ValueTask AMessageNumberInTheListRepliesAsPennMushDoes()
	{
		var sender = await Player("MtPenn");
		var target = await Player("MtPennTo");
		await Run(sender, $"@mail {target.Name}=Dinner/Are you coming?");
		var original = (await Mailbox(target)).Single();

		await Run(target, "@mail 1=Yes.");
		await Run(target, "@mail 1=Menu/What are we eating?");

		var replies = await Mailbox(sender);
		await Assert.That(replies[0].Subject.ToPlainText()).IsEqualTo("Re: Dinner");
		await Assert.That(replies[0].Content.ToPlainText()).IsEqualTo("Yes.");
		await Assert.That(replies[0].Thread).IsEqualTo(original.Thread);
		await Assert.That(replies[1].Subject.ToPlainText()).IsEqualTo("Menu");
		await Assert.That(replies[1].Thread).IsEqualTo(original.Thread);
	}

	/// <summary><c>extmail.c:1369</c> — a number naming no message stops the send there.</summary>
	[Test]
	public async ValueTask AMessageNumberNamingNothingIsRefused()
	{
		var player = await Player("MtPennNone");

		var heard = await Heard(player, () => Run(player, "@mail 7=Hello?"));

		await Assert.That(heard).Contains("MAIL: You can't reply to nonexistent mail.");
	}

	[Test]
	public async ValueTask ReplyAllReachesEveryoneTheSenderAddressedButTheReplier()
	{
		var sender = await Player("MtAll");
		var first = await Player("MtAllA");
		var second = await Player("MtAllB");
		await Run(sender, $"@mail {first.Name} {second.Name}=Plans/Meet at noon.");
		var original = (await Mailbox(first)).Single();

		await Run(first, "@mail/replyall 1=Noon works.");

		var toSender = (await Mailbox(sender)).Single();
		var toSecond = await Mailbox(second);
		await Assert.That(toSecond).Count().IsEqualTo(2);
		await Assert.That(toSecond[1].Message).IsEqualTo(toSender.Message);
		await Assert.That(toSender.Thread).IsEqualTo(original.Thread);
		await Assert.That(await Mailbox(first)).Count().IsEqualTo(1)
			.Because("the replier was addressed, but a reply to everyone does not mail its own writer");
	}

	[Test]
	public async ValueTask ReplyAllMailsAnAliasAsTheAlias()
	{
		var sender = await Player("MtAlias");
		var member = await Player("MtAliasM");
		var other = await Player("MtAliasO");
		var alias = $"+mt{Guid.NewGuid().ToString("N")[..6]}";
		await Run(sender, $"@malias {alias}=#{member.DbRef.Number} #{other.DbRef.Number}");
		await Run(sender, $"@mail {alias}=Staff/Meeting moved.");
		var original = (await Mailbox(member)).Single();
		await Assert.That(original.Addressed).IsEquivalentTo([alias]);

		await Run(member, "@mail/replyall 1=Thanks.");

		await Assert.That(await Mailbox(other)).Count().IsEqualTo(2);
		await Assert.That((await Mailbox(sender)).Single().Addressed).Contains(alias);
	}

	[Test]
	public async ValueTask QuoteAddsTheOriginalBelowTheReply()
	{
		var sender = await Player("MtQuote");
		var target = await Player("MtQuoteTo");
		await Run(sender, $"@mail {target.Name}=Dinner/Are you coming?");

		await Run(target, "@mail/reply/quote 1=Yes.");

		var text = (await Mailbox(sender)).Single().Content.ToPlainText();
		await Assert.That(text).StartsWith("Yes.\n\nOn ");
		await Assert.That(text).Contains($", {sender.Name} wrote:\n> Are you coming?");
	}

	[Test]
	public async ValueTask ReplyToAnonymousMailIsRefused()
	{
		var sender = await Player("MtAnon");
		var target = await Player("MtAnonTo");
		await Run(sender, $"@mail {target.Name}=Secret/Guess who.");
		var mail = (await Mailbox(target)).Single();
		await Mediator.Send(new SharpMUSH.Library.Commands.Database.DeleteMailCommand(mail));
		await Mediator.Send(new SharpMUSH.Library.Commands.Database.SendMailCommand(
			(await Mediator.Send(new GetObjectNodeQuery(sender.DbRef))).Expect<SharpPlayer>().Object,
			(await Mediator.Send(new GetObjectNodeQuery(target.DbRef))).Expect<SharpPlayer>(),
			new SharpMail
			{
				DateSent = DateTimeOffset.UtcNow, Fresh = true, Read = false, Tagged = false, Urgent = false, Forwarded = false,
				Cleared = false, Folder = "INBOX", Content = MarkupText.Plain("Guess who."), Subject = MarkupText.Plain("Secret"),
				Anonymous = true, From = new DotNext.Threading.AsyncLazy<AnyOptionalSharpObject>(_ => Task.FromResult(new AnyOptionalSharpObject(new None())))
			}));

		var heard = await Heard(target, () => Run(target, "@mail/reply 1=Who is this?"));

		await Assert.That(heard).Contains("MAIL: You can't reply to anonymous mail.");
		await Assert.That(await Mailbox(sender)).IsEmpty();
	}

	/// <summary>A message's id names it however the mailbox is renumbered.</summary>
	[Test]
	public async ValueTask ReplyTakesTheMessageIdInPlaceOfItsNumber()
	{
		var first = await Player("MtById");
		var second = await Player("MtByIdB");
		var target = await Player("MtByIdTo");
		await Run(first, $"@mail {target.Name}=Old/First.");
		await Run(second, $"@mail {target.Name}=Plans/Meet at noon.");
		var answered = (await Mailbox(target))[1];
		await Run(target, "@mail/clear 1");
		await Run(target, "@mail/purge");

		await Run(target, $"@mail/reply {answered.Message}=Noon works.");

		await Assert.That(await Mailbox(first)).IsEmpty();
		var reply = (await Mailbox(second)).Single();
		await Assert.That(reply.ReplyTo).IsEqualTo(answered.Message);
		await Assert.That(reply.Subject.ToPlainText()).IsEqualTo("Re: Plans");
	}

	[Test]
	public async ValueTask ReplyNamesOneMessage()
	{
		var sender = await Player("MtOne");
		var target = await Player("MtOneTo");
		await Run(sender, $"@mail {target.Name}=A/First.");
		await Run(sender, $"@mail {target.Name}=B/Second.");

		var heard = await Heard(target, () => Run(target, "@mail/reply 1-2=Both?"));

		await Assert.That(heard).Contains("MAIL: You can only reply to one message at a time.");
		await Assert.That(await Mailbox(sender)).IsEmpty();
	}

	[Test]
	public async ValueTask ReplyTakesNoSendSwitchesItCannotUse()
	{
		var player = await Player("MtSwitch");

		var heard = await Heard(player, () => Run(player, "@mail/reply/quick 1=Hi"));

		await Assert.That(heard).Contains("Error: Too many switches passed to @mail.");
	}

	[Test]
	public async ValueTask ThreadListsTheConversationTheReaderCanSee()
	{
		var sender = await Player("MtList");
		var first = await Player("MtListA");
		var second = await Player("MtListB");
		await Run(sender, $"@mail {first.Name} {second.Name}=Plans/Meet at noon.");
		await Run(first, "@mail/reply 1=Noon works.");
		await Run(second, "@mail/reply 1=Can't make it.");

		var heard = string.Join("\n", await Heard(first, () => Run(first, "@mail/thread 1")));

		await Assert.That(heard).Contains("Mail thread: Plans");
		await Assert.That(heard).Contains("2 messages.");
		await Assert.That(heard).DoesNotContain("3 messages.")
			.Because("the second recipient's reply went to the sender only");

		var senderView = string.Join("\n", await Heard(sender, () => Run(sender, "@mail/thread 1")));
		await Assert.That(senderView).Contains("3 messages.");
	}

	[Test]
	public async ValueTask ThreadNamesWhoEachMessageWasAddressedTo()
	{
		var sender = await Player("MtTo");
		var first = await Player("MtToA");
		var second = await Player("MtToB");
		await Run(sender, $"@mail {first.Name} {second.Name}=Plans/Meet at noon.");
		await Run(first, "@mail/reply 1=Noon works.");
		var reader = (await Mediator.Send(new GetObjectNodeQuery(first.DbRef))).Expect<SharpPlayer>();
		var thread = (await Mailbox(first)).Single().Thread!.Value;

		var messages = await SharpMUSH.Implementation.Commands.MailCommand.MailThreads.ReadAsync(
			WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>(), Mediator, reader, thread);

		await Assert.That(messages).Count().IsEqualTo(2);
		await Assert.That(messages[0].To).IsEquivalentTo([first.Name, second.Name]);
		await Assert.That(messages[0].Position?.ToString()).IsEqualTo("0:1");
		await Assert.That(messages[1].To).IsEquivalentTo([sender.Name]);
		await Assert.That(messages[1].Sent).IsTrue();
		await Assert.That(messages[1].Position).IsNull();
	}

	/// <summary>
	/// Two people step aside with plain replies and come back with a reply to everyone: each reply sits under the
	/// message it answers, and the person left out sees only what they were sent.
	/// </summary>
	[Test]
	public async ValueTask ThreadShowsEachReplyUnderTheMessageItAnswers()
	{
		var ann = await Player("MtTreeAnn");
		var bob = await Player("MtTreeBob");
		var cat = await Player("MtTreeCat");
		await Run(ann, $"@mail {bob.Name} {cat.Name}=Plans/Meet at noon.");
		await Run(bob, "@mail/reply 1=Want to go early, just us?");
		await Run(ann, "@mail/reply 1=Yes, eleven.");
		await Run(bob, "@mail/replyall 1=Noon works for both of us.");
		var reader = (await Mediator.Send(new GetObjectNodeQuery(bob.DbRef))).Expect<SharpPlayer>();
		var outsider = (await Mediator.Send(new GetObjectNodeQuery(cat.DbRef))).Expect<SharpPlayer>();
		var thread = (await Mailbox(bob)).First().Thread!.Value;
		var data = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();

		var bobView = await SharpMUSH.Implementation.Commands.MailCommand.MailThreads.ReadAsync(data, Mediator, reader, thread);
		var catView = await SharpMUSH.Implementation.Commands.MailCommand.MailThreads.ReadAsync(data, Mediator, outsider, thread);

		await Assert.That(bobView.Select(message => (message.First.Content.ToPlainText(), message.Depth))).IsEquivalentTo(
			[("Meet at noon.", 0), ("Want to go early, just us?", 1), ("Yes, eleven.", 2), ("Noon works for both of us.", 1)],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(catView.Select(message => (message.First.Content.ToPlainText(), message.Depth))).IsEquivalentTo(
			[("Meet at noon.", 0), ("Noon works for both of us.", 1)], TUnit.Assertions.Enums.CollectionOrdering.Matching);

		var heard = string.Join("\n", await Heard(bob, () => Run(bob, "@mail/thread 1")));
		await Assert.That(heard).Contains("Each reply is under the message it answers.");
		await Assert.That(heard).Contains($"└─ 0:2 {ann.Name} to {bob.Name}");
	}

	/// <summary>A leading + names a mail alias, never a message to reply to.</summary>
	[Test]
	public async ValueTask APlusNumberInTheListIsAnAlias()
	{
		var sender = await Player("MtPlus");
		var target = await Player("MtPlusTo");
		await Run(target, $"@mail {sender.Name}=Hello/First.");

		var heard = await Heard(sender, () => Run(sender, "@mail +1=Not a reply/Hi."));

		await Assert.That(heard).DoesNotContain($"MAIL: You sent your message to {target.Name}.");
		await Assert.That(await Mailbox(target)).IsEmpty();
	}

	[Test]
	public async ValueTask AReplyThatCannotBeSentIsNotSentAsOrdinaryMail()
	{
		var player = await Player("MtNoBody");
		var other = await Player("MtNoBodyTo");
		await Run(other, $"@mail {player.Name}=Hello/First.");

		var heard = await Heard(player, () => Run(player, $"@mail/reply/silent {other.Name}="));

		await Assert.That(await Mailbox(other)).IsEmpty();
		await Assert.That(heard).IsNotEmpty();
	}

	[Test]
	public async ValueTask ThreadTakesTheThreadId()
	{
		var sender = await Player("MtGuid");
		var target = await Player("MtGuidTo");
		await Run(sender, $"@mail {target.Name}=Plans/Meet at noon.");
		var thread = await Evaluate(target, "mailthread(1)");

		var heard = string.Join("\n", await Heard(sender, () => Run(sender, $"@mail/thread {thread}")));

		await Assert.That(heard).Contains("Mail thread: Plans");
		await Assert.That(heard).Contains($"Thread {thread}");
	}

	[Test]
	public async ValueTask ThreadShowsNothingOfAConversationTheReaderIsNotIn()
	{
		var sender = await Player("MtPrivate");
		var target = await Player("MtPrivateTo");
		var outsider = await Player("MtPrivateOut");
		await Run(sender, $"@mail {target.Name}=Private/Just us.");
		var thread = await Evaluate(target, "mailthread(1)");

		var heard = await Heard(outsider, () => Run(outsider, $"@mail/thread {thread}"));

		await Assert.That(heard).Contains("MAIL: You have no mail in that conversation.");
	}

	[Test]
	public async ValueTask ForwardStartsAConversationOfItsOwn()
	{
		var sender = await Player("MtFwd");
		var target = await Player("MtFwdTo");
		var onward = await Player("MtFwdOn");
		await Run(sender, $"@mail {target.Name}=Plans/Meet at noon.");

		await Run(target, $"@mail/fwd 1={onward.Name}");

		var original = (await Mailbox(target)).Single();
		var forward = (await Mailbox(onward)).Single();
		await Assert.That(forward.Thread).IsNotNull();
		await Assert.That(forward.Thread).IsNotEqualTo(original.Thread);
		await Assert.That(forward.Addressed).IsEquivalentTo([onward.DbRef.ToString()])
			.Because("a reply to everyone on a forward reaches whoever it was forwarded to");
	}
}
