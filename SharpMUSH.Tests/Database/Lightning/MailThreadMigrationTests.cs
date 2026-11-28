using DotNext.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// World format 3: mail stored before conversations gets a message id, shared by the copies of one send, which is
/// also its thread; and the thread index the store reads conversations from.
/// </summary>
public class MailThreadMigrationTests
{
	private readonly string _path = Path.Join(Path.GetTempPath(), "mail-thread-migration-" + Guid.NewGuid().ToString("N"));

	[After(Test)]
	public async Task Cleanup() => await FixtureDirectoryCleanup.DeleteAsync(_path);

	private LightningDatabase Open() => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private static async Task<SharpPlayer> NewPlayer(LightningDatabase db, string name)
		=> (await db.GetObjectNodeAsync(await db.CreatePlayerAsync(name, "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();

	private static SharpMail NewMail(string subject, DateTimeOffset sent, string body = "text") => new()
	{
		DateSent = sent,
		Fresh = true,
		Read = false,
		Tagged = false,
		Urgent = false,
		Forwarded = false,
		Cleared = false,
		Folder = "INBOX",
		Content = MarkupText.Plain(body),
		Subject = MarkupText.Plain(subject),
		From = new AsyncLazy<AnyOptionalSharpObject>(_ => throw new InvalidOperationException("unused on send"))
	};

	private static async Task<long> Send(LightningDatabase db, SharpPlayer from, SharpPlayer to, SharpMail mail)
		=> long.Parse((await db.SendMailAsync(from.Object, to, mail)).Expect<AdmittedMail>().Id.Split('/')[1]);

	private static MailRecord Row(LightningDatabase db, long id)
		=> db.Store.Read(tx => tx.TryGet(Tables.Mail, Keys.Dbref(id), out var bytes) ? db.ReadMail(tx, bytes) : throw new InvalidOperationException());

	[Test]
	public async Task CopiesOfOneSendShareAThreadAndEverythingElseStartsItsOwn()
	{
		var start = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
		long together1, together2, later, otherSender, again, uninvolved;
		await using (var db = Open())
		{
			await db.Migrate();
			var sender = await NewPlayer(db, "MigSender");
			var other = await NewPlayer(db, "MigOther");
			var a = await NewPlayer(db, "MigA");
			var b = await NewPlayer(db, "MigB");

			together1 = await Send(db, sender, a, NewMail("Plans", start));
			otherSender = await Send(db, other, a, NewMail("Plans", start.AddMilliseconds(10)));
			together2 = await Send(db, sender, b, NewMail("Plans", start.AddMilliseconds(20)));
			again = await Send(db, sender, b, NewMail("Plans", start.AddMilliseconds(30)));
			later = await Send(db, sender, a, NewMail("Plans", start.AddSeconds(10)));
			uninvolved = await Send(db, sender, a, NewMail("Other", start.AddSeconds(11)));

			// As a format-2 world left them: no ids, no index, no record of the migration.
			await db.Store.WriteAsync(tx =>
			{
				foreach (var (key, value) in tx.Range(Tables.Mail, []).ToList())
				{
					var record = db.ReadMail(tx, value);
					db.PutMail(tx, key, record with { Message = null, Thread = null, ReplyTo = null, Addressed = null });
				}

				tx.DeletePrefix(Tables.MailThread, []);
				tx.Delete(Tables.Meta, Keys.Str("mig:" + LightningDatabase.MailThreadMigrationId));
			});
		}

		await using (var db = Open())
		{
			await db.Migrate();

			var send = Row(db, together1).Message;
			await Assert.That(send).IsNotNull();
			await Assert.That(Row(db, together2).Message).IsEqualTo(send);
			await Assert.That(Row(db, together1).Thread).IsEqualTo(send);
			await Assert.That(Row(db, otherSender).Message).IsNotEqualTo(send);
			await Assert.That(Row(db, again).Message).IsNotEqualTo(send)
				.Because("a second copy to a mailbox the send already reached is another send");
			await Assert.That(Row(db, later).Message).IsNotEqualTo(send);
			await Assert.That(Row(db, uninvolved).Message).IsNotEqualTo(Row(db, later).Message);

			var sender = (await db.GetObjectNodeAsync(new DBRef((int)Row(db, together1).Sender))).Expect<SharpPlayer>();
			var thread = await db.GetMailThreadAsync(sender.Object, send!.Value).ToListAsync();
			await Assert.That(thread.Select(copy => copy.Mail.Id ?? string.Empty)).IsEquivalentTo([$"Mail/{together1}", $"Mail/{together2}"]);
			await Assert.That(thread.All(copy => copy.Sent && !copy.Held)).IsTrue();
		}
	}

	/// <summary>
	/// An imported date before 1970 still gets an id, and copies from two players who held one number in turn are two
	/// sends even when everything else about them matches.
	/// </summary>
	[Test]
	public async Task OldDatesAndAReusedSenderNumberAreHandled()
	{
		var start = new DateTimeOffset(1965, 3, 1, 12, 0, 0, TimeSpan.Zero);
		long first, second;
		await using (var db = Open())
		{
			await db.Migrate();
			var sender = await NewPlayer(db, "OldSender");
			var a = await NewPlayer(db, "OldA");
			var b = await NewPlayer(db, "OldB");

			first = await Send(db, sender, a, NewMail("Plans", start));
			second = await Send(db, sender, b, NewMail("Plans", start.AddMilliseconds(10)));

			await db.Store.WriteAsync(tx =>
			{
				foreach (var (key, value) in tx.Range(Tables.Mail, []).ToList())
				{
					var record = db.ReadMail(tx, value);
					var reused = Keys.ReadDbref(key) == second ? record.SenderCreationTime + 1 : record.SenderCreationTime;
					db.PutMail(tx, key, record with
					{
						Message = null,
						Thread = null,
						ReplyTo = null,
						Addressed = null,
						SenderCreationTime = reused
					});
				}

				tx.DeletePrefix(Tables.MailThread, []);
				tx.Delete(Tables.Meta, Keys.Str("mig:" + LightningDatabase.MailThreadMigrationId));
			});
		}

		await using (var db = Open())
		{
			await db.Migrate();

			await Assert.That(Row(db, first).Message).IsNotNull();
			await Assert.That(Row(db, second).Message).IsNotNull();
			await Assert.That(Row(db, second).Message).IsNotEqualTo(Row(db, first).Message)
				.Because("the second copy came from another player who held the same number");
		}
	}

	[Test]
	public async Task DeletingACopyTakesItOutOfItsThread()
	{
		await using var db = Open();
		await db.Migrate();
		var sender = await NewPlayer(db, "DelSender");
		var a = await NewPlayer(db, "DelA");
		var mail = NewMail("Plans", DateTimeOffset.UtcNow);
		mail.Message = mail.Thread = Guid.CreateVersion7();
		var id = await Send(db, sender, a, mail);

		await db.DeleteMailAsync($"Mail/{id}");

		await Assert.That(db.Store.Read(tx => tx.Range(Tables.MailThread, mail.Thread!.Value.ToByteArray(bigEndian: true)).Count())).IsEqualTo(0);
	}

	[Test]
	public async Task AReaderSeesOnlyTheCopiesTheySentOrHold()
	{
		await using var db = Open();
		await db.Migrate();
		var sender = await NewPlayer(db, "ViewSender");
		var a = await NewPlayer(db, "ViewA");
		var b = await NewPlayer(db, "ViewB");
		var outsider = await NewPlayer(db, "ViewOut");
		var thread = Guid.CreateVersion7();
		foreach (var to in new[] { a, b })
		{
			var mail = NewMail("Plans", DateTimeOffset.UtcNow);
			(mail.Message, mail.Thread) = (thread, thread);
			await Send(db, sender, to, mail);
		}

		await Assert.That(await db.GetMailThreadAsync(a.Object, thread).CountAsync()).IsEqualTo(1);
		await Assert.That(await db.GetMailThreadAsync(sender.Object, thread).CountAsync()).IsEqualTo(2);
		await Assert.That(await db.GetMailThreadAsync(outsider.Object, thread).CountAsync()).IsEqualTo(0);
	}
}
