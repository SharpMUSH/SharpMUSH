using DotNext.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// <see cref="IMailStore"/> against the Lightning provider directly — no NATS, no
/// <c>ServerWebAppFactory</c>. Complements <c>MailCommandTests</c> (which runs through the mediator and
/// the command parser) by asserting the LMDB encoding itself: <c>Tables.MailBox</c> positional ordering
/// within a folder, <c>Tables.MailSent</c> lookup, folder rename and distinct listing, and that deleting
/// an object removes both its received mail and its sent-index entries.
/// </summary>
public class MailTests
{
	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private string _path = null!;
	private LightningDatabase _db = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		if (Directory.Exists(_path))
		{
			try
			{
				Directory.Delete(_path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort: a lingering LMDB lock file (mdb.lck) can outlive the writer thread's
				// join by a few milliseconds under load. Leaving the temp directory behind costs
				// disk, not correctness — matches MigrationTests' own cleanup.
			}
		}
	}

	private async Task<SharpPlayer> NewPlayer(string name)
		=> (await _db.GetObjectNodeAsync(await _db.CreatePlayerAsync(name, "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();

	private static SharpMail NewMail(string subject, string content, string folder = "INBOX") => new()
	{
		DateSent = DateTimeOffset.UtcNow,
		Fresh = true,
		Read = false,
		Tagged = false,
		Urgent = false,
		Forwarded = false,
		Cleared = false,
		Folder = folder,
		Content = MarkupText.Plain(content),
		Subject = MarkupText.Plain(subject),
		From = new AsyncLazy<AnyOptionalSharpObject>(_ => throw new InvalidOperationException("unused on send"))
	};

	[Test]
	public async Task SendMailAsyncStoresTheMailAndListsItForTheRecipient()
	{
		var sender = await NewPlayer("MailSenderOne");
		var recipient = await NewPlayer("MailRecipientOne");

		await _db.SendMailAsync(sender.Object, recipient, NewMail("Hello", "World"));

		var mails = new List<SharpMail>();
		await foreach (var mail in _db.GetIncomingMailsAsync(recipient, "INBOX"))
		{
			mails.Add(mail);
		}

		await Assert.That(mails).Count().IsEqualTo(1);
		await Assert.That(mails[0].Subject.ToPlainText()).IsEqualTo("Hello");
		await Assert.That(mails[0].Content.ToPlainText()).IsEqualTo("World");

		var from = (await mails[0].From.WithCancellation(CancellationToken.None)).Expect<SharpPlayer>();
		await Assert.That(from.Object.DBRef.Number).IsEqualTo(sender.Object.DBRef.Number);
	}

	/// <summary>
	/// #1226: the store answers the number the message has in its folder as of the write that stored it, so
	/// a delete committed before the send is already counted.
	/// </summary>
	[Test]
	public async Task SendMailAsyncAnswersTheNumberTheMessageHasInItsFolder()
	{
		var sender = await NewPlayer("MailNumberSender");
		var recipient = await NewPlayer("MailNumberRecipient");

		var first = (await _db.SendMailAsync(sender.Object, recipient, NewMail("One", "a"))).Expect<AdmittedMail>();
		var saved = (await _db.SendMailAsync(sender.Object, recipient, NewMail("Saved", "b", "SAVED"))).Expect<AdmittedMail>();
		var second = (await _db.SendMailAsync(sender.Object, recipient, NewMail("Two", "c"))).Expect<AdmittedMail>();
		await _db.DeleteMailAsync(first.Id);
		var third = (await _db.SendMailAsync(sender.Object, recipient, NewMail("Three", "d"))).Expect<AdmittedMail>();

		await Assert.That(first.Number).IsEqualTo(1);
		await Assert.That(saved.Number).IsEqualTo(1);
		await Assert.That(second.Number).IsEqualTo(2);
		await Assert.That(third.Number).IsEqualTo(2);
		await Assert.That((await _db.GetIncomingMailAsync(recipient, "INBOX", 1))!.Id).IsEqualTo(third.Id);
	}

	/// <summary>#1226: a folder holding its limit refuses the message in the write, storing nothing.</summary>
	[Test]
	public async Task SendMailAsyncRefusesAFolderThatHoldsItsLimit()
	{
		var sender = await NewPlayer("MailLimitSender");
		var recipient = await NewPlayer("MailLimitRecipient");

		var kept = await _db.SendMailAsync(sender.Object, recipient, NewMail("Kept", "a"), limit: 1);
		var elsewhere = await _db.SendMailAsync(sender.Object, recipient, NewMail("Elsewhere", "b", "SAVED"), limit: 1);
		var refused = await _db.SendMailAsync(sender.Object, recipient, NewMail("Refused", "c"), limit: 1);
		var unlimited = await _db.SendMailAsync(sender.Object, recipient, NewMail("Unlimited", "d"));

		await Assert.That(kept.Expect<AdmittedMail>().Number).IsEqualTo(1);
		await Assert.That(elsewhere.Expect<AdmittedMail>().Number).IsEqualTo(1);
		refused.Expect<MailboxFull>();
		await Assert.That(unlimited.Expect<AdmittedMail>().Number).IsEqualTo(2);

		var subjects = new List<string>();
		await foreach (var mail in _db.GetAllIncomingMailsAsync(recipient))
		{
			subjects.Add(mail.Subject.ToPlainText());
		}

		await Assert.That(subjects).IsEquivalentTo(["Kept", "Elsewhere", "Unlimited"]);
		await Assert.That(await _db.GetAllSentMailsAsync(sender.Object).CountAsync()).IsEqualTo(3);
	}

	/// <summary>
	/// #1226: sends racing one mailbox each get their own number and a limit is never overrun, with no
	/// gate outside the store.
	/// </summary>
	[Test]
	public async Task ConcurrentSendsAreNumberedApartAndCannotOverrunTheLimit()
	{
		var sender = await NewPlayer("MailRaceSender");
		var recipient = await NewPlayer("MailRaceRecipient");

		var admitted = await Task.WhenAll(Enumerable.Range(0, 16).Select(i =>
			_db.SendMailAsync(sender.Object, recipient, NewMail($"Race{i}", "x"), limit: 10).AsTask()));

		await Assert.That(admitted.Select(a => a is AdmittedMail stored ? stored.Number : 0).Where(n => n > 0))
			.IsEquivalentTo(Enumerable.Range(1, 10));
		await Assert.That(await _db.GetIncomingMailsAsync(recipient, "INBOX").CountAsync()).IsEqualTo(10);
	}

	[Test]
	public async Task PositionalAccessCountsWithinTheFolderOnly()
	{
		var sender = await NewPlayer("MailSenderTwo");
		var recipient = await NewPlayer("MailRecipientTwo");

		await _db.SendMailAsync(sender.Object, recipient, NewMail("Inbox One", "a", "INBOX"));
		await _db.SendMailAsync(sender.Object, recipient, NewMail("Saved One", "b", "SAVED"));
		await _db.SendMailAsync(sender.Object, recipient, NewMail("Inbox Two", "c", "INBOX"));

		var first = await _db.GetIncomingMailAsync(recipient, "INBOX", 0);
		var second = await _db.GetIncomingMailAsync(recipient, "INBOX", 1);
		var outOfRange = await _db.GetIncomingMailAsync(recipient, "INBOX", 2);
		var saved = await _db.GetIncomingMailAsync(recipient, "SAVED", 0);

		await Assert.That(first).IsNotNull();
		await Assert.That(first!.Subject.ToPlainText()).IsEqualTo("Inbox One");
		await Assert.That(second).IsNotNull();
		await Assert.That(second!.Subject.ToPlainText()).IsEqualTo("Inbox Two");
		await Assert.That(outOfRange).IsNull();
		await Assert.That(saved).IsNotNull();
		await Assert.That(saved!.Subject.ToPlainText()).IsEqualTo("Saved One");
	}

	[Test]
	public async Task GetMailFoldersAsyncReturnsDistinctFoldersOnly()
	{
		var sender = await NewPlayer("MailSenderThree");
		var recipient = await NewPlayer("MailRecipientThree");

		await _db.SendMailAsync(sender.Object, recipient, NewMail("Inbox One", "a", "INBOX"));
		await _db.SendMailAsync(sender.Object, recipient, NewMail("Inbox Two", "b", "INBOX"));
		await _db.SendMailAsync(sender.Object, recipient, NewMail("Saved One", "c", "SAVED"));

		var folders = await _db.GetMailFoldersAsync(recipient);

		await Assert.That(folders).IsEquivalentTo(["INBOX", "SAVED"]);
	}

	[Test]
	public async Task RenameMailFolderAsyncRewritesEveryRecordInThatFolder()
	{
		var sender = await NewPlayer("MailSenderFour");
		var recipient = await NewPlayer("MailRecipientFour");

		await _db.SendMailAsync(sender.Object, recipient, NewMail("Inbox One", "a", "INBOX"));
		await _db.SendMailAsync(sender.Object, recipient, NewMail("Inbox Two", "b", "INBOX"));
		await _db.SendMailAsync(sender.Object, recipient, NewMail("Saved One", "c", "SAVED"));

		await _db.RenameMailFolderAsync(recipient, "INBOX", "ARCHIVE");

		var folders = await _db.GetMailFoldersAsync(recipient);
		await Assert.That(folders).IsEquivalentTo(["ARCHIVE", "SAVED"]);

		var archived = new List<SharpMail>();
		await foreach (var mail in _db.GetIncomingMailsAsync(recipient, "ARCHIVE"))
		{
			archived.Add(mail);
		}
		await Assert.That(archived).Count().IsEqualTo(2);
	}

	[Test]
	public async Task SentMailLookupFindsMailBySenderAndRecipient()
	{
		var sender = await NewPlayer("MailSenderFive");
		var recipientA = await NewPlayer("MailRecipientFiveA");
		var recipientB = await NewPlayer("MailRecipientFiveB");

		await _db.SendMailAsync(sender.Object, recipientA, NewMail("To A", "a"));
		await _db.SendMailAsync(sender.Object, recipientB, NewMail("To B", "b"));

		var toA = await _db.GetSentMailAsync(sender.Object, recipientA, 0);
		var toAAgain = await _db.GetSentMailAsync(sender.Object, recipientA, 1);

		await Assert.That(toA).IsNotNull();
		await Assert.That(toA!.Subject.ToPlainText()).IsEqualTo("To A");
		await Assert.That(toAAgain).IsNull();

		var allSent = new List<SharpMail>();
		await foreach (var mail in _db.GetAllSentMailsAsync(sender.Object))
		{
			allSent.Add(mail);
		}
		await Assert.That(allSent).Count().IsEqualTo(2);
	}

	[Test]
	public async Task DeletingTheRecipientRemovesItsReceivedMailAndSentIndexEntries()
	{
		var sender = await NewPlayer("MailSenderSix");
		var recipient = await NewPlayer("MailRecipientSix");
		var recipientDbref = recipient.Object.DBRef;
		var recipientKey = (long)recipient.Object.Key;
		var senderKey = (long)sender.Object.Key;

		await _db.SendMailAsync(sender.Object, recipient, NewMail("Doomed", "x"));

		var mailIdBeforeDelete = _db.Store.Read(tx =>
			tx.Range(Tables.MailBox, Keys.Dbref(recipientKey)).Select(e => Keys.ReadDbref(e.Key.AsSpan(e.Key.Length - 8, 8))).Single());

		await _db.DeleteObjectAsync(recipientDbref);

		var leftovers = _db.Store.Read(tx =>
			tx.Range(Tables.MailBox, Keys.Dbref(recipientKey)).Count()
			+ tx.Range(Tables.MailSent, Keys.Dbref(senderKey)).Count()
			+ (tx.TryGet(Tables.Mail, Keys.Dbref(mailIdBeforeDelete), out _) ? 1 : 0));

		await Assert.That(leftovers).IsEqualTo(0);
	}

	[Test]
	public async Task DeletingTheSenderLeavesTheMailReadableWithNoSender()
	{
		var sender = await NewPlayer("MailSenderSeven");
		var recipient = await NewPlayer("MailRecipientSeven");
		var senderKey = (long)sender.Object.Key;

		await _db.SendMailAsync(sender.Object, recipient, NewMail("Still here", "x"));

		await _db.DeleteObjectAsync(sender.Object.DBRef);

		var sentIndexLeftovers = _db.Store.Read(tx => tx.Range(Tables.MailSent, Keys.Dbref(senderKey)).Count());
		await Assert.That(sentIndexLeftovers).IsEqualTo(0);

		var mail = await _db.GetIncomingMailAsync(recipient, "INBOX", 0);
		await Assert.That(mail).IsNotNull();

		var from = await mail!.From.WithCancellation(CancellationToken.None);
		await Assert.That(from.IsNone).IsTrue();
	}
}
