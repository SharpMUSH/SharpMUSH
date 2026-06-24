using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Functions;

[NotInParallel]
public class MailFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	// Unique test identifier to ensure we don't conflict with other test runs
	private static readonly string TestRunId = Guid.NewGuid().ToString("N")[..8];
	private static bool _setupComplete;

	[Before(Test)]
	public async Task EnsureTestMailSetup()
	{
		// [NotInParallel] on the class guarantees sequential execution — no semaphore needed.
		if (_setupComplete) return;

		var executor = await Parser.CurrentState.KnownExecutorObject(Mediator);
		var testPlayer = executor.AsPlayer;

		var existingMail = Mediator.CreateStream(new GetAllMailListQuery(testPlayer));

		await foreach (var mail in existingMail)
		{
			await Mediator.Send(new DeleteMailCommand(mail));
		}

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
			Content = MModule.single($"TESTMAIL-{TestRunId}-MSG1-Content"),
			Subject = MModule.single($"TESTMAIL-{TestRunId}-Subject1"),
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
			Content = MModule.single($"TESTMAIL-{TestRunId}-MSG2-Content with more text"),
			Subject = MModule.single($"TESTMAIL-{TestRunId}-UrgentSubject2"),
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
			Content = MModule.single($"TESTMAIL-{TestRunId}-MSG3-Content"),
			Subject = MModule.single($"TESTMAIL-{TestRunId}-Subject3"),
			From = new DotNext.Threading.AsyncLazy<AnyOptionalSharpObject>(
				async _ => await ValueTask.FromResult(executor.WithNoneOption()))
		};

		await Mediator.Send(new SendMailCommand(executor.Object(), testPlayer, testMail1));
		await Mediator.Send(new SendMailCommand(executor.Object(), testPlayer, testMail2));
		await Mediator.Send(new SendMailCommand(executor.Object(), testPlayer, testMail3));

		_setupComplete = true;
	}

	[Test]
	public async Task Mail_NoArgs_ReturnsCount()
	{
		var result = (await Parser.FunctionParse(MModule.single("mail()")))?.Message!;
		var count = int.Parse(result.ToPlainText()!);
		await Assert.That(count).IsEqualTo(3);
	}

	[Test]
	public async Task Mail_WithMessageNumber_ReturnsContent()
	{
		var result = (await Parser.FunctionParse(MModule.single("mail(1)")))?.Message!;
		var content = result.ToPlainText();
		await Assert.That(content).Contains($"TESTMAIL-{TestRunId}");
	}

	[Test]
	[Arguments("mail(999)", "#-1 NO SUCH MAIL")]
	public async Task Mail_InvalidMessage_ReturnsError(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Maillist_NoArgs_ReturnsMailList()
	{
		var result = (await Parser.FunctionParse(MModule.single("maillist()")))?.Message!;
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
		var result = (await Parser.FunctionParse(MModule.single("maillist(unread)")))?.Message!;
		var mailList = result.ToPlainText()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(mailList.Length).IsGreaterThanOrEqualTo(2);
	}

	[Test]
	public async Task Mailfrom_ValidMessage_ReturnsSenderDbref()
	{
		var result = (await Parser.FunctionParse(MModule.single("mailfrom(1)")))?.Message!;
		var dbref = result.ToPlainText();
		await Assert.That(dbref).IsNotNull();
	}

	[Test]
	[Arguments("mailfrom(999)", "#-1 NO SUCH MAIL")]
	public async Task Mailfrom_InvalidMessage_ReturnsError(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Mailstats_ValidPlayer_ReturnsStats()
	{
		var result = (await Parser.FunctionParse(MModule.single("mailstats(%#)")))?.Message!;
		var parts = result.ToPlainText()!.Split(' ');
		await Assert.That(parts.Length).IsEqualTo(2);
		await Assert.That(int.TryParse(parts[0], out var sent)).IsTrue();
		await Assert.That(int.TryParse(parts[1], out var received)).IsTrue();
		await Assert.That(sent).IsEqualTo(3);
		await Assert.That(received).IsEqualTo(3);
	}

	[Test]
	public async Task Maildstats_ValidPlayer_ReturnsDetailedStats()
	{
		var result = (await Parser.FunctionParse(MModule.single("maildstats(%#)")))?.Message!;
		var parts = result.ToPlainText()!.Split(' ');
		await Assert.That(parts.Length).IsEqualTo(6);
		foreach (var part in parts)
		{
			await Assert.That(int.TryParse(part, out _)).IsTrue();
		}
		var sent = int.Parse(parts[0]);
		var sentUnread = int.Parse(parts[1]);
		var sentCleared = int.Parse(parts[2]);
		var received = int.Parse(parts[3]);
		var receivedUnread = int.Parse(parts[4]);
		var receivedCleared = int.Parse(parts[5]);

		await Assert.That(sent).IsEqualTo(3);
		await Assert.That(received).IsEqualTo(3);
		await Assert.That(receivedUnread).IsEqualTo(2);
		await Assert.That(receivedCleared).IsEqualTo(1);
		await Assert.That(sentUnread).IsEqualTo(2);
		await Assert.That(sentCleared).IsEqualTo(1);
	}

	[Test]
	public async Task Mailfstats_ValidPlayer_ReturnsFullStats()
	{
		var result = (await Parser.FunctionParse(MModule.single("mailfstats(%#)")))?.Message!;
		var parts = result.ToPlainText()!.Split(' ');
		await Assert.That(parts.Length).IsEqualTo(8);
		foreach (var part in parts)
		{
			await Assert.That(int.TryParse(part, out _)).IsTrue();
		}
		var sent = int.Parse(parts[0]);
		var received = int.Parse(parts[4]);
		var receivedBytes = int.Parse(parts[7]);

		await Assert.That(sent).IsEqualTo(3);
		await Assert.That(received).IsEqualTo(3);
		await Assert.That(receivedBytes).IsGreaterThan(0);
	}

	[Test]
	public async Task Mailstatus_ValidMessage_ReturnsStatusFormat()
	{
		// Use maillist() to obtain the actual mail number rather than assuming it is always 1.
		var listResult = (await Parser.FunctionParse(MModule.single("maillist()")))?.Message!;
		var mailList = listResult.ToPlainText()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(mailList.Length).IsGreaterThan(0).Because("EnsureTestMailSetup should have created at least one mail");

		// maillist() returns entries in "folder:number" format — take the number part of the first entry.
		var firstEntry = mailList[0].Split(':');
		await Assert.That(firstEntry.Length).IsEqualTo(2).Because("each maillist entry should be in folder:number format");
		var mailNumber = firstEntry[1];

		var result = (await Parser.FunctionParse(MModule.single($"mailstatus({mailNumber})")))?.Message!;
		var status = result.ToPlainText();
		// Status should be 5 characters in NCUF+ format
		await Assert.That(status).Length().IsEqualTo(5);
		// Should contain valid status characters (N or -, C or -, U or -, F or -, + or -)
		await Assert.That(status!.All(c => c == 'N' || c == 'C' || c == 'U' || c == 'F' || c == '+' || c == '-')).IsTrue();
	}

	[Test]
	public async Task Mailstatus_ChecksForUrgentFlag()
	{
		var allMail = (await Parser.FunctionParse(MModule.single("maillist()")))?.Message!;
		var mailList = allMail.ToPlainText()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		bool foundUrgent = false;
		foreach (var mailId in mailList)
		{
			var parts = mailId.Split(':');
			if (parts.Length == 2)
			{
				var result = (await Parser.FunctionParse(MModule.single($"mailstatus({parts[1]})")))?.Message!;
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
	[Arguments("mailstatus(999)", "#-1 NO SUCH MAIL")]
	public async Task Mailstatus_InvalidMessage_ReturnsError(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Mailsubject_ValidMessage_ReturnsSubject()
	{
		var result = (await Parser.FunctionParse(MModule.single("mailsubject(1)")))?.Message!;
		var subject = result.ToPlainText();
		await Assert.That(subject).Contains($"TESTMAIL-{TestRunId}");
	}

	[Test]
	[Arguments("mailsubject(999)", "#-1 NO SUCH MAIL")]
	public async Task Mailsubject_InvalidMessage_ReturnsError(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Mailtime_ValidMessage_ReturnsTimestamp()
	{
		var result = (await Parser.FunctionParse(MModule.single("mailtime(1)")))?.Message!;
		var timestamp = result.ToPlainText();
		await Assert.That(long.TryParse(timestamp, out var ts)).IsTrue();
		var date = DateTimeOffset.FromUnixTimeSeconds(ts);
		await Assert.That(date).IsGreaterThan(DateTimeOffset.UtcNow.AddDays(-1));
		await Assert.That(date).IsLessThan(DateTimeOffset.UtcNow.AddMinutes(1));
	}

	[Test]
	[Arguments("mailtime(999)", "#-1 NO SUCH MAIL")]
	public async Task Mailtime_InvalidMessage_ReturnsError(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("malias()", "")]
	public async Task Malias_NoArgs_ReturnsEmpty(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Folderstats_NoArgs_ReturnsStats()
	{
		var result = (await Parser.FunctionParse(MModule.single("folderstats()")))?.Message!;
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
}

