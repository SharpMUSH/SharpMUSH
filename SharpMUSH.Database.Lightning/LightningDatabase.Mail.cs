using System.Runtime.CompilerServices;
using DotNext.Threading;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IMailStore"/>: @mail folders, incoming and sent mail. <see cref="Tables.Mail"/> is keyed by a dedicated <c>next_mail</c>
/// counter (mirroring <see cref="AllocateDbref"/> but its own key, since mail ids and dbrefs are
/// unrelated sequences); <see cref="Tables.MailBox"/> and <see cref="Tables.MailSent"/> are ordered
/// per-recipient / per-sender indexes (recipient-or-sender dbref + mail id -> empty), so <c>@mail N</c>
/// positional access is "the Nth entry of the recipient's <see cref="Tables.MailBox"/> range, filtered to
/// the requested folder" rather than a stored ordinal. <see cref="Tables.MailCount"/> holds how many
/// box entries each (recipient, folder) has, kept in the same write as every change to a box entry or
/// its folder, so admission checks a folder's limit without reading the mailbox.
/// <para>
/// <see cref="Tables.MailFolder"/> (recipient + folder + mail id) and <see cref="Tables.MailSentTo"/>
/// (sender + recipient + mail id) are the same entries ordered for the two positional reads: the Nth mail
/// of a folder and the Nth mail one sender sent one recipient are the Nth key of a prefix, so a single
/// read decodes one row, and the folder list is one seek per folder. Both move in the same write as the
/// box entry and the row's folder.
/// </para>
/// </summary>
public partial class LightningDatabase
{
	private static byte[] MailKey(long mailId) => Keys.Dbref(mailId);

	private static byte[] MailBoxKey(long recipient, long mailId) => Keys.Concat(Keys.Dbref(recipient), Keys.Dbref(mailId));

	private static byte[] MailSentKey(long sender, long mailId) => Keys.Concat(Keys.Dbref(sender), Keys.Dbref(mailId));

	private static byte[] MailCountKey(long recipient, string folder) => Keys.Concat(Keys.Dbref(recipient), Keys.Str(folder));

	/// <summary>The <see cref="Tables.MailFolder"/> range of one folder of one mailbox.</summary>
	private static byte[] MailFolderPrefix(long recipient, string folder) => Keys.Concat(Keys.Dbref(recipient), Keys.Str(folder), Keys.Sep);

	private static byte[] MailFolderKey(long recipient, string folder, long mailId) => Keys.Concat(MailFolderPrefix(recipient, folder), Keys.Dbref(mailId));

	private static byte[] MailSentToKey(long sender, long recipient, long mailId)
		=> Keys.Concat(Keys.Dbref(sender), Keys.Dbref(recipient), Keys.Dbref(mailId));

	/// <summary>The mail id every index key here ends with.</summary>
	private static long TrailingMailId(byte[] key) => Keys.ReadDbref(key.AsSpan(key.Length - 8, 8));

	/// <summary>The ids of one folder of a mailbox, in mail-id order.</summary>
	private static IEnumerable<long> MailFolderIds(ITx tx, long recipient, string folder)
		=> tx.Range(Tables.MailFolder, MailFolderPrefix(recipient, folder)).Select(e => TrailingMailId(e.Key));

	/// <summary>The ids of the mail one sender sent one recipient, in mail-id order.</summary>
	private static IEnumerable<long> MailSentToIds(ITx tx, long sender, long recipient)
		=> tx.Range(Tables.MailSentTo, Keys.Concat(Keys.Dbref(sender), Keys.Dbref(recipient))).Select(e => TrailingMailId(e.Key));

	/// <summary>Each id's row, skipping an id whose row is missing, as <see cref="RangeMailBox"/> does.</summary>
	private static IEnumerable<(long MailId, MailRecord Record)> MailRows(ITx tx, IEnumerable<long> ids)
	{
		foreach (var mailId in ids)
		{
			if (tx.TryGet(Tables.Mail, MailKey(mailId), out var bytes))
			{
				yield return (mailId, Codec.Deserialize<MailRecord>(bytes));
			}
		}
	}

