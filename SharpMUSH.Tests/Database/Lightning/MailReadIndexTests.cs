using DotNext.Threading;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using TUnit.Assertions.Enums;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Single-mail reads and folder discovery through the folder and sent-to indexes (#1463): the same
/// ordinals and folder order as before, after moves, renames and deletes, without decoding the other
/// mail in the collection.
/// </summary>
public class MailReadIndexTests : LightningDatabaseFixture
{
	private async Task<SharpPlayer> NewPlayer(string name)
		=> (await Db.GetObjectNodeAsync(await Db.CreatePlayerAsync(name, "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();

	private static SharpMail NewMail(string subject, string folder = "INBOX") => new()
	{
		DateSent = DateTimeOffset.UtcNow,
		Fresh = true,
		Read = false,
		Tagged = false,
		Urgent = false,
		Forwarded = false,
		Cleared = false,
		Folder = folder,
		Content = MarkupText.Plain("body of " + subject),
		Subject = MarkupText.Plain(subject),
		From = new AsyncLazy<AnyOptionalSharpObject>(_ => throw new InvalidOperationException("unused on send"))
	};

	private async Task<string> Send(SharpPlayer from, SharpPlayer to, string subject, string folder = "INBOX")
		=> (await Db.SendMailAsync(from.Object, to, NewMail(subject, folder))).Expect<AdmittedMail>().Id;

	private async Task<string?> Nth(SharpPlayer to, string folder, int n)
		=> (await Db.GetIncomingMailAsync(to, folder, n))?.Subject.ToPlainText();

	private async Task<string[]> Listed(SharpPlayer to, string folder)
		=> (await Db.GetIncomingMailsAsync(to, folder).ToListAsync()).Select(m => m.Subject.ToPlainText()).ToArray();

	/// <summary>Writes bytes that are not a mail record over a row, so any read that decodes it throws.</summary>
	private async Task Corrupt(string mailId)
		=> await Db.Store.WriteAsync(tx => tx.Put(Tables.Mail, Keys.Dbref(long.Parse(mailId.Split('/')[1])), "not json"u8));

	private long Held(SharpPlayer to, string folder)
		=> Db.Store.Read(tx => tx.TryGet(Tables.MailCount, Keys.Concat(Keys.Dbref(to.Object.DBRef.Number), Keys.Str(folder)), out var v)
			? Keys.ReadDbref(v)
			: 0);

	/// <summary>
	/// The summary listing reports what the full listing does, message for message, and never decodes a
	/// body: a row whose body is not even a string still lists, while the full read of it throws.
	/// </summary>
	[Test]
	public async Task SummariesMatchTheFullListingWithoutDecodingBodies()
	{
		var sender = await NewPlayer("SumSender");
		var other = await NewPlayer("SumOther");
		var to = await NewPlayer("SumRecipient");
		var ids = new List<string>
		{
			await Send(sender, to, "first"),
			await Send(other, to, "elsewhere", "WORK"),
			await Send(other, to, "second"),
			await Send(sender, to, "third")
		};
		await Db.UpdateMailAsync(ids[2], MailUpdate.ReadEdit(true));
		await Db.DeleteMailAsync(ids[0]);

		var full = await Db.GetIncomingMailsAsync(to, "INBOX").ToListAsync();
		var summaries = await Db.GetIncomingMailSummariesAsync(to, "INBOX").ToListAsync();

		await Assert.That(summaries.Select(m => m.Id)).IsEquivalentTo(full.Select(m => m.Id!), CollectionOrdering.Matching);
		for (var i = 0; i < full.Count; i++)
		{
			var name = (await full[i].From.WithCancellation(CancellationToken.None)).Object()?.Name;
			await Assert.That(summaries[i]).IsEqualTo(new MailSummary(full[i].Id!, full[i].DateSent, full[i].Read, full[i].Urgent,
				full[i].Folder, summaries[i].Subject, name));
			await Assert.That(summaries[i].Subject.ToPlainText()).IsEqualTo(full[i].Subject.ToPlainText());
		}

		await Assert.That(summaries.Select(m => m.SenderName ?? "")).IsEquivalentTo(["SumOther", "SumSender"], CollectionOrdering.Matching);
		await Assert.That(summaries[0].Read).IsTrue();

		var key = Keys.Dbref(long.Parse(ids[3].Split('/')[1]));
		await Db.Store.WriteAsync(tx =>
		{
			tx.TryGet(Tables.Mail, key, out var bytes);
			var row = System.Text.Json.Nodes.JsonNode.Parse(bytes)!.AsObject();
			var content = row.First(p => p.Key.Equals("Content", StringComparison.OrdinalIgnoreCase)).Key;
			row[content] = new System.Text.Json.Nodes.JsonObject { ["not"] = "a body" };
			tx.Put(Tables.Mail, key, System.Text.Encoding.UTF8.GetBytes(row.ToJsonString()));
		});

		await Assert.That(async () => await Db.GetIncomingMailsAsync(to, "INBOX").ToListAsync()).Throws<Exception>();
		await Assert.That((await Db.GetIncomingMailSummariesAsync(to, "INBOX").ToListAsync()).Select(m => m.Subject.ToPlainText()))
			.IsEquivalentTo(["second", "third"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task OrdinalsFollowMovesAndDeletes()
	{
		var sender = await NewPlayer("OrdSender");
		var to = await NewPlayer("OrdRecipient");
		var ids = new List<string>();
		for (var i = 0; i < 5; i++) ids.Add(await Send(sender, to, $"m{i}"));

		await Db.MoveMailFolderAsync(ids[1], "WORK");
		await Db.DeleteMailAsync(ids[3]);

		await Assert.That(await Listed(to, "INBOX")).IsEquivalentTo(["m0", "m2", "m4"], CollectionOrdering.Matching);
		await Assert.That(await Nth(to, "INBOX", 0)).IsEqualTo("m0");
		await Assert.That(await Nth(to, "INBOX", 1)).IsEqualTo("m2");
		await Assert.That(await Nth(to, "INBOX", 2)).IsEqualTo("m4");
		await Assert.That(await Nth(to, "INBOX", 3)).IsNull();
		await Assert.That(await Nth(to, "INBOX", -1)).IsNull();
		await Assert.That(await Nth(to, "WORK", 0)).IsEqualTo("m1");
		await Assert.That(Held(to, "INBOX")).IsEqualTo(3L);
		await Assert.That(Held(to, "WORK")).IsEqualTo(1L);

		// Moving back slots it in by arrival, not at the end.
		await Db.MoveMailFolderAsync(ids[1], "INBOX");
		await Assert.That(await Listed(to, "INBOX")).IsEquivalentTo(["m0", "m1", "m2", "m4"], CollectionOrdering.Matching);
		await Assert.That(await Nth(to, "INBOX", 1)).IsEqualTo("m1");
		await Assert.That(Held(to, "INBOX")).IsEqualTo(4L);
		await Assert.That(Held(to, "WORK")).IsEqualTo(0L);
	}

	[Test]
	public async Task RenameMovesTheWholeFolderAndItsCount()
	{
		var sender = await NewPlayer("RenSender");
		var to = await NewPlayer("RenRecipient");
		await Send(sender, to, "a", "OLD");
		await Send(sender, to, "b", "KEEP");
		await Send(sender, to, "c", "OLD");

		await Db.RenameMailFolderAsync(to, "OLD", "NEW");

		await Assert.That(await Listed(to, "OLD")).IsEmpty();
		await Assert.That(await Listed(to, "NEW")).IsEquivalentTo(["a", "c"], CollectionOrdering.Matching);
		await Assert.That(await Nth(to, "NEW", 1)).IsEqualTo("c");
		await Assert.That(Held(to, "NEW")).IsEqualTo(2L);
		await Assert.That(Held(to, "OLD")).IsEqualTo(0L);
		await Assert.That(await Db.GetMailFoldersAsync(to)).IsEquivalentTo(["NEW", "KEEP"], CollectionOrdering.Matching);
	}

	/// <summary>Folders come back in the order their oldest mail arrived, as the box scan reported them.</summary>
	[Test]
	public async Task FoldersComeBackInArrivalOrder()
	{
		var sender = await NewPlayer("FolderSender");
		var to = await NewPlayer("FolderRecipient");
		var zeta = await Send(sender, to, "z1", "ZETA");
		await Send(sender, to, "a1", "ALPHA");
		await Send(sender, to, "i1", "INBOX");
		await Send(sender, to, "a2", "ALPHA");
		await Send(sender, to, "AB", "AB");
		await Send(sender, to, "blank", "");

		await Assert.That(await Db.GetMailFoldersAsync(to)).IsEquivalentTo(["ZETA", "ALPHA", "INBOX", "AB"], CollectionOrdering.Matching);

		await Db.MoveMailFolderAsync(zeta, "ALPHA");
		await Assert.That(await Db.GetMailFoldersAsync(to)).IsEquivalentTo(["ALPHA", "INBOX", "AB"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task SentMailOrdinalsArePerRecipient()
	{
		var sender = await NewPlayer("SentSender");
		var one = await NewPlayer("SentOne");
		var two = await NewPlayer("SentTwo");
		await Send(sender, one, "s0");
		await Send(sender, two, "s1");
		var deleted = await Send(sender, one, "s2");
		await Send(sender, one, "s3");
		await Db.DeleteMailAsync(deleted);

		var toOne = (await Db.GetSentMailsAsync(sender.Object, one).ToListAsync()).Select(m => m.Subject.ToPlainText());
		await Assert.That(toOne).IsEquivalentTo(["s0", "s3"], CollectionOrdering.Matching);
		await Assert.That((await Db.GetSentMailAsync(sender.Object, one, 1))?.Subject.ToPlainText()).IsEqualTo("s3");
		await Assert.That((await Db.GetSentMailAsync(sender.Object, two, 0))?.Subject.ToPlainText()).IsEqualTo("s1");
		await Assert.That(await Db.GetSentMailAsync(sender.Object, two, 1)).IsNull();
	}

	/// <summary>Proof only the selected mail is decoded: the others can be unreadable and the read still answers.</summary>
	[Test]
	public async Task SingleReadsDecodeOnlyTheSelectedMail()
	{
		var sender = await NewPlayer("BoundSender");
		var to = await NewPlayer("BoundRecipient");
		var first = await Send(sender, to, "first");
		var second = await Send(sender, to, "second");
		await Send(sender, to, "third", "WORK");
		await Corrupt(first);
		await Corrupt(second);

		await Assert.That((await Db.GetIncomingMailAsync(to, "INBOX", 2))).IsNull();
		await Assert.That(await Nth(to, "WORK", 0)).IsEqualTo("third");
		await Assert.That(await Db.GetMailFoldersAsync(to)).IsEquivalentTo(["INBOX", "WORK"], CollectionOrdering.Matching);
		await Assert.That((await Db.GetSentMailAsync(sender.Object, to, 2))?.Subject.ToPlainText()).IsEqualTo("third");
	}

	[Test]
	public async Task DeletingAPlayerDropsItsIndexEntries()
	{
		var sender = await NewPlayer("CascadeSender");
		var to = await NewPlayer("CascadeRecipient");
		var other = await NewPlayer("CascadeOther");
		await Send(sender, to, "doomed");
		await Send(to, other, "survives");

		await Db.DeleteObjectAsync(to.Object.DBRef);

		var recipientKey = Keys.Dbref(to.Object.DBRef.Number);
		await Assert.That(Db.Store.Read(tx => tx.Range(Tables.MailFolder, recipientKey).Count())).IsEqualTo(0);
		await Assert.That(Db.Store.Read(tx => tx.Range(Tables.MailSentTo, recipientKey).Count())).IsEqualTo(0);
		await Assert.That(Db.Store.Read(tx => tx.Range(Tables.MailSentTo, Keys.Dbref(sender.Object.DBRef.Number)).Count())).IsEqualTo(0);
		// Mail the deleted player sent stays in the other mailbox, at its ordinal.
		await Assert.That(await Nth(other, "INBOX", 0)).IsEqualTo("survives");
	}
}
