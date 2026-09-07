using System.Runtime.CompilerServices;
using DotNext.Threading;
using OneOf.Types;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IMailStore"/>: @mail folders, incoming and sent mail. Ported from
/// <c>SurrealDatabase.Mail.cs</c>. <see cref="Tables.Mail"/> is keyed by a dedicated <c>next_mail</c>
/// counter (mirroring <see cref="AllocateDbref"/> but its own key, since mail ids and dbrefs are
/// unrelated sequences); <see cref="Tables.MailBox"/> and <see cref="Tables.MailSent"/> are ordered
/// per-recipient / per-sender indexes (recipient-or-sender dbref + mail id -> empty), so <c>@mail N</c>
/// positional access is "the Nth entry of the recipient's <see cref="Tables.MailBox"/> range, filtered to
/// the requested folder" rather than a stored ordinal.
/// </summary>
public sealed partial class LightningDatabase
{
	private static byte[] MailKey(long mailId) => Keys.Dbref(mailId);

	private static byte[] MailBoxKey(long recipient, long mailId) => Keys.Concat(Keys.Dbref(recipient), Keys.Dbref(mailId));

	private static byte[] MailSentKey(long sender, long mailId) => Keys.Concat(Keys.Dbref(sender), Keys.Dbref(mailId));

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
			? Hydrate(tx, found.Dbref, found.Record).WithNoneOption()
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
			Content = MModule.deserialize(record.Content),
			Subject = MModule.deserialize(record.Subject),
			From = new AsyncLazy<AnyOptionalSharpObject>(_ => Task.FromResult(from))
		};
	}

	private List<SharpMail> GetIncomingMailsCore(ITx tx, long recipient, string? folder)
		=> RangeMailBox(tx, recipient)
			.Where(m => folder is null || m.Record.Folder == folder)
			.Select(m => MapRecordToMail(tx, m.MailId, m.Record))
			.ToList();

	private List<SharpMail> GetSentMailsCore(ITx tx, long sender, long? recipient)
		=> RangeMailSent(tx, sender, recipient)
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
		var result = Store.Read(tx =>
		{
			var mails = GetIncomingMailsCore(tx, (long)id.Object.Key, folder);
			return mail >= 0 && mail < mails.Count ? mails[mail] : null;
		});
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
		var result = Store.Read(tx =>
		{
			var mails = GetSentMailsCore(tx, sender.Key, (long)recipient.Object.Key);
			return mail >= 0 && mail < mails.Count ? mails[mail] : null;
		});
		return ValueTask.FromResult(result);
	}

	public ValueTask<string[]> GetMailFoldersAsync(SharpPlayer id, CancellationToken cancellationToken = default)
	{
		var result = Store.Read(tx => RangeMailBox(tx, (long)id.Object.Key)
			.Select(m => m.Record.Folder)
			.Where(f => !string.IsNullOrEmpty(f))
			.Distinct()
			.ToArray());
		return ValueTask.FromResult(result);
	}

	public async ValueTask SendMailAsync(SharpObject from, SharpPlayer to, SharpMail mail, CancellationToken cancellationToken = default)
	{
		var senderKey = (long)from.Key;
		var recipientKey = (long)to.Object.Key;

		await Store.WriteAsync(tx =>
		{
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
				Content = MModule.serialize(mail.Content),
				Subject = MModule.serialize(mail.Subject)
			};

			tx.Put(Tables.Mail, MailKey(mailId), Codec.Serialize(record));
			tx.Put(Tables.MailBox, MailBoxKey(recipientKey, mailId), []);
			tx.Put(Tables.MailSent, MailSentKey(senderKey, mailId), []);
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

			// UpdateMailAsync only ever carries one edit; matches SurrealDatabase.Mail.cs's switch.
			// Reading clears Fresh the way the read confirmation does everywhere else in the engine —
			// clear/tag/urgent are status changes a player makes deliberately and don't touch it.
			var updated = commandMail switch
			{
				{ IsReadEdit: true } => record with { Read = commandMail.AsReadEdit, Fresh = false },
				{ IsClearEdit: true } => record with { Cleared = commandMail.AsClearEdit },
				{ IsTaggedEdit: true } => record with { Tagged = commandMail.AsTaggedEdit },
				{ IsUrgentEdit: true } => record with { Urgent = commandMail.AsUrgentEdit },
				_ => record
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
			tx.Delete(Tables.MailBox, MailBoxKey(record.Recipient, id));
			tx.Delete(Tables.MailSent, MailSentKey(record.Sender, id));
			tx.Delete(Tables.Mail, MailKey(id));
		}, cancellationToken);
	}

	public async ValueTask RenameMailFolderAsync(SharpPlayer player, string folder, string newFolder, CancellationToken cancellationToken = default)
	{
		var recipient = (long)player.Object.Key;

		await Store.WriteAsync(tx =>
		{
			foreach (var (mailId, record) in RangeMailBox(tx, recipient).Where(m => m.Record.Folder == folder).ToList())
			{
				tx.Put(Tables.Mail, MailKey(mailId), Codec.Serialize(record with { Folder = newFolder }));
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
		}, cancellationToken);
	}

	public IAsyncEnumerable<SharpMail> GetAllSystemMailAsync(CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpMail>(GetAllSystemMailCoreAsync);

	private async IAsyncEnumerable<SharpMail> GetAllSystemMailCoreAsync([EnumeratorCancellation] CancellationToken ct)
	{
		byte[]? lastKey = null;
		const int pageSize = 256;

		while (true)
		{
			ct.ThrowIfCancellationRequested();
			var page = Store.Read(tx =>
				(lastKey is null ? tx.Range(Tables.Mail, []) : tx.RangeFrom(Tables.Mail, [], lastKey, null))
					.Take(pageSize)
					.Select(entry => (entry.Key,
						Mail: MapRecordToMail(tx, Keys.ReadDbref(entry.Key), Codec.Deserialize<MailRecord>(entry.Value))))
					.ToList());

			foreach (var (_, mail) in page)
			{
				yield return mail;
			}

			if (page.Count < pageSize)
			{
				yield break;
			}

			lastKey = page[^1].Key;
		}
	}
}