	/// <summary>
	/// The mail at 0-based <paramref name="position"/> of an id-ordered index range: the ids before it are
	/// stepped over without their rows being read, so only the selected mail is decoded.
	/// </summary>
	private SharpMail? MailAt(ITx tx, IEnumerable<long> ids, int position)
	{
		if (position < 0) return null;
		foreach (var mailId in ids.Skip(position))
		{
			return tx.TryGet(Tables.Mail, MailKey(mailId), out var bytes)
				? MapRecordToMail(tx, mailId, Codec.Deserialize<MailRecord>(bytes))
				: null;
		}

		return null;
	}

	private static long ReadMailCount(ITx tx, long recipient, string folder)
		=> tx.TryGet(Tables.MailCount, MailCountKey(recipient, folder), out var v) ? Keys.ReadDbref(v) : 0;

	/// <summary>Moves a folder's count by <paramref name="delta"/>; a folder that empties loses its row.</summary>
	private static void AdjustMailCount(ITx tx, long recipient, string folder, long delta)
	{
		if (delta == 0)
		{
			return;
		}

		var key = MailCountKey(recipient, folder);
		var count = ReadMailCount(tx, recipient, folder) + delta;
		if (count > 0)
		{
			tx.Put(Tables.MailCount, key, Keys.Dbref(count));
		}
		else
		{
			tx.Delete(Tables.MailCount, key);
		}
	}

	private static string MailId(long id) => $"Mail/{id}";

	private static long ParseMailId(string id) => long.Parse(id.Contains('/') ? id[(id.IndexOf('/') + 1)..] : id);

	/// <summary>Reads and increments the <c>next_mail</c> counter inside a write job, mirroring <see cref="AllocateDbref"/>.</summary>
	private static long AllocateMailId(ITx tx)
	{
		var current = tx.TryGet(Tables.Meta, Keys.Str("next_mail"), out var v) ? Keys.ReadDbref(v) : 0;
		tx.Put(Tables.Meta, Keys.Str("next_mail"), Keys.Dbref(current + 1));
		return current;
	}

	/// <summary>
	/// Every entry of a recipient's <see cref="Tables.MailBox"/> range, in mail-id order (which is
	/// insertion order — the id comes from a monotonic counter), each resolved to its
	/// <see cref="Tables.Mail"/> row. A box entry whose row is somehow missing is skipped rather than
	/// throwing, so a half-applied delete never turns a listing into an exception.
	/// </summary>
	private static IEnumerable<(long MailId, MailRecord Record)> RangeMailBox(ITx tx, long recipient)
	{
		foreach (var (key, _) in tx.Range(Tables.MailBox, Keys.Dbref(recipient)))
		{
			var mailId = Keys.ReadDbref(key.AsSpan(key.Length - 8, 8));
			if (tx.TryGet(Tables.Mail, MailKey(mailId), out var bytes))
			{
				yield return (mailId, Codec.Deserialize<MailRecord>(bytes));
			}
		}
	}

	/// <summary>As <see cref="RangeMailBox"/> but over <see cref="Tables.MailSent"/>, filtered to a recipient when given.</summary>
	private static IEnumerable<(long MailId, MailRecord Record)> RangeMailSent(ITx tx, long sender, long? recipient)
	{
		foreach (var (key, _) in tx.Range(Tables.MailSent, Keys.Dbref(sender)))
		{
			var mailId = Keys.ReadDbref(key.AsSpan(key.Length - 8, 8));
			if (!tx.TryGet(Tables.Mail, MailKey(mailId), out var bytes))
			{
				continue;
			}

			var record = Codec.Deserialize<MailRecord>(bytes);
			if (recipient is { } r && record.Recipient != r)
			{
				continue;
			}

			yield return (mailId, record);
		}
	}

