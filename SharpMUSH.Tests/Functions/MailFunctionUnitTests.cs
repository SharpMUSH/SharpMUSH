using SharpMUSH.Implementation;
using SharpMUSH.Configuration.Options;
using NSubstitute;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class MailFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	/// <summary>Evaluates as this test's own player, whose mailbox holds exactly the three fixture messages.</summary>
	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParserFor(_player);
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	// Unique test identifier to ensure we don't conflict with other test runs
	private static readonly string TestRunId = Guid.NewGuid().ToString("N")[..8];

	private DBRef _player;

	/// <summary>
	/// Every test gets a fresh player who has mailed itself three messages. God's mailbox is the one
	/// every other mail test writes to, so counting it was only ever exact when nothing else ran.
	/// </summary>
	[Before(Test)]
	public async Task EnsureTestMailSetup()
	{
		_player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "MailFunctions");
		var executor = (await Mediator.Send(new GetObjectNodeQuery(_player))).Expect<AnySharpObject>();
		var testPlayer = executor.Expect<SharpPlayer>();

		var testMail1 = new SharpMail
		{
			DateSent = DateTimeOffset.UtcNow.AddHours(-2),
			Fresh = false,
			Read = true,
			Tagged = false,
			Urgent = false,
			Cleared = false,
			Forwarded = false,
			Folder = "INBOX",
			Content = MarkupText.Plain($"TESTMAIL-{TestRunId}-MSG1-Content"),
			Subject = MarkupText.Plain($"TESTMAIL-{TestRunId}-Subject1"),
			From = new DotNext.Threading.AsyncLazy<AnyOptionalSharpObject>(
				async _ => await ValueTask.FromResult(executor.WithNoneOption()))
		};

		var testMail2 = new SharpMail
		{
			DateSent = DateTimeOffset.UtcNow.AddHours(-1),
			Fresh = true,
			Read = false,
			Tagged = true,
			Urgent = true,
			Cleared = false,
			Forwarded = false,
			Folder = "INBOX",
			Content = MarkupText.Plain($"TESTMAIL-{TestRunId}-MSG2-Content with more text"),
			Subject = MarkupText.Plain($"TESTMAIL-{TestRunId}-UrgentSubject2"),
			From = new DotNext.Threading.AsyncLazy<AnyOptionalSharpObject>(
				async _ => await ValueTask.FromResult(executor.WithNoneOption()))
		};

		var testMail3 = new SharpMail
		{
			DateSent = DateTimeOffset.UtcNow.AddMinutes(-30),
			Fresh = false,
			Read = false,
			Tagged = false,
			Urgent = false,
			Cleared = true,
			Forwarded = false,
			Folder = "INBOX",
			Content = MarkupText.Plain($"TESTMAIL-{TestRunId}-MSG3-Content"),
			Subject = MarkupText.Plain($"TESTMAIL-{TestRunId}-Subject3"),
			From = new DotNext.Threading.AsyncLazy<AnyOptionalSharpObject>(
				async _ => await ValueTask.FromResult(executor.WithNoneOption()))
		};

		await Mediator.Send(new SendMailCommand(executor.Object(), testPlayer, testMail1));
		await Mediator.Send(new SendMailCommand(executor.Object(), testPlayer, testMail2));
		await Mediator.Send(new SendMailCommand(executor.Object(), testPlayer, testMail3));
	}

	[Test]
	public async Task Mail_NoArgs_ReturnsCount()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("mail()")))!.Message;
		var count = int.Parse(result.ToPlainText()!);
		await Assert.That(count).IsEqualTo(3);
	}

	[Test]
	public async Task Mail_WithMessageNumber_ReturnsContent()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("mail(1)")))!.Message;
		var content = result.ToPlainText();
		await Assert.That(content).Contains($"TESTMAIL-{TestRunId}");
	}

	/// <summary>
	/// <c>parse_message_spec</c> reads a message number with <c>is_integer</c>
	/// (<c>src/extmail.c:3171-3174</c>), so under TINY_MATH its leading digits name the message.
	/// </summary>
	[Test]
	[Arguments(true, "mail(1x)", true)]
	[Arguments(false, "mail(1x)", false)]
	public async Task AMessageNumberIsReadAsIsInteger(bool tinyMath, string str, bool readsMessage)
	{
		var baseline = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(baseline with { Compatibility = baseline.Compatibility with { TinyMath = tinyMath } });
		var original = (MUSHCodeParser)Parser;
		var parser = original with { ServiceProvider = new OptionsProvider(original.ServiceProvider, options) };

		var result = (await parser.FunctionParse(MarkupText.Plain(str)))!.Message.ToPlainText();
		await Assert.That(result.Contains($"TESTMAIL-{TestRunId}")).IsEqualTo(readsMessage);
	}

	private sealed class OptionsProvider(IServiceProvider inner, IOptionsWrapper<SharpMUSHOptions> options) : IServiceProvider
	{
		public object? GetService(Type serviceType) => serviceType == typeof(IOptionsWrapper<SharpMUSHOptions>)
			? options : inner.GetService(serviceType);
	}

	[Test]
	[Arguments("mail(999)", "#-1 INVALID MESSAGE OR PLAYER")]
	public async Task Mail_InvalidMessage_ReturnsError(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))!.Message;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Maillist_NoArgs_ReturnsMailList()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("maillist()")))!.Message;
		var mailList = result.ToPlainText()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(mailList.Length).IsGreaterThanOrEqualTo(3);
		foreach (var entry in mailList)
		{
			await Assert.That(entry).Contains(":");
		}
	}

	[Test]
	public async Task Maillist_WithFilter_ReturnsFilteredList()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("maillist(unread)")))!.Message;
		var mailList = result.ToPlainText()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(mailList.Length).IsGreaterThanOrEqualTo(2);
	}

	[Test]
	public async Task Mailfrom_ValidMessage_ReturnsSenderDbref()
	{
		// The fixture player sent every message to itself; fun_mailfrom writes a bare dbref.
		var result = (await Parser.FunctionParse(MarkupText.Plain("mailfrom(1)")))!.Message;
		await Assert.That(result.ToPlainText()).IsEqualTo($"#{_player.Number}");
	}

	[Test]
	[Arguments("mailfrom(999)", "#-1")]
	public async Task Mailfrom_InvalidMessage_ReturnsError(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))!.Message;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Mailstats_ValidPlayer_ReturnsStats()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("mailstats(%#)")))!.Message;
		var parts = result.ToPlainText()!.Split(' ');
		await Assert.That(parts.Length).IsEqualTo(2);
		await Assert.That(int.TryParse(parts[0], out var sent)).IsTrue();
		await Assert.That(int.TryParse(parts[1], out var received)).IsTrue();
		await Assert.That(sent).IsEqualTo(3);
		await Assert.That(received).IsEqualTo(3);
	}

	/// <summary>
	/// The fixture sends three messages to itself: one read, one unread, and one unread but cleared. As in
	/// PennMUSH's <c>fun_mailstats</c> and <c>count_mail</c>, the cleared one counts only as cleared, so
	/// one message is unread, not two. Live PennMUSH 80a1d5b, with the same three messages:
	/// <c>maildstats()</c> gives <c>0 0 0 3 1 1</c> and <c>mail(Mortal)</c> gives <c>1 1 1</c>.
	/// </summary>
	[Test]
	public async Task MailStats_ClearedMessageCountsOnlyAsCleared()
	{
		var bytes = $"TESTMAIL-{TestRunId}-MSG1-Content".Length
			+ $"TESTMAIL-{TestRunId}-MSG2-Content with more text".Length
			+ $"TESTMAIL-{TestRunId}-MSG3-Content".Length;

		await Assert.That((await Parser.FunctionParse(MarkupText.Plain("maildstats(%#)")))!.Message.ToPlainText())
			.IsEqualTo("3 1 1 3 1 1");
		await Assert.That((await Parser.FunctionParse(MarkupText.Plain("mailfstats(%#)")))!.Message.ToPlainText())
			.IsEqualTo($"3 1 1 {bytes} 3 1 1 {bytes}");
		await Assert.That((await Parser.FunctionParse(MarkupText.Plain("mail(%#)")))!.Message.ToPlainText())
			.IsEqualTo("1 1 1");
	}

	/// <summary>
	/// <c>fun_mailstats</c> looks the player up first and then requires <c>controls()</c>, so a mortal
	/// may read their own statistics however they name themselves, and anyone else's is refused.
	/// Live PennMUSH 80a1d5b, as a mortal with three messages: <c>mailstats(me)</c>,
	/// <c>mailstats(Mortal)</c> and <c>mailstats(*Mortal)</c> each give <c>0 3</c>;
	/// <c>mailstats(One)</c> notifies "The post office protects privacy!"; <c>mailstats(nosuchguy)</c>
	/// notifies "nosuchguy: No such player.". SharpMUSH returns those two reasons instead.
	/// </summary>
	[Test]
	[Arguments("mailstats()", "0 0")]
	[Arguments("mailstats(me)", "0 0")]
	[Arguments("mailstats(%#)", "0 0")]
	[Arguments("maildstats(me)", "0 0 0 0 0 0")]
	[Arguments("mailfstats(me)", "0 0 0 0 0 0 0 0")]
	[Arguments("mailstats(#1)", "#-1 PERMISSION DENIED")]
	[Arguments("maildstats(#1)", "#-1 PERMISSION DENIED")]
	[Arguments("mailfstats(#1)", "#-1 PERMISSION DENIED")]
	[Arguments("mailstats(NoSuchMailStatsPlayer)", "#-1 NO SUCH PLAYER")]
	public async Task MailStats_MortalReadsOnlyTheirOwn(string code, string expected)
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "MailStatsMortal");
		var asMortal = WebAppFactoryArg.FunctionParserFor(mortal);

		var result = (await asMortal.FunctionParse(MarkupText.Plain(code)))!.Message.ToPlainText();

		await Assert.That(result).IsEqualTo(expected);
	}

	/// <summary>
	/// <c>fun_mail</c>, <c>fun_maillist</c> and <c>mailfun_fetch</c> (<c>src/extmail.c</c>) match the
	/// player first and then require <c>controls(executor, player)</c>, so a mortal reads their own
	/// mailbox under any name for themselves and is refused anyone else's (#1491). SharpMUSH used to
	/// refuse every non-wizard before looking at who was named. A refused fetch answers as Penn's does:
	/// "Permission denied" to the caller, and the function's usual not-found value.
	/// </summary>
	[Test]
	[Arguments("mail(me)", "0 1 0")]
	[Arguments("mail(%#)", "0 1 0")]
	[Arguments("mail(me,1)", "MortalOwnMailBody")]
	[Arguments("mailsubject(me,1)", "MortalOwnMailSubject")]
	[Arguments("mailstatus(me,1)", "N----")]
	[Arguments("mail(#1)", "#-1 PERMISSION DENIED")]
	[Arguments("mail(#1,1)", "#-1 INVALID MESSAGE OR PLAYER")]
	[Arguments("mailsubject(#1,1)", "#-1")]
	[Arguments("mailfrom(#1,1)", "#-1")]
	[Arguments("maillist(#1,1)", "#-1 PERMISSION DENIED")]
	public async Task MailFunctions_MortalReadsOnlyTheirOwnMailbox(string code, string expected)
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "MailOwnMortal");
		var asMortal = WebAppFactoryArg.FunctionParserFor(mortal);
		await asMortal.FunctionParse(MarkupText.Plain("mailsend(me,MortalOwnMailSubject/MortalOwnMailBody)"));

		var result = (await asMortal.FunctionParse(MarkupText.Plain(code)))!.Message.ToPlainText();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("mail(#1,1)")]
	[Arguments("mailsubject(#1,1)")]
	[Arguments("mailtime(#1,1)")]
	public async Task MailFetch_RefusedMailbox_TellsTheCaller(string code)
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "MailFetchRefused");
		var before = WebAppFactoryArg.Notifications.DeliveryCountFor(mortal);

		await WebAppFactoryArg.FunctionParserFor(mortal).FunctionParse(MarkupText.Plain(code));

		// Only what the mortal was told by its own call: a parallel test's player disconnecting in the shared
		// default home is heard too, from that player.
		await Assert.That(WebAppFactoryArg.Notifications.DeliveriesFor(mortal).Skip(before)
				.Where(delivery => delivery.Sender is { } sender && sender.Number == mortal.Number)
				.Select(delivery => delivery.Message))
			.IsEquivalentTo(["Permission denied"]);
	}

	[Test]
	public async Task MailFrom_MortalReadsTheirOwnMessage()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "MailFromMortal");
		var asMortal = WebAppFactoryArg.FunctionParserFor(mortal);
		await asMortal.FunctionParse(MarkupText.Plain("mailsend(me,FromSubject/FromBody)"));

		var result = (await asMortal.FunctionParse(MarkupText.Plain("mailfrom(me,1)")))!.Message.ToPlainText();

		// fun_mailfrom writes a bare dbref, not an objid.
		await Assert.That(result).IsEqualTo($"#{mortal.Number}");
	}

	[Test]
	public async Task Mailstatus_ValidMessage_ReturnsStatusFormat()
	{
		// Use maillist() to obtain the actual mail number rather than assuming it is always 1.
		var listResult = (await Parser.FunctionParse(MarkupText.Plain("maillist()")))!.Message;
		var mailList = listResult.ToPlainText()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(mailList.Length).IsGreaterThan(0).Because("EnsureTestMailSetup should have created at least one mail");

		// maillist() returns entries in "folder:number" format — take the number part of the first entry.
		var firstEntry = mailList[0].Split(':');
		await Assert.That(firstEntry.Length).IsEqualTo(2).Because("each maillist entry should be in folder:number format");
		var mailNumber = firstEntry[1];

		var result = (await Parser.FunctionParse(MarkupText.Plain($"mailstatus({mailNumber})")))!.Message;
		var status = result.ToPlainText();
		// Status should be 5 characters in NCUF+ format
		await Assert.That(status).Length().IsEqualTo(5);
		// Should contain valid status characters (N or -, C or -, U or -, F or -, + or -)
		await Assert.That(status!.All(c => c == 'N' || c == 'C' || c == 'U' || c == 'F' || c == '+' || c == '-')).IsTrue();
	}

	[Test]
	public async Task Mailstatus_ChecksForUrgentFlag()
	{
		var allMail = (await Parser.FunctionParse(MarkupText.Plain("maillist()")))!.Message;
		var mailList = allMail.ToPlainText()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		bool foundUrgent = false;
		foreach (var mailId in mailList)
		{
			var parts = mailId.Split(':');
			if (parts.Length == 2)
			{
				var result = (await Parser.FunctionParse(MarkupText.Plain($"mailstatus({parts[1]})")))!.Message;
				var status = result.ToPlainText();
				if (status!.Contains("U"))
				{
					foundUrgent = true;
					break;
				}
			}
		}

		// We created one urgent message in setup
		await Assert.That(foundUrgent).IsTrue();
	}

	[Test]
	[Arguments("mailstatus(999)", "#-1")]
	public async Task Mailstatus_InvalidMessage_ReturnsError(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))!.Message;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Mailsubject_ValidMessage_ReturnsSubject()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("mailsubject(1)")))!.Message;
		var subject = result.ToPlainText();
		await Assert.That(subject).Contains($"TESTMAIL-{TestRunId}");
	}

	[Test]
	[Arguments("mailsubject(999)", "#-1")]
	public async Task Mailsubject_InvalidMessage_ReturnsError(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))!.Message;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary><c>fun_mailtime</c> writes <c>show_time(mp-&gt;time, 0)</c>: local time, as <c>time()</c> does.</summary>
	[Test]
	public async Task Mailtime_ValidMessage_ReturnsTimestamp()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("mailtime(1)")))!.Message;
		var parsed = DateTime.ParseExact(result.ToPlainText(), "ddd MMM dd HH:mm:ss yyyy",
			System.Globalization.CultureInfo.InvariantCulture);
		await Assert.That(parsed).IsGreaterThan(DateTime.Now.AddDays(-1));
		await Assert.That(parsed).IsLessThan(DateTime.Now.AddMinutes(1));
	}

	[Test]
	[Arguments("mailtime(999)", "#-1")]
	public async Task Mailtime_InvalidMessage_ReturnsError(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))!.Message;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Folderstats_NoArgs_ReturnsStats()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("folderstats()")))!.Message;
		var parts = result.ToPlainText()!.Split(' ');
		await Assert.That(parts.Length).IsEqualTo(3);
		foreach (var part in parts)
		{
			await Assert.That(int.TryParse(part, out _)).IsTrue();
		}
		var read = int.Parse(parts[0]);
		var unread = int.Parse(parts[1]);
		var cleared = int.Parse(parts[2]);

		await Assert.That(read).IsGreaterThanOrEqualTo(0);
		await Assert.That(unread).IsGreaterThanOrEqualTo(0);
		await Assert.That(cleared).IsGreaterThanOrEqualTo(0);
	}

	/// <summary>
	/// Only a player has a mailbox, and a non-player executor holds no mail, rather than either one
	/// throwing out of the function. The fetch functions answer a non-player as they answer any message
	/// they cannot fetch, and maillist() with #-1 NO MATCH, as live PennMUSH does. The mail*stats()
	/// functions refuse a non-player executor outright, as <c>fun_mailstats</c> does: live PennMUSH
	/// 80a1d5b gives <c>objeval(StatObj,mailstats())</c> nothing but "No such player.".
	/// </summary>
	[Test]
	[Arguments("mail(here)", "#-1 INVALID MESSAGE OR PLAYER")]
	[Arguments("mail(here,1)", "#-1 INVALID MESSAGE OR PLAYER")]
	[Arguments("maillist(here,1)", "#-1 NO MATCH")]
	[Arguments("mailfrom(here,1)", "#-1")]
	[Arguments("mailstats(here)", "#-1 NO SUCH PLAYER")]
	[Arguments("maildstats(here)", "#-1 NO SUCH PLAYER")]
	[Arguments("mailfstats(here)", "#-1 NO SUCH PLAYER")]
	[Arguments("folderstats(here,INBOX)", "#-1 NO SUCH PLAYER")]
	[Arguments("objeval(here,mail())", "0")]
	[Arguments("objeval(here,mail(1))", "#-1 INVALID MESSAGE OR PLAYER")]
	[Arguments("objeval(here,mailstats())", "#-1 NO SUCH PLAYER")]
	public async Task MailFunctions_NonPlayer_HasNoMailbox(string str, string expected)
	{
		// God, who controls the room objeval() runs as.
		var result = (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain(str)))!.Message;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}

