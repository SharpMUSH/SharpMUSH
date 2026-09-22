using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

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
}