	private SharpMail MapRecordToMail(ITx tx, long mailId, MailRecord record)
	{
		var from = ReadObject(tx, record.Sender) is { } found
			? Hydrate(found.Dbref, found.Record).WithNoneOption()
			: (AnyOptionalSharpObject)new None();

		return new SharpMail
		{
			Id = MailId(mailId),
			DateSent = DateTimeOffset.FromUnixTimeMilliseconds(record.DateSent),
			Fresh = record.Fresh,
			Read = record.Read,
			Tagged = record.Tagged,
			Urgent = record.Urgent,
			Forwarded = record.Forwarded,
			Cleared = record.Cleared,
			Folder = record.Folder,
			Content = MarkupTextSerializer.Deserialize(record.Content),
			Subject = MarkupTextSerializer.Deserialize(record.Subject),
			From = new AsyncLazy<AnyOptionalSharpObject>(_ => Task.FromResult(from))
		};
	}

	private List<SharpMail> GetIncomingMailsCore(ITx tx, long recipient, string? folder)
		=> (folder is null ? RangeMailBox(tx, recipient) : MailRows(tx, MailFolderIds(tx, recipient, folder)))
			.Select(m => MapRecordToMail(tx, m.MailId, m.Record))
			.ToList();

	private List<SharpMail> GetSentMailsCore(ITx tx, long sender, long? recipient)
		=> (recipient is { } r ? MailRows(tx, MailSentToIds(tx, sender, r)) : RangeMailSent(tx, sender, null))
			.Select(m => MapRecordToMail(tx, m.MailId, m.Record))
			.ToList();

