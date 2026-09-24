using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.Json;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The delivery half of <c>@mail</c>, <c>mailsend()</c> and <c>@mail/fwd</c>: PennMUSH's <c>send_mail</c>
/// (<c>extmail.c:1472</c>) and <c>real_send_mail</c> (<c>extmail.c:1557</c>). Every sender here is a
/// mortal unless the test is about privilege, because God passes the mail lock unconditionally.
/// The expected texts were captured from a live PennMUSH (tools/oracle) on 2026-09-22.
/// </summary>
public class MailDeliveryTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private async Task<CallState> Run(TestIsolationHelpers.TestPlayer who, string command)
		=> await WebAppFactoryArg.CommandParserFor(who.DbRef, who.Handle)
			.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command));

	private Task God(string command)
		=> GodParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	private async Task<List<string>> Heard(TestIsolationHelpers.TestPlayer who, Func<Task> action)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who.DbRef);
		await action();
		return [.. WebAppFactoryArg.Notifications.For(who.DbRef).Skip(before)];
	}

	private async Task<SharpMail[]> Mailbox(TestIsolationHelpers.TestPlayer who, string folder = "INBOX")
	{
		var player = (await Mediator.Send(new GetObjectNodeQuery(who.DbRef))).Expect<SharpPlayer>();
		return await Mediator.CreateStream(new GetMailListQuery(player, folder)).ToArrayAsync();
	}

	private async Task<string> Get(DBRef obj, string attribute)
		=> (await GodParser.FunctionParse(MarkupText.Plain($"[get(#{obj.Number}/{attribute})]")))!.Message!.ToPlainText();

	/// <summary>
	/// <c>do_mail_fwd</c> delivers through <c>send_mail</c> (<c>extmail.c:1296</c>), so the recipient hears
	/// about a forwarded message as about any other. The forward used to store the mail and say nothing.
	/// </summary>
	[Test]
	public async ValueTask ForwardedMailNotifiesTheRecipient()
	{
		var sender = await Player("MdFwdNote");
		var target = await Player("MdFwdNoteTo");
		await Run(sender, "@mail me=Original/Forward me.");

		var heard = await Heard(target, () => Run(sender, $"@mail/fwd 1=#{target.DbRef.Number}"));

		await Assert.That(heard).Contains(m => m.StartsWith($"MAIL: You have a new message (1) from {sender.Name}."));
	}

	/// <summary>
	/// <c>real_send_mail</c> builds a new message (<c>extmail.c:1609</c>): a forward reaches its recipient
	/// unread whatever the forwarder had done with their own copy. The forward used to send the fetched
	/// record itself, read flag and all.
	/// </summary>
	[Test]
	public async ValueTask ForwardedMailArrivesUnread()
	{
		var sender = await Player("MdFwdRead");
		var target = await Player("MdFwdReadTo");
		await Run(sender, "@mail me=Seen/Already read.");
		await Run(sender, "@mail/read 1");

		await Run(sender, $"@mail/fwd 1=#{target.DbRef.Number}");

		var mail = await Mailbox(target);
		await Assert.That(mail).Count().IsEqualTo(1);
		await Assert.That(mail[0].Read).IsFalse();
		await Assert.That(mail[0].Forwarded).IsTrue();
		await Assert.That(mail[0].Subject.ToPlainText()).IsEqualTo("Fwd: Seen");
	}

	/// <summary><c>extmail.c:1621</c> — the prefix is added only if the subject does not already carry it.</summary>
	[Test]
	public async ValueTask ForwardingAForwardDoesNotStackThePrefix()
	{
		var sender = await Player("MdFwdPfx");
		var target = await Player("MdFwdPfxTo");
		await Run(sender, "@mail me=Fwd: Chain/Body.");

		await Run(sender, $"@mail/fwd 1=#{target.DbRef.Number}");

		await Assert.That((await Mailbox(target))[0].Subject.ToPlainText()).IsEqualTo("Fwd: Chain");
	}

	/// <summary>
	/// A forward goes through the recipient's mail lock like a send (<c>extmail.c:1578</c>), with the same
	/// refusal the sender would see for a send.
	/// </summary>
	[Test]
	public async ValueTask ForwardingToALockedRecipientIsRefused()
	{
		var sender = await Player("MdFwdLock");
		var target = await Player("MdFwdLockTo");
		await Run(target, "@lock/mail me=#0");
		await Run(sender, "@mail me=Locked/Body.");

		var heard = await Heard(sender, () => Run(sender, $"@mail/fwd 1=#{target.DbRef.Number}"));

		await Assert.That(heard).Contains($"MAIL: {target.Name} is not accepting mail from you right now.");
		await Assert.That(await Mailbox(target)).IsEmpty();
	}

	/// <summary>
	/// <c>real_send_mail</c> refuses through <c>fail_lock</c> (<c>extmail.c:1587</c>), so the default is
	/// Penn's wording and a <c>MAIL_LOCK`FAILURE</c> on the recipient replaces it. Captured:
	/// <c>MAIL: LC127 is not accepting mail from you right now.</c>, then <c>custom mail failure</c>.
	/// </summary>
	[Test]
	public async ValueTask ALockedMailboxRefusesThroughTheLocksFailureMessage()
	{
		var sender = await Player("MdLockMsg");
		var target = await Player("MdLockMsgTo");
		await Run(target, "@lock/mail me=#0");

		var plain = await Heard(sender, () => Run(sender, $"@mail #{target.DbRef.Number}=Locked/Body."));
		await Run(target, "&MAIL_LOCK`FAILURE me=custom mail failure");
		var custom = await Heard(sender, () => Run(sender, $"@mail #{target.DbRef.Number}=Locked/Body."));

		await Assert.That(plain).Contains($"MAIL: {target.Name} is not accepting mail from you right now.");
		await Assert.That(custom).Contains("custom mail failure");
		await Assert.That(custom).DoesNotContain(m => m.Contains("is not accepting mail"));
		await Assert.That(await Mailbox(target)).IsEmpty();
	}

	/// <summary>
	/// <c>Hasprivs(player) || eval_lock(...)</c> (<c>extmail.c:1578</c>): a wizard's mail is not stopped by
	/// the lock. Captured: One's mail was delivered to a mail-locked mortal.
	/// </summary>
	[Test]
	public async ValueTask AWizardSenderPassesTheMailLock()
	{
		var wizard = await Player("MdLockWiz");
		var target = await Player("MdLockWizTo");
		await God($"@set #{wizard.DbRef.Number}=WIZARD");
		await Run(target, "@lock/mail me=#0");

		await Run(wizard, $"@mail #{target.DbRef.Number}=Past the lock/Body.");

		await Assert.That(await Mailbox(target)).Count().IsEqualTo(1);
	}

	/// <summary>
	/// <c>extmail.c:1680</c> — the confirmation warns when the recipient's reply would bounce off the
	/// sender's own mail lock. Captured: <c>MAIL: You sent your message to LC127, but they can't mail you!</c>
	/// </summary>
	[Test]
	public async ValueTask TheSenderIsWarnedWhenTheRecipientCannotReply()
	{
		var sender = await Player("MdNoReply");
		var target = await Player("MdNoReplyTo");
		await Run(sender, "@lock/mail me=#0");

		var heard = await Heard(sender, () => Run(sender, $"@mail #{target.DbRef.Number}=One way/Body."));

		await Assert.That(heard).Contains($"MAIL: You sent your message to {target.Name}, but they can't mail you!");
	}

	/// <summary><c>extmail.c:1570</c> — a message reading only <c>clear</c> is taken as a typo for <c>@mail/clear</c>.</summary>
	[Test]
	public async ValueTask AMessageSayingOnlyClearIsNotSent()
	{
		var sender = await Player("MdClear");

		var heard = await Heard(sender, () => Run(sender, "@mail me=clear"));

		await Assert.That(heard).Contains("MAIL: You probably don't wanna send mail saying 'clear'.");
		await Assert.That(await Mailbox(sender)).IsEmpty();
	}

	/// <summary>
	/// <c>extmail.c:1700</c> — <c>AMAIL</c> runs only for a privileged recipient, never for mail to oneself,
	/// and through <c>did_it</c>, so it is queued. Captured: a mortal's AMAIL (set by One) stayed silent,
	/// the same player's fired once they were ROYALTY, and self-mail did not fire it.
	/// </summary>
	[Test]
	public async ValueTask AmailRunsOnlyForAPrivilegedRecipient()
	{
		using var _ = TestOptionsOverride.Scope(options => options with
		{
			Attribute = options.Attribute with { AMail = true }
		});

		var sender = await Player("MdAmail");
		var mortal = await Player("MdAmailMortal");
		var royal = await Player("MdAmailRoyal");
		await God($"@set #{royal.DbRef.Number}=ROYALTY");
		await God($"&AMAIL #{mortal.DbRef.Number}=&AMAILED me=%#");
		await God($"&AMAIL #{royal.DbRef.Number}=&AMAILED me=%#");

		await Run(sender, $"@mail #{mortal.DbRef.Number}=Hello/Body.");
		await Run(sender, $"@mail #{royal.DbRef.Number}=Hello/Body.");
		await Run(royal, "@mail me=Self/Body.");
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(await Get(mortal.DbRef, "AMAILED")).IsEqualTo(string.Empty);
		await Assert.That(await Get(royal.DbRef, "AMAILED")).StartsWith($"#{sender.DbRef.Number}");
	}

	/// <summary>
	/// <c>send_mail</c> (<c>extmail.c:1483</c>): a recipient with a <c>MAILFORWARDLIST</c> is not
	/// delivered to; each listed player that passes <c>Can_MailForward</c> is, and the sender is told the
	/// message went to the recipient they named. Captured: B locked <c>@lock/mailforward me=*A</c>, A set
	/// <c>&amp;MAILFORWARDLIST me=#4</c>; S's mail to A reached only B, and S saw
	/// <c>MAIL: You sent your message to LA127.</c>
	/// </summary>
	[Test]
	public async ValueTask AForwardListDeliversToEachTargetThatAllowsIt()
	{
		var sender = await Player("MdFlSend");
		var owner = await Player("MdFlOwner");
		var target = await Player("MdFlTarget");
		await Run(target, $"@lock/mailforward me=#{owner.DbRef.Number}");
		await Run(owner, $"&MAILFORWARDLIST me=#{target.DbRef.Number}");

		var ownerHeard = await Heard(owner, async () =>
		{
			var targetHeard = await Heard(target, async () =>
			{
				var senderHeard = await Heard(sender, () => Run(sender, $"@mail #{owner.DbRef.Number}=Forwarded/Body."));
				await Assert.That(senderHeard).Contains($"MAIL: You sent your message to {owner.Name}.");
			});
			await Assert.That(targetHeard).Contains($"MAIL: You have a new message (1) from {sender.Name}.");
		});

		await Assert.That(ownerHeard).DoesNotContain(m => m.StartsWith("MAIL: You have a new message"));
		await Assert.That(await Mailbox(owner)).IsEmpty();
		await Assert.That(await Mailbox(target)).Count().IsEqualTo(1);
	}

	/// <summary>
	/// <c>Can_MailForward</c> (<c>hdrs/mushdb.h:130</c>) needs control or a <em>set</em> mailforward lock
	/// the owner passes: an unset lock is not permission. Captured after B unlocked: A heard
	/// <c>Failed attempt to forward @mail to #4</c>, S heard
	/// <c>MAIL: Your message was not sent to LA127 due to a mail forwarding problem.</c>, and a silent send
	/// still told A.
	/// </summary>
	[Test]
	public async ValueTask AForwardTargetThatNeverAllowedItIsReportedAndNotDelivered()
	{
		var sender = await Player("MdFlNoLock");
		var owner = await Player("MdFlNoLockOwner");
		var target = await Player("MdFlNoLockTarget");
		await Run(owner, $"&MAILFORWARDLIST me=#{target.DbRef.Number}");

		var senderHeard = await Heard(sender, async () =>
		{
			var ownerHeard = await Heard(owner, () => Run(sender, $"@mail #{owner.DbRef.Number}=Nowhere/Body."));
			await Assert.That(ownerHeard).Contains($"Failed attempt to forward @mail to #{target.DbRef.Number}");
		});
		var silentOwnerHeard = await Heard(owner, async () =>
		{
			var silentSenderHeard = await Heard(sender, () => Run(sender, $"@mail/silent #{owner.DbRef.Number}=Nowhere/Body."));
			await Assert.That(silentSenderHeard).IsEmpty();
		});

		await Assert.That(senderHeard)
			.Contains($"MAIL: Your message was not sent to {owner.Name} due to a mail forwarding problem.");
		await Assert.That(silentOwnerHeard).Contains($"Failed attempt to forward @mail to #{target.DbRef.Number}");
		await Assert.That(await Mailbox(owner)).IsEmpty();
		await Assert.That(await Mailbox(target)).IsEmpty();
	}

	/// <summary>
	/// "don't check mailforward further" (<c>extmail.c:1479</c>): a forward target's own list is not
	/// consulted, so two lists naming each other cannot bounce a message. Captured: A→B with B→C, and the
	/// mail stopped at B.
	/// </summary>
	[Test]
	public async ValueTask AForwardTargetsOwnListIsNotFollowed()
	{
		var sender = await Player("MdFlChain");
		var first = await Player("MdFlChainA");
		var second = await Player("MdFlChainB");
		await Run(second, $"@lock/mailforward me=#{first.DbRef.Number}");
		await Run(first, $"@lock/mailforward me=#{second.DbRef.Number}");
		await Run(first, $"&MAILFORWARDLIST me=#{second.DbRef.Number}");
		await Run(second, $"&MAILFORWARDLIST me=#{first.DbRef.Number}");

		await Run(sender, $"@mail #{first.DbRef.Number}=Loop/Body.");

		await Assert.That(await Mailbox(first)).IsEmpty();
		await Assert.That(await Mailbox(second)).Count().IsEqualTo(1);
	}

	/// <summary>
	/// A list naming its owner keeps a copy there too — <c>controls(p, p)</c> passes. Captured:
	/// <c>&amp;MAILFORWARDLIST me=#3 #4</c> delivered to both.
	/// </summary>
	[Test]
	public async ValueTask AForwardListNamingItsOwnerKeepsACopy()
	{
		var sender = await Player("MdFlSelf");
		var owner = await Player("MdFlSelfOwner");
		var target = await Player("MdFlSelfTarget");
		await Run(target, $"@lock/mailforward me=#{owner.DbRef.Number}");
		await Run(owner, $"&MAILFORWARDLIST me=#{owner.DbRef.Number} #{target.DbRef.Number}");

		await Run(sender, $"@mail #{owner.DbRef.Number}=Both/Body.");

		await Assert.That(await Mailbox(owner)).Count().IsEqualTo(1);
		await Assert.That(await Mailbox(target)).Count().IsEqualTo(1);
	}

	/// <summary>
	/// Each forward goes through <c>real_send_mail</c> with silent=1, so it is the <em>target's</em> mail
	/// lock that counts, and the owner's is never asked. Captured: B mail-locked gave S the forwarding
	/// problem; A mail-locked still reached B.
	/// </summary>
	[Test]
	public async ValueTask ForwardingChecksTheTargetsMailLockNotTheOwners()
	{
		var sender = await Player("MdFlLocks");
		var owner = await Player("MdFlLocksOwner");
		var target = await Player("MdFlLocksTarget");
		await Run(target, $"@lock/mailforward me=#{owner.DbRef.Number}");
		await Run(owner, $"&MAILFORWARDLIST me=#{target.DbRef.Number}");

		await Run(owner, "@lock/mail me=#0");
		await Run(sender, $"@mail #{owner.DbRef.Number}=Owner locked/Body.");
		await Assert.That(await Mailbox(target)).Count().IsEqualTo(1);

		await Run(owner, "@unlock/mail me");
		await Run(target, "@lock/mail me=#0");
		var heard = await Heard(sender, () => Run(sender, $"@mail #{owner.DbRef.Number}=Target locked/Body."));

		await Assert.That(heard)
			.Contains($"MAIL: Your message was not sent to {owner.Name} due to a mail forwarding problem.");
		await Assert.That(heard).DoesNotContain(m => m.Contains("is not accepting mail"));
		await Assert.That(await Mailbox(target)).Count().IsEqualTo(1);
	}

	/// <summary>
	/// The list is read by <c>send_mail</c>, which every sender goes through. Captured: <c>mailsend()</c> and
	/// <c>@mail/fwd</c> to A both reached B.
	/// </summary>
	[Test]
	public async ValueTask MailsendAndForwardFollowTheForwardList()
	{
		var sender = await Player("MdFlPaths");
		var owner = await Player("MdFlPathsOwner");
		var target = await Player("MdFlPathsTarget");
		await Run(target, $"@lock/mailforward me=#{owner.DbRef.Number}");
		await Run(owner, $"&MAILFORWARDLIST me=#{target.DbRef.Number}");

		await Run(sender, $"think [mailsend(#{owner.DbRef.Number},Function/Body.)]");
		await Run(sender, "@mail me=Kept/Body.");
		await Run(sender, $"@mail/fwd 1=#{owner.DbRef.Number}");

		await Assert.That(await Mailbox(owner)).IsEmpty();
		await Assert.That((await Mailbox(target)).Select(m => m.Subject.ToPlainText()))
			.IsEquivalentTo(["Function", "Fwd: Kept"]);
	}

	/// <summary>
	/// Only dbrefs are forward targets (<c>is_objid</c>, <c>extmail.c:1495</c>); other words are skipped
	/// without comment, and a dbref that is no player — or no object — is reported to the owner.
	/// </summary>
	[Test]
	public async ValueTask AForwardListIgnoresWordsAndReportsNonPlayers()
	{
		var sender = await Player("MdFlJunk");
		var owner = await Player("MdFlJunkOwner");
		var target = await Player("MdFlJunkTarget");
		await Run(target, $"@lock/mailforward me=#{owner.DbRef.Number}");
		await Run(owner, $"&MAILFORWARDLIST me={target.Name} garbage #0 #99999999 #{target.DbRef.Number}");

		var ownerHeard = await Heard(owner, () => Run(sender, $"@mail #{owner.DbRef.Number}=Junk/Body."));

		await Assert.That(ownerHeard).Contains("Failed attempt to forward @mail to #0");
		await Assert.That(ownerHeard).Contains("Failed attempt to forward @mail to #-1");
		await Assert.That(ownerHeard.Count(m => m.StartsWith("Failed attempt"))).IsEqualTo(2);
		await Assert.That(await Mailbox(target)).Count().IsEqualTo(1);
	}

	/// <summary>
	/// <c>controls(p, x)</c> is the other half of <c>Can_MailForward</c>: a wizard's list reaches a player
	/// who set no mailforward lock at all.
	/// </summary>
	[Test]
	public async ValueTask AForwardListOwnerWhoControlsTheTargetNeedsNoLock()
	{
		var sender = await Player("MdFlCtl");
		var wizard = await Player("MdFlCtlWiz");
		var target = await Player("MdFlCtlTarget");
		await God($"@set #{wizard.DbRef.Number}=WIZARD");
		await Run(wizard, $"&MAILFORWARDLIST me=#{target.DbRef.Number}");

		await Run(sender, $"@mail #{wizard.DbRef.Number}=Controlled/Body.");

		await Assert.That(await Mailbox(target)).Count().IsEqualTo(1);
	}

	/// <summary>
	/// <c>filter_mail</c> (<c>extmail.c:3289</c>): a non-empty MAILFILTER result files the new message into
	/// that folder. Captured: <c>MAIL: You have a new message (3) from LS309.</c> then
	/// <c>MAIL: Msg 0:3 filed in folder 1 [STUFF]</c>. SharpMUSH folders are named, not numbered.
	/// </summary>
	[Test]
	public async ValueTask AMailFilterFilesTheMessageIntoTheFolderItNames()
	{
		var sender = await Player("MdFltFile");
		var target = await Player("MdFltFileTo");
		await Run(target, "&MAILFILTER me=[if(strmatch(%1,*urgent*),Urgent)]");

		var heard = await Heard(target, async () =>
		{
			await Run(sender, $"@mail #{target.DbRef.Number}=Not urgent at all/Body.");
			await Run(sender, $"@mail #{target.DbRef.Number}=Plain/Body.");
		});

		await Assert.That(heard).Contains($"MAIL: You have a new message (1) from {sender.Name}.");
		await Assert.That(heard).Contains("MAIL: Msg 1 filed in folder Urgent.");
		await Assert.That((await Mailbox(target)).Select(m => m.Subject.ToPlainText())).IsEquivalentTo(["Plain"]);
		await Assert.That((await Mailbox(target, "Urgent")).Select(m => m.Subject.ToPlainText()))
			.IsEquivalentTo(["Not urgent at all"]);
	}

	/// <summary>
	/// The filter runs as the recipient with the sender as enactor, and gets the sender's dbref, the
	/// subject, the body as written and the U/F/R flags. Captured from
	/// <c>[pemit(me,f:%0|%1|%2|%3|%#|%@|%!)]</c> on an urgent message:
	/// <c>f:#6|flt1|filter body|U|#6|#8|#8</c>.
	/// </summary>
	[Test]
	public async ValueTask AMailFilterSeesTheSenderSubjectBodyAndFlags()
	{
		var sender = await Player("MdFltArgs");
		var target = await Player("MdFltArgsTo");
		await Run(sender, "&MAILSIGNATURE me=-- signed");
		await Run(target, "&MAILFILTER me=[pemit(me,f:%0|%1|%2|%3|%#|%@|%!)]");

		var heard = await Heard(target, () => Run(sender, $"@mail/urgent #{target.DbRef.Number}=Subject line/filter body"));

		var s = sender.DbRef.Number;
		var t = target.DbRef.Number;
		await Assert.That(heard).Contains($"f:#{s}|Subject line|filter body|U|#{s}|#{t}|#{t}");
		await Assert.That(await Mailbox(target)).Count().IsEqualTo(1);
	}

	/// <summary>A forward reaches the filter flagged <c>F</c> (<c>extmail.c:3306</c>).</summary>
	[Test]
	public async ValueTask AMailFilterSeesAForwardFlagged()
	{
		var sender = await Player("MdFltFwd");
		var target = await Player("MdFltFwdTo");
		await Run(target, "&MAILFILTER me=[pemit(me,flags:%3)]");
		await Run(sender, "@mail me=Pass it on/Body.");

		var heard = await Heard(target, () => Run(sender, $"@mail/fwd 1=#{target.DbRef.Number}"));

		await Assert.That(heard).Contains("flags:F");
	}

	/// <summary>
	/// A result that names no folder leaves the message in the inbox. Captured:
	/// <c>MAIL: Invalid folder specification</c>. Penn folder names are alphanumeric
	/// (<c>extmail.c:333</c>), which is also what keeps an error string from becoming a folder.
	/// </summary>
	[Test]
	public async ValueTask AMailFilterResultThatIsNoFolderNameLeavesTheMessageInTheInbox()
	{
		var sender = await Player("MdFltBad");
		var target = await Player("MdFltBadTo");
		await Run(target, "&MAILFILTER me=not a folder!");

		var heard = await Heard(target, () => Run(sender, $"@mail #{target.DbRef.Number}=Stays/Body."));

		await Assert.That(heard).Contains("MAIL: Invalid folder specification");
		await Assert.That(await Mailbox(target)).Count().IsEqualTo(1);
	}

	/// <summary>A refused message never reaches the filter: <c>filter_mail</c> runs after the store.</summary>
	[Test]
	public async ValueTask ARefusedMessageDoesNotRunTheFilter()
	{
		var sender = await Player("MdFltRefused");
		var target = await Player("MdFltRefusedTo");
		await Run(target, "&MAILFILTER me=[pemit(me,filter ran)]");
		await Run(target, "@lock/mail me=#0");

		var heard = await Heard(target, () => Run(sender, $"@mail #{target.DbRef.Number}=Refused/Body."));

		await Assert.That(heard).DoesNotContain("filter ran");
	}

	/// <summary>
	/// A filter that mails its owner is recursion PennMUSH does not bound: the captured run delivered 124
	/// messages and then the server died ("Parent mush process exited unexpectedly"). Mail sent while a
	/// filter is being evaluated is delivered without running filters, so the loop is one message deep.
	/// </summary>
	[Test]
	public async ValueTask AMailFilterThatSendsMailDoesNotRecurse()
	{
		var sender = await Player("MdFltLoop");
		var target = await Player("MdFltLoopTo");
		await Run(target, "&MAILFILTER me=[mailsend(me,loop/loop)]");

		await Run(sender, $"@mail #{target.DbRef.Number}=Start/Body.");

		await Assert.That((await Mailbox(target)).Select(m => m.Subject.ToPlainText()))
			.IsEquivalentTo(["Start", "loop"]);
	}

	/// <summary>
	/// <c>real_send_mail</c> (<c>extmail.c:1593</c>): a mailbox whose inbox holds its <c>mail_limit</c> takes
	/// no more, and the quota binds a wizard sender too. Captured with <c>&amp;MAILQUOTA *LQ309=1</c>:
	/// <c>MAIL: LQ309's mailbox is full. Can't send.</c> for S and for One, and nothing for
	/// <c>mailsend()</c>, which is silent.
	/// </summary>
	[Test]
	public async ValueTask AFullMailboxRefusesTheNextMessage()
	{
		var sender = await Player("MdQtaFull");
		var wizard = await Player("MdQtaFullWiz");
		var target = await Player("MdQtaFullTo");
		await God($"@set #{wizard.DbRef.Number}=WIZARD");
		await God($"&MAILQUOTA #{target.DbRef.Number}=1");
		await Run(sender, $"@mail #{target.DbRef.Number}=First/Body.");

		var senderHeard = await Heard(sender, () => Run(sender, $"@mail #{target.DbRef.Number}=Second/Body."));
		var wizardHeard = await Heard(wizard, () => Run(wizard, $"@mail #{target.DbRef.Number}=Wizard/Body."));
		var silentHeard = await Heard(sender, () => Run(sender, $"think [mailsend(#{target.DbRef.Number},Third/Body.)]"));

		await Assert.That(senderHeard).Contains($"MAIL: {target.Name}'s mailbox is full. Can't send.");
		await Assert.That(wizardHeard).Contains($"MAIL: {target.Name}'s mailbox is full. Can't send.");
		await Assert.That(silentHeard).DoesNotContain(m => m.Contains("mailbox is full"));
		await Assert.That((await Mailbox(target)).Select(m => m.Subject.ToPlainText())).IsEquivalentTo(["First"]);
	}

	/// <summary>
	/// <c>count_mail(target, 0, ...)</c> counts the inbox only, so filing a message elsewhere makes room.
	/// Captured: after <c>@mail/file 1=1</c> the next message was delivered.
	/// </summary>
	[Test]
	public async ValueTask FilingOutOfTheInboxMakesRoom()
	{
		var sender = await Player("MdQtaFile");
		var target = await Player("MdQtaFileTo");
		await God($"&MAILQUOTA #{target.DbRef.Number}=1");
		await Run(sender, $"@mail #{target.DbRef.Number}=First/Body.");
		await Run(target, "@mail/file 1=Saved");

		await Run(sender, $"@mail #{target.DbRef.Number}=Second/Body.");

		await Assert.That((await Mailbox(target)).Select(m => m.Subject.ToPlainText())).IsEquivalentTo(["Second"]);
	}

	/// <summary>
	/// <c>mail_limit</c> (<c>extmail.c:1534</c>): MAILQUOTA overrides the configured <c>mail_limit</c> when it
	/// is a positive integer; anything else falls back to it. Captured with the default limit:
	/// <c>abc</c> and <c>-5</c> both delivered.
	/// </summary>
	[Test]
	[Arguments("abc")]
	[Arguments("-5")]
	[Arguments("0")]
	public async ValueTask AQuotaThatIsNoPositiveIntegerFallsBackToTheConfiguredLimit(string quota)
	{
		using var _ = TestOptionsOverride.Scope(options => options with
		{
			Limit = options.Limit with { MailLimit = 1 }
		});

		var sender = await Player("MdQtaBad");
		var target = await Player("MdQtaBadTo");
		await God($"&MAILQUOTA #{target.DbRef.Number}={quota}");

		await Run(sender, $"@mail #{target.DbRef.Number}=First/Body.");
		var heard = await Heard(sender, () => Run(sender, $"@mail #{target.DbRef.Number}=Second/Body."));

		await Assert.That(heard).Contains($"MAIL: {target.Name}'s mailbox is full. Can't send.");
		await Assert.That(await Mailbox(target)).Count().IsEqualTo(1);
	}

	/// <summary>A valid MAILQUOTA raises the limit above the configured one as well as lowering it.</summary>
	[Test]
	public async ValueTask AQuotaRaisesTheConfiguredLimit()
	{
		using var _ = TestOptionsOverride.Scope(options => options with
		{
			Limit = options.Limit with { MailLimit = 1 }
		});

		var sender = await Player("MdQtaRaise");
		var target = await Player("MdQtaRaiseTo");
		await God($"&MAILQUOTA #{target.DbRef.Number}=3");

		await Run(sender, $"@mail #{target.DbRef.Number}=First/Body.");
		await Run(sender, $"@mail #{target.DbRef.Number}=Second/Body.");

		await Assert.That(await Mailbox(target)).Count().IsEqualTo(2);
	}

	/// <summary>
	/// MAILQUOTA is a wizard attribute (<c>AF_WIZARD | AF_LOCKED</c>, <c>atr_tab.h:113</c>). Captured:
	/// <c>That attribute cannot be changed by you.</c> for a mortal setting their own.
	/// </summary>
	[Test]
	public async ValueTask AMortalCannotRaiseTheirOwnQuota()
	{
		using var _ = TestOptionsOverride.Scope(options => options with
		{
			Limit = options.Limit with { MailLimit = 1 }
		});

		var sender = await Player("MdQtaMortal");
		var target = await Player("MdQtaMortalTo");
		await Run(target, "&MAILQUOTA me=100");

		await Run(sender, $"@mail #{target.DbRef.Number}=First/Body.");
		await Run(sender, $"@mail #{target.DbRef.Number}=Second/Body.");

		await Assert.That(await Get(target.DbRef, "MAILQUOTA")).IsEqualTo(string.Empty);
		await Assert.That(await Mailbox(target)).Count().IsEqualTo(1);
	}

	/// <summary>
	/// A forward is its own <c>real_send_mail</c>, so a full forward target refuses it silently and the
	/// sender hears about the forwarding problem.
	/// </summary>
	[Test]
	public async ValueTask AFullForwardTargetIsAForwardingProblem()
	{
		var sender = await Player("MdQtaFwd");
		var owner = await Player("MdQtaFwdOwner");
		var target = await Player("MdQtaFwdTarget");
		await God($"&MAILQUOTA #{target.DbRef.Number}=1");
		await Run(target, $"@lock/mailforward me=#{owner.DbRef.Number}");
		await Run(owner, $"&MAILFORWARDLIST me=#{target.DbRef.Number}");
		await Run(sender, $"@mail #{owner.DbRef.Number}=First/Body.");

		var heard = await Heard(sender, () => Run(sender, $"@mail #{owner.DbRef.Number}=Second/Body."));

		await Assert.That(heard)
			.Contains($"MAIL: Your message was not sent to {owner.Name} due to a mail forwarding problem.");
		await Assert.That(heard).DoesNotContain(m => m.Contains("mailbox is full"));
		await Assert.That(await Mailbox(target)).Count().IsEqualTo(1);
	}

	/// <summary>
	/// Penn is single-threaded, so counting the inbox and inserting the message are one step there. Here
	/// deliveries run concurrently (the portal's command invoker among them), so two messages arriving
	/// together must still be numbered apart.
	/// </summary>
	[Test]
	public async ValueTask ConcurrentDeliveriesToOneMailboxAreNumberedApart()
	{
		var target = await Player("MdRaceTo");
		var senders = new List<TestIsolationHelpers.TestPlayer>();
		for (var i = 0; i < 8; i++)
		{
			senders.Add(await Player($"MdRace{i}"));
		}

		var heard = await Heard(target, () =>
			Task.WhenAll(senders.Select(sender => Run(sender, $"@mail #{target.DbRef.Number}=Race/Body."))));

		var numbers = heard
			.Select(m => System.Text.RegularExpressions.Regex.Match(m, @"^MAIL: You have a new message \((\d+)\)"))
			.Where(match => match.Success)
			.Select(match => int.Parse(match.Groups[1].Value));

		await Assert.That(numbers).IsEquivalentTo(Enumerable.Range(1, 8));
	}

	/// <summary>
	/// The quota is decided under the same gate as the store, so messages arriving together cannot all
	/// pass a count taken before any of them was written.
	/// </summary>
	[Test]
	public async ValueTask ConcurrentDeliveriesCannotOverfillAMailbox()
	{
		var target = await Player("MdRaceQtaTo");
		await God($"&MAILQUOTA #{target.DbRef.Number}=1");
		var senders = new List<TestIsolationHelpers.TestPlayer>();
		for (var i = 0; i < 8; i++)
		{
			senders.Add(await Player($"MdRaceQta{i}"));
		}

		await Task.WhenAll(senders.Select(sender => Run(sender, $"@mail #{target.DbRef.Number}=Race/Body.")));

		await Assert.That(await Mailbox(target)).Count().IsEqualTo(1);
	}

	/// <summary>
	/// An empty MAILFORWARDLIST is no list. Penn (empty_attrs yes) keeps <c>&amp;MAILFORWARDLIST me=</c> as
	/// an empty attribute, treats it as a list naming nobody, and drops every message: captured
	/// <c>MAIL: Your message was not sent to LE707 due to a mail forwarding problem.</c> That is a trap with
	/// no use, so this deviates.
	/// </summary>
	[Test]
	public async ValueTask AnEmptyForwardListDeliversNormally()
	{
		var sender = await Player("MdFlEmpty");
		var owner = await Player("MdFlEmptyOwner");
		await Run(owner, "&MAILFORWARDLIST me=");

		var heard = await Heard(sender, () => Run(sender, $"@mail #{owner.DbRef.Number}=Delivered/Body."));

		await Assert.That(heard).Contains($"MAIL: You sent your message to {owner.Name}.");
		await Assert.That(await Mailbox(owner)).Count().IsEqualTo(1);
	}

	/// <summary>
	/// <c>SetExpandedDataAsync</c> replaces the folder array, so two deliveries filing into different new
	/// folders must not each store only their own: the read and the write happen under the mailbox gate.
	/// Read back through <see cref="ExpandedDataQuery"/> rather than
	/// <c>GetExpandedDataAsync&lt;ExpandedMailData&gt;</c>, which cannot return typed data today (#1221).
	/// </summary>
	[Test]
	public async ValueTask ConcurrentFilingKeepsEveryNewFolder()
	{
		var target = await Player("MdRaceFldTo");
		await Run(target, "&MAILFILTER me=%1");
		var senders = new List<TestIsolationHelpers.TestPlayer>();
		for (var i = 0; i < 6; i++)
		{
			senders.Add(await Player($"MdRaceFld{i}"));
		}

		await Task.WhenAll(senders.Select((sender, i) =>
			Run(sender, $"@mail #{target.DbRef.Number}=Folder{i}/Body.")));

		var stored = await Mediator.Send(new ExpandedDataQuery(
			(await Mediator.Send(new GetObjectNodeQuery(target.DbRef))).Expect<SharpPlayer>().Object,
			nameof(ExpandedMailData)));

		var folders = JsonSerializer.Deserialize<ExpandedMailData>(JsonSerializer.Serialize(stored))!.Folders!;

		await Assert.That(folders).IsEquivalentTo(Enumerable.Range(0, 6).Select(i => $"Folder{i}"));
	}
}
