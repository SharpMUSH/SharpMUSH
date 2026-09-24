using System.Text;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The messages in the maildb the same PennMUSH 1.8.8 game wrote beside
/// <see cref="PennMUSHDbrefPreservationTests"/>'s dump: eleven messages between One (#1), Alice (#3),
/// Bob (#4) and Carol (#5), read, urgent, tagged, forwarded, and one filed in Bob's folder 1, which Bob's MAILFOLDERS names ARCHIVE.
/// </summary>
public class PennMUSHMailImportTests
{
	private static readonly string MailFixturePath =
		Path.Join(AppContext.BaseDirectory, "Services", "TestData", "pennmush-1.8.8p0-holes.maildb");

	[Test]
	public async Task EveryMessageIsRead()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var mail = await world.Parser.ParseMailFileAsync(MailFixturePath);

		await Assert.That(mail.MessageReadError).IsNull();
		await Assert.That(mail.Messages.Count).IsEqualTo(11);
		await Assert.That(mail.Messages[5]).IsEqualTo(new PennMUSHMailMessage(4, 3, 1790269200, "Thu Sep 24 12:01:44 2026",
			"Second note", "Second body with \nnewline", 256));
		await Assert.That(mail.Messages[5].Folder).IsEqualTo(1);
	}

	/// <summary>
	/// Through the path the upload endpoint takes. Every message lands in its recipient's mailbox under the
	/// dbrefs the dump preserved, from its sender, with its subject, body, time, flags and folder, in the
	/// order PennMUSH lists them.
	/// </summary>
	[Test]
	public async Task EveryMessageArrivesAsPennMUSHHadIt()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(PennMUSHDbrefPreservationTests.FixturePath,
			MailFixturePath, new Progress<ConversionProgress>());

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.MailMessagesConverted).IsEqualTo(11);
		await Assert.That(result.Warnings).DoesNotContain(w => w.Contains("mail message", StringComparison.OrdinalIgnoreCase));

		await Assert.That(await MailboxAsync(world, 3)).IsEquivalentTo(new[]
		{
			"#5|Team meeting|Meeting at #11 tonight|INBOX|",
			"#4|Re: Welcome|Thanks Alice!|INBOX|read",
			"#1|Staff notice|From god: see #12|INBOX|",
			"#3|Alias test|Sent through the alias|INBOX|"
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);

		await Assert.That(await MailboxAsync(world, 4)).IsEquivalentTo(new[]
		{
			"#3|Welcome Bob|Hello Bob, welcome to the game. See #9 and #11.|INBOX|read",
			"#3|Second note|Second body with \nnewline|ARCHIVE|",
			"#3|Urgent thing|Please read #6 now|INBOX|urgent",
			"#5|Team meeting|Meeting at #11 tonight|INBOX|tagged",
			"#3|Alias test|Sent through the alias|INBOX|"
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);

		await Assert.That(await MailboxAsync(world, 5)).IsEquivalentTo(new[]
		{
			"#3|Fwd: Team meeting|Meeting at #11 tonight|INBOX|forwarded",
			"#3|Alias test|Sent through the alias|INBOX|"
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);

		// asctime in the game's local time, read back in local time as load_mail reads it.
		var bob = (await PennMUSHDbrefPreservationTests.NodeAsync(world, 4)).Expect<SharpPlayer>();
		var welcome = (await world.Mediator.CreateStream(new GetAllMailListQuery(bob)).ToListAsync())[0];
		await Assert.That(welcome.DateSent).IsEqualTo(new DateTimeOffset(new DateTime(2026, 9, 24, 12, 1, 43, DateTimeKind.Local)));

		// An unread message can still be retracted; a read one cannot.
		await Assert.That(welcome.Fresh).IsFalse();
		var alice = (await PennMUSHDbrefPreservationTests.NodeAsync(world, 3)).Expect<SharpPlayer>();
		await Assert.That((await world.Mediator.CreateStream(new GetAllSentMailListQuery(alice.Object)).ToListAsync()).Count)
			.IsEqualTo(7);
	}

	/// <summary>
	/// @mail/debug fix, which load_mail runs: a message to anything but a player is dropped, and one from a
	/// sender that no longer exists is from #0. Here one goes to #6, the Widget thing, and one comes from #7,
	/// a hole in the dump; a third names Alice with a creation time that is not hers.
	/// </summary>
	[Test]
	public async Task BadRecipientsAndSendersAreReported()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var database = await world.Parser.ParseFileAsync(PennMUSHDbrefPreservationTests.FixturePath);
		const string maildb = "+15\n0\n\"*** End of MALIAS ***\"\n3\n" +
			"4\n7\n1790269200\n\"Thu Sep 24 12:01:43 2026\"\n\"Ghost\"\n\"From a hole\"\n0\n" +
			"6\n3\n1790269200\n\"Thu Sep 24 12:01:43 2026\"\n\"Thing\"\n\"To a thing\"\n0\n" +
			"4\n3\n1\n\"Thu Sep 24 12:01:43 2026\"\n\"Old Alice\"\n\"From a recycled dbref\"\n0\n" +
			"***END OF DUMP***\n";
		database.Mail = await world.Parser.ParseMailAsync(new MemoryStream(Encoding.UTF8.GetBytes(maildb)));

		var result = await world.Converter.ConvertDatabaseAsync(database);

		await Assert.That(result.MailMessagesConverted).IsEqualTo(2);
		await Assert.That(result.Warnings).Contains(w => w.StartsWith("1 mail message(s) not imported") && w.Contains("#6: 1"));
		await Assert.That(result.Warnings).Contains(w => w.StartsWith("1 mail message(s) from a sender that was not imported") && w.Contains("#7: 1"));
		await Assert.That(result.Warnings).Contains(w => w.StartsWith("1 mail message(s) name a sender whose creation time") && w.Contains("#3: 1"));

		await Assert.That(await MailboxAsync(world, 4)).IsEquivalentTo(new[]
		{
			"#0|Ghost|From a hole|INBOX|",
			"#3|Old Alice|From a recycled dbref|INBOX|"
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>A folder the recipient named in MAILFOLDERS keeps that name; a message cut off is reported, the rest kept.</summary>
	[Test]
	public async Task NamedFoldersAndATruncatedMaildb()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var database = await world.Parser.ParseFileAsync(PennMUSHDbrefPreservationTests.FixturePath);
		database.GetObject(4)!.Attributes.RemoveAll(a => a.Name == "MAILFOLDERS");
		database.GetObject(4)!.Attributes.Add(new PennMUSHAttribute { Name = "MAILFOLDERS", Value = "0:INBOX:0 2:SAVED:2 " });
		const string maildb = "+15\n0\n\"*** End of MALIAS ***\"\n2\n" +
			"4\n3\n1790269200\n\"Thu Sep  4 09:00:00 2026\"\n\"Keep\"\n\"Filed\"\n513\n" +
			"4\n3\n1790269200\n\"Thu Sep";
		database.Mail = await world.Parser.ParseMailAsync(new MemoryStream(Encoding.UTF8.GetBytes(maildb)));

		var result = await world.Converter.ConvertDatabaseAsync(database);

		await Assert.That(result.MailMessagesConverted).IsEqualTo(1);
		await Assert.That(result.Warnings).Contains(w => w.StartsWith("The maildb holds 2 mail message(s) but only 1 could be read"));
		await Assert.That(await MailboxAsync(world, 4)).IsEquivalentTo(new[] { "#3|Keep|Filed|SAVED|read" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	private static async Task<string[]> MailboxAsync(IsolatedImportWorld world, int dbref)
	{
		var player = (await PennMUSHDbrefPreservationTests.NodeAsync(world, dbref)).Expect<SharpPlayer>();
		var mails = await world.Mediator.CreateStream(new GetAllMailListQuery(player)).ToListAsync();
		var described = new List<string>();
		foreach (var mail in mails)
		{
			var from = await mail.From.WithCancellation(CancellationToken.None);
			var flags = new[] { (mail.Read, "read"), (mail.Urgent, "urgent"), (mail.Tagged, "tagged"), (mail.Forwarded, "forwarded"), (mail.Cleared, "cleared") }
				.Where(f => f.Item1).Select(f => f.Item2);
			described.Add($"#{from.Object()!.DBRef.Number}|{mail.Subject.ToPlainText()}|{mail.Content.ToPlainText()}|{mail.Folder}|{string.Join(",", flags)}");
		}

		return [.. described];
	}
}