	public IAsyncEnumerable<SharpMail> GetIncomingMailsAsync(SharpPlayer id, string folder, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpMail>(ct => GetIncomingMailsCoreAsync((long)id.Object.Key, folder, ct));

	private async IAsyncEnumerable<SharpMail> GetIncomingMailsCoreAsync(long recipient, string? folder,
		[EnumeratorCancellation] CancellationToken ct)
	{
		var mails = Store.Read(tx => GetIncomingMailsCore(tx, recipient, folder));
		foreach (var mail in mails)
		{
			ct.ThrowIfCancellationRequested();
			yield return mail;
		}
	}

	public IAsyncEnumerable<SharpMail> GetAllIncomingMailsAsync(SharpPlayer id, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpMail>(ct => GetIncomingMailsCoreAsync((long)id.Object.Key, null, ct));

	public ValueTask<SharpMail?> GetIncomingMailAsync(SharpPlayer id, string folder, int mail, CancellationToken cancellationToken = default)
	{
		var result = Store.Read(tx => MailAt(tx, MailFolderIds(tx, (long)id.Object.Key, folder), mail));
		return ValueTask.FromResult(result);
	}

	public IAsyncEnumerable<SharpMail> GetSentMailsAsync(SharpObject sender, SharpPlayer recipient, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpMail>(ct => GetSentMailsCoreAsync(sender.Key, (long)recipient.Object.Key, ct));

	private async IAsyncEnumerable<SharpMail> GetSentMailsCoreAsync(long sender, long? recipient,
		[EnumeratorCancellation] CancellationToken ct)
	{
		var mails = Store.Read(tx => GetSentMailsCore(tx, sender, recipient));
		foreach (var mail in mails)
		{
			ct.ThrowIfCancellationRequested();
			yield return mail;
		}
	}

	public IAsyncEnumerable<SharpMail> GetAllSentMailsAsync(SharpObject sender, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpMail>(ct => GetSentMailsCoreAsync(sender.Key, null, ct));

	public ValueTask<SharpMail?> GetSentMailAsync(SharpObject sender, SharpPlayer recipient, int mail, CancellationToken cancellationToken = default)
	{
		var result = Store.Read(tx => MailAt(tx, MailSentToIds(tx, sender.Key, (long)recipient.Object.Key), mail));
		return ValueTask.FromResult(result);
	}

	/// <summary>
	/// The mailbox's folders in the order their oldest mail arrived, read from <see cref="Tables.MailFolder"/>
	/// one seek per folder: after a folder's first key the cursor jumps past that folder's whole range.
	/// </summary>
	public ValueTask<string[]> GetMailFoldersAsync(SharpPlayer id, CancellationToken cancellationToken = default)
	{
		var recipient = (long)id.Object.Key;
		var box = Keys.Dbref(recipient);
		var result = Store.Read(tx =>
		{
			var folders = new List<(long FirstMailId, string Folder)>();
			var seek = box;
			while (tx.RangeFromKey(Tables.MailFolder, seek).FirstOrDefault() is { Key: { } key }
				&& Keys.StartsWith(key, box))
			{
				// recipient (8) + folder + 0x00 + mail id (8)
				var folder = Keys.ReadStr(key.AsSpan(8, key.Length - 8 - 1 - 8));
				folders.Add((TrailingMailId(key), folder));
				seek = Keys.Concat(Keys.Dbref(recipient), Keys.Str(folder), [0x01]);
			}

			return folders
				.Where(f => !string.IsNullOrEmpty(f.Folder))
				.OrderBy(f => f.FirstMailId)
				.Select(f => f.Folder)
				.ToArray();
		});
		return ValueTask.FromResult(result);
	}

	public async ValueTask<MailAdmission> SendMailAsync(SharpObject from, SharpPlayer to, SharpMail mail, long? limit = null,
		CancellationToken cancellationToken = default)
	{
		var senderKey = (long)from.Key;
		var recipientKey = (long)to.Object.Key;

		return await Store.WriteAsync<MailAdmission>(tx =>
		{
			var held = ReadMailCount(tx, recipientKey, mail.Folder);
			if (held >= limit)
			{
				return new MailboxFull();
			}

			var mailId = AllocateMailId(tx);
			var record = new MailRecord
			{
				Sender = senderKey,
				Recipient = recipientKey,
				DateSent = mail.DateSent.ToUnixTimeMilliseconds(),
				Fresh = mail.Fresh,
				Read = mail.Read,
				Tagged = mail.Tagged,
				Urgent = mail.Urgent,
				Forwarded = mail.Forwarded,
				Cleared = mail.Cleared,
				Folder = mail.Folder,
				Content = MarkupTextSerializer.Serialize(mail.Content),
				Subject = MarkupTextSerializer.Serialize(mail.Subject)
			};

			tx.Put(Tables.Mail, MailKey(mailId), Codec.Serialize(record));
			tx.Put(Tables.MailBox, MailBoxKey(recipientKey, mailId), []);
			tx.Put(Tables.MailSent, MailSentKey(senderKey, mailId), []);
			tx.Put(Tables.MailFolder, MailFolderKey(recipientKey, mail.Folder, mailId), []);
			tx.Put(Tables.MailSentTo, MailSentToKey(senderKey, recipientKey, mailId), []);
			AdjustMailCount(tx, recipientKey, mail.Folder, 1);
			return new AdmittedMail(MailId(mailId), (int)held + 1);
		}, cancellationToken);
	}

	public async ValueTask UpdateMailAsync(string mailId, MailUpdate commandMail, CancellationToken cancellationToken = default)
	{
		var id = ParseMailId(mailId);

		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Mail, MailKey(id), out var bytes))
			{
				return;
			}

			var record = Codec.Deserialize<MailRecord>(bytes);

			// UpdateMailAsync only ever carries one edit.
			// Reading clears Fresh the way the read confirmation does everywhere else in the engine —
			// clear/tag/urgent are status changes a player makes deliberately and don't touch it.
			var updated = commandMail switch
			{
				MailUpdate.Read read => record with { Read = read.Value, Fresh = false },
				MailUpdate.Cleared cleared => record with { Cleared = cleared.Value },
				MailUpdate.Tagged tagged => record with { Tagged = tagged.Value },
				MailUpdate.Urgent urgent => record with { Urgent = urgent.Value }
			};

			tx.Put(Tables.Mail, MailKey(id), Codec.Serialize(updated));
		}, cancellationToken);
	}

