using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// World format 3: a mail row carries <see cref="MailRecord.Message"/>, <see cref="MailRecord.Thread"/>,
/// <see cref="MailRecord.ReplyTo"/> and <see cref="MailRecord.Addressed"/>, and <see cref="Tables.MailThread"/> indexes
/// every row by its thread. Mail stored before then gets a message id, which is also its thread: the copies one send
/// delivered share one, found as the portal's sent list found them (one sender, the same subject, text and urgency,
/// stamped within <see cref="SameSendWindowMs"/> of the send's first copy, each to a mailbox the send had not reached).
/// Those rows keep no <see cref="MailRecord.Addressed"/>, since nothing recorded who the sender named.
/// </summary>
public partial class LightningDatabase
{
	internal const string MailThreadMigrationId = "0014_mail_threads";

	/// <summary>How far apart the copies of one send were stamped: each was stamped as it was delivered.</summary>
	private const long SameSendWindowMs = 5000;

	private void MigrateMailThreads(ITx tx)
	{
		var marker = Keys.Str("mig:" + MailThreadMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;

		var sends = new Dictionary<long, PendingSend>();
		foreach (var (key, value) in tx.Range(Tables.Mail, []).ToList())
		{
			var mailId = Keys.ReadDbref(key);
			var record = ReadMail(tx, value);
			if (record.Message is not null)
			{
				continue;
			}

			if (!sends.TryGetValue(record.Sender, out var send) || !send.Takes(record))
			{
				send = new PendingSend(record, MessageIdFor(record.DateSent));
				sends[record.Sender] = send;
			}

			send.Recipients.Add(record.Recipient);
			PutMail(tx, key, record with { Message = send.Id, Thread = send.Id });
			tx.Put(Tables.MailThread, MailThreadKey(send.Id, mailId), []);
		}

		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = MailThreadMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
	}

	/// <summary>
	/// A new message id stamped with when it was sent. A version 7 id holds no time before 1970, and a stored date may
	/// be anything an import wrote, so a date outside what it can hold is clamped to the nearest one it can.
	/// </summary>
	internal static Guid MessageIdFor(long dateSentMs)
		=> Guid.CreateVersion7(DateTimeOffset.FromUnixTimeMilliseconds(
			Math.Clamp(dateSentMs, 0, DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())));

	/// <summary>The send one sender's latest copy belongs to, while later copies may still join it.</summary>
	private sealed class PendingSend(MailRecord first, Guid id)
	{
		public Guid Id { get; } = id;
		public HashSet<long> Recipients { get; } = [];

		public bool Takes(MailRecord next)
			=> next.DateSent >= first.DateSent
				&& next.DateSent - first.DateSent <= SameSendWindowMs
				&& next.SenderCreationTime == first.SenderCreationTime
				&& next.Urgent == first.Urgent
				&& next.Anonymous == first.Anonymous
				&& next.Subject == first.Subject
				&& next.Content == first.Content
				&& !Recipients.Contains(next.Recipient);
	}
}
