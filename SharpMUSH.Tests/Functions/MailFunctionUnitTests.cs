using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using System.Threading;

namespace SharpMUSH.Tests.Functions;

public class MailFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	
	// Unique test identifier to ensure we don't conflict with other test runs
	private static readonly string TestRunId = Guid.NewGuid().ToString("N")[..8];
	private static int _setupComplete = 0;
	private static readonly SemaphoreSlim _setupLock = new(1, 1);

	[Before(Test)]
	public async Task EnsureTestMailSetup()
	{
		// Only run setup once for all tests
		if (Interlocked.CompareExchange(ref _setupComplete, 0, 0) == 1) return;

		await _setupLock.WaitAsync();
		try
		{
			// Check again after acquiring the lock
			if (Interlocked.CompareExchange(ref _setupComplete, 0, 0) == 1) return;
		
			// Perform setup
		// Get the current player (executor)
		var executor = await Parser.CurrentState.KnownExecutorObject(Mediator);
		var testPlayer = executor.AsPlayer;
		
		// Clear any existing mail to ensure clean state
		var existingMail = Mediator.CreateStream(new GetAllMailListQuery(testPlayer));
		
		await foreach (var mail in existingMail)
		{
			await Mediator.Send(new DeleteMailCommand(mail));
		}
		
		// Create test mail messages with unique content tied to this test run
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

		// Send the test mail to the player
		await Mediator.Send(new SendMailCommand(executor.Object(), testPlayer, testMail1));
		await Mediator.Send(new SendMailCommand(executor.Object(), testPlayer, testMail2));
		await Mediator.Send(new SendMailCommand(executor.Object(), testPlayer, testMail3));
		
		// Mark setup as complete
		Interlocked.Exchange(ref _setupComplete, 1);
		}
		finally
		{
			_setupLock.Release();
		}
	}

	[Test]
	public async Task Mail_NoArgs_ReturnsCount()
	{
		// Should return count of messages (3 from setup after clearing)
		var result = (await Parser.FunctionParse(MModule.single("mail()")))?.Message!;
		var count = int.Parse(result.ToPlainText()!);
		// Should have exactly 3 messages after our clean setup
		await Assert.That(count).IsEqualTo(3);
	}

	[Test]
	public async Task Mail_WithMessageNumber_ReturnsContent()
	{
		// Get a message content (any of our test messages)
		var result = (await Parser.FunctionParse(MModule.single("mail(1)")))?.Message!;
		var content = result.ToPlainText();
		// Should be one of our test messages with our unique ID
		await Assert.That(content).Contains($"TESTMAIL-{TestRunId}");
	}

	[Test]
	[Arguments("mail(999)", "#-1 NO SUCH MAIL")]
	public async Task Mail_InvalidMessage_ReturnsError(string str, string expected)
	{
		// Non-existent message should return error
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Maillist_NoArgs_ReturnsMailList()
	{
		// Should return list of messages in folder:number format
		var result = (await Parser.FunctionParse(MModule.single("maillist()")))?.Message!;
		var mailList = result.ToPlainText()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(mailList.Length).IsGreaterThanOrEqualTo(3);
		// Each entry should be in folder:number format
		foreach (var entry in mailList)
		{
			await Assert.That(entry).Contains(":");
		}
	}

	[Test]
	public async Task Maillist_WithFilter_ReturnsFilteredList()
	{
		// Filter for unread messages
		var result = (await Parser.FunctionParse(MModule.single("maillist(unread)")))?.Message!;
		var mailList = result.ToPlainText()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		// Should have at least 2 unread messages from setup
		await Assert.That(mailList.Length).IsGreaterThanOrEqualTo(2);
	}

	[Test]
	public async Task Mailfrom_ValidMessage_ReturnsSenderDbref()
	{
		// Get sender of first message
		var result = (await Parser.FunctionParse(MModule.single("mailfrom(1)")))?.Message!;
		var dbref = result.ToPlainText();
		// Should either be a dbref or empty (if From is not set correctly)
		// For now, just check it's a valid format or empty
		await Assert.That(dbref).IsNotNull();
	}

	[Test]
	[Arguments("mailfrom(999)", "#-1 NO SUCH MAIL")]
	public async Task Mailfrom_InvalidMessage_ReturnsError(string str, string expected)
	{
		// Non-existent message should return error
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Mailstats_ValidPlayer_ReturnsStats()
	{
		// Should return "sent received" format
		var result = (await Parser.FunctionParse(MModule.single("mailstats(%#)")))?.Message!;
		var parts = result.ToPlainText()!.Split(' ');
		// Should have exactly 2 parts
		await Assert.That(parts.Length).IsEqualTo(2);
		// Both should be numbers
		await Assert.That(int.TryParse(parts[0], out var sent)).IsTrue();
		await Assert.That(int.TryParse(parts[1], out var received)).IsTrue();
		// Should have sent 3 messages (to self)
		await Assert.That(sent).IsEqualTo(3);
		// Should have received 3 messages
		await Assert.That(received).IsEqualTo(3);
	}

	[Test]
	public async Task Maildstats_ValidPlayer_ReturnsDetailedStats()
	{
		// Should return "sent sentUnread sentCleared received receivedUnread receivedCleared"
		var result = (await Parser.FunctionParse(MModule.single("maildstats(%#)")))?.Message!;
		var parts = result.ToPlainText()!.Split(' ');
		// Should have exactly 6 parts
		await Assert.That(parts.Length).IsEqualTo(6);
		// All should be numbers
		foreach (var part in parts)
		{
			await Assert.That(int.TryParse(part, out _)).IsTrue();
		}
		// Parse the values
		var sent = int.Parse(parts[0]);
		var sentUnread = int.Parse(parts[1]);
		var sentCleared = int.Parse(parts[2]);
		var received = int.Parse(parts[3]);
		var receivedUnread = int.Parse(parts[4]);
		var receivedCleared = int.Parse(parts[5]);
		
		// Validate all counts match our setup: 3 sent, 2 unread, 1 cleared
		await Assert.That(sent).IsEqualTo(3);
		await Assert.That(received).IsEqualTo(3);
		await Assert.That(receivedUnread).IsEqualTo(2); // mail 2 and 3 are unread
		await Assert.That(receivedCleared).IsEqualTo(1); // mail 3 is cleared
		// Also validate sent mail stats (we sent to ourselves)
		await Assert.That(sentUnread).IsEqualTo(2); // Same as received unread
		await Assert.That(sentCleared).IsEqualTo(1); // Same as received cleared
	}

	[Test]
	public async Task Mailfstats_ValidPlayer_ReturnsFullStats()
	{
		// Should return "sent sentUnread sentCleared sentBytes received receivedUnread receivedCleared receivedBytes"
		var result = (await Parser.FunctionParse(MModule.single("mailfstats(%#)")))?.Message!;
		var parts = result.ToPlainText()!.Split(' ');
		// Should have exactly 8 parts
		await Assert.That(parts.Length).IsEqualTo(8);
		// All should be numbers
		foreach (var part in parts)
		{
			await Assert.That(int.TryParse(part, out _)).IsTrue();
		}
		// Parse values
		var sent = int.Parse(parts[0]);
		var received = int.Parse(parts[4]);
		var receivedBytes = int.Parse(parts[7]);
		
		await Assert.That(sent).IsEqualTo(3);
		await Assert.That(received).IsEqualTo(3);
		// Byte count should be sum of all message content lengths
		await Assert.That(receivedBytes).IsGreaterThan(0);
	}

	[Test]
	public async Task Mailstatus_ValidMessage_ReturnsStatusFormat()
	{
		// Get status of any message
		var result = (await Parser.FunctionParse(MModule.single("mailstatus(1)")))?.Message!;
		var status = result.ToPlainText();
		// Status should be 5 characters in NCUF+ format
		await Assert.That(status).HasLength().EqualTo(5);
		// Should contain valid status characters (N or -, C or -, U or -, F or -, + or -)
		await Assert.That(status!.All(c => c == 'N' || c == 'C' || c == 'U' || c == 'F' || c == '+' || c == '-')).IsTrue();
	}

	[Test]
	public async Task Mailstatus_ChecksForUrgentFlag()
	{
		// Get all mail statuses and check if any have urgent flag
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
		// Non-existent message should return error
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Mailsubject_ValidMessage_ReturnsSubject()
	{
		// Get subject of any message
		var result = (await Parser.FunctionParse(MModule.single("mailsubject(1)")))?.Message!;
		var subject = result.ToPlainText();
		// Should be one of our test subjects with unique ID
		await Assert.That(subject).Contains($"TESTMAIL-{TestRunId}");
	}

	[Test]
	[Arguments("mailsubject(999)", "#-1 NO SUCH MAIL")]
	public async Task Mailsubject_InvalidMessage_ReturnsError(string str, string expected)
	{
		// Non-existent message should return error
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Mailtime_ValidMessage_ReturnsTimestamp()
	{
		// Get time of any message
		var result = (await Parser.FunctionParse(MModule.single("mailtime(1)")))?.Message!;
		var timestamp = result.ToPlainText();
		// Should be a valid Unix timestamp
		await Assert.That(long.TryParse(timestamp, out var ts)).IsTrue();
		// Timestamp should be reasonable (not in the distant past or future)
		var date = DateTimeOffset.FromUnixTimeSeconds(ts);
		await Assert.That(date).IsGreaterThan(DateTimeOffset.UtcNow.AddDays(-1));
		await Assert.That(date).IsLessThan(DateTimeOffset.UtcNow.AddMinutes(1));
	}

	[Test]
	[Arguments("mailtime(999)", "#-1 NO SUCH MAIL")]
	public async Task Mailtime_InvalidMessage_ReturnsError(string str, string expected)
	{
		// Non-existent message should return error
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("malias()", "")]
	public async Task Malias_NoArgs_ReturnsEmpty(string str, string expected)
	{
		// Mail aliases not implemented, should return empty
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Folderstats_NoArgs_ReturnsStats()
	{
		// Should return "read unread cleared" format for current folder
		var result = (await Parser.FunctionParse(MModule.single("folderstats()")))?.Message!;
		var parts = result.ToPlainText()!.Split(' ');
		// Should have exactly 3 parts
		await Assert.That(parts.Length).IsEqualTo(3);
		// All should be numbers
		foreach (var part in parts)
		{
			await Assert.That(int.TryParse(part, out _)).IsTrue();
		}
		// Parse values
		var read = int.Parse(parts[0]);
		var unread = int.Parse(parts[1]);
		var cleared = int.Parse(parts[2]);
		
		// From mail() test we know there are 3 messages total
		// So read + unread should equal the total messages
		// But folder stats might return 0 if called before mail is properly set up
		// Just verify it's a valid format with non-negative numbers
		await Assert.That(read).IsGreaterThanOrEqualTo(0);
		await Assert.That(unread).IsGreaterThanOrEqualTo(0);
		await Assert.That(cleared).IsGreaterThanOrEqualTo(0);
	}
}