	public async ValueTask DeleteMailAsync(string mailId, CancellationToken cancellationToken = default)
	{
		var id = ParseMailId(mailId);

		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Mail, MailKey(id), out var bytes))
			{
				return;
			}

			var record = Codec.Deserialize<MailRecord>(bytes);
			if (tx.TryGet(Tables.MailBox, MailBoxKey(record.Recipient, id), out _))
			{
				AdjustMailCount(tx, record.Recipient, record.Folder, -1);
			}

			tx.Delete(Tables.MailBox, MailBoxKey(record.Recipient, id));
			tx.Delete(Tables.MailSent, MailSentKey(record.Sender, id));
			tx.Delete(Tables.MailFolder, MailFolderKey(record.Recipient, record.Folder, id));
			tx.Delete(Tables.MailSentTo, MailSentToKey(record.Sender, record.Recipient, id));
			tx.Delete(Tables.Mail, MailKey(id));
		}, cancellationToken);
	}

	public async ValueTask RenameMailFolderAsync(SharpPlayer player, string folder, string newFolder, CancellationToken cancellationToken = default)
	{
		var recipient = (long)player.Object.Key;

		await Store.WriteAsync(tx =>
		{
			var moved = MailRows(tx, MailFolderIds(tx, recipient, folder).ToList()).ToList();
			foreach (var (mailId, record) in moved)
			{
				tx.Put(Tables.Mail, MailKey(mailId), Codec.Serialize(record with { Folder = newFolder }));
				tx.Delete(Tables.MailFolder, MailFolderKey(recipient, folder, mailId));
				tx.Put(Tables.MailFolder, MailFolderKey(recipient, newFolder, mailId), []);
			}

			if (folder != newFolder)
			{
				AdjustMailCount(tx, recipient, folder, -moved.Count);
				AdjustMailCount(tx, recipient, newFolder, moved.Count);
			}
		}, cancellationToken);
	}

	public async ValueTask MoveMailFolderAsync(string mailId, string newFolder, CancellationToken cancellationToken = default)
	{
		var id = ParseMailId(mailId);

		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Mail, MailKey(id), out var bytes))
			{
				return;
			}

			var record = Codec.Deserialize<MailRecord>(bytes);
			tx.Put(Tables.Mail, MailKey(id), Codec.Serialize(record with { Folder = newFolder }));
			if (record.Folder != newFolder && tx.TryGet(Tables.MailBox, MailBoxKey(record.Recipient, id), out _))
			{
				AdjustMailCount(tx, record.Recipient, record.Folder, -1);
				AdjustMailCount(tx, record.Recipient, newFolder, 1);
				tx.Delete(Tables.MailFolder, MailFolderKey(record.Recipient, record.Folder, id));
				tx.Put(Tables.MailFolder, MailFolderKey(record.Recipient, newFolder, id), []);
			}
		}, cancellationToken);
	}

	public IAsyncEnumerable<SharpMail> GetAllSystemMailAsync(CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpMail>(GetAllSystemMailCoreAsync);

	private async IAsyncEnumerable<SharpMail> GetAllSystemMailCoreAsync([EnumeratorCancellation] CancellationToken ct)
	{
		await foreach (var (key, value) in Store.RangeAsync(Tables.Mail, [], ct: ct))
		{
			var mailId = Keys.ReadDbref(key);
			var record = Codec.Deserialize<MailRecord>(value);
			yield return Store.Read(tx => MapRecordToMail(tx, mailId, record));
		}
	}
}
