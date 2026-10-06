using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// Builds the read indexes a world written before them lacks: the session expiry order, the object type
/// index, the wiki list indexes and the mail folder and sent-to indexes. Each is derived entirely from rows
/// already on disk, built in one write job together with its marker, and skipped once the marker exists;
/// from then on the writes that touch those rows keep it current.
/// </summary>
public partial class LightningDatabase
{
	internal const string SessionExpiryIndexMigrationId = "0006_session_expiry_index";
	internal const string ObjectTypeIndexMigrationId = "0007_object_type_index";
	internal const string WikiListIndexMigrationId = "0008_wiki_list_indexes";
	internal const string MailReadIndexMigrationId = "0009_mail_read_indexes";

	private async ValueTask RebuildReadIndexesAsync(CancellationToken cancellationToken)
	{
		await Store.WriteAsync(tx => RebuildIndex(tx, SessionExpiryIndexMigrationId, [Tables.SessionExpiry], () =>
		{
			foreach (var (token, value) in tx.Range(Tables.Session, []))
			{
				cancellationToken.ThrowIfCancellationRequested();
				tx.Put(Tables.SessionExpiry, SessionExpiryKey(Codec.Deserialize<SessionRecord>(value).ExpiryUnixMs, token), []);
			}
		}), cancellationToken);

		await Store.WriteAsync(tx => RebuildIndex(tx, ObjectTypeIndexMigrationId, [Tables.ObjType], () =>
		{
			foreach (var (key, value) in tx.Range(Tables.Obj, []))
			{
				cancellationToken.ThrowIfCancellationRequested();
				IndexObjectType(tx, Codec.Deserialize<ObjectRecord>(value).Type, Keys.ReadDbref(key));
			}
		}), cancellationToken);

		await Store.WriteAsync(tx => RebuildIndex(tx, WikiListIndexMigrationId,
			[Tables.WikiRecent, Tables.WikiByNamespace, Tables.WikiByCategory], () =>
			{
				foreach (var (key, record) in AllWikiPages(tx))
				{
					cancellationToken.ThrowIfCancellationRequested();
					WikiListIndexes(tx, key, record, add: true);
				}
			}), cancellationToken);

		await Store.WriteAsync(tx => RebuildIndex(tx, MailReadIndexMigrationId, [Tables.MailFolder, Tables.MailSentTo], () =>
		{
			// A folder entry for every box entry whose row exists, the entries the box listing yields; a sent-to
			// entry for every sent entry whose row exists, the entries the sent listing yields.
			foreach (var (key, _) in tx.Range(Tables.MailBox, []))
			{
				cancellationToken.ThrowIfCancellationRequested();
				var recipient = Keys.ReadDbref(key);
				var mailId = TrailingMailId(key);
				if (!tx.TryGet(Tables.Mail, MailKey(mailId), out var bytes)) continue;
				tx.Put(Tables.MailFolder, MailFolderKey(recipient, Codec.Deserialize<MailRecord>(bytes).Folder, mailId), []);
			}

			foreach (var (key, _) in tx.Range(Tables.MailSent, []))
			{
				cancellationToken.ThrowIfCancellationRequested();
				var sender = Keys.ReadDbref(key);
				var mailId = TrailingMailId(key);
				if (!tx.TryGet(Tables.Mail, MailKey(mailId), out var bytes)) continue;
				tx.Put(Tables.MailSentTo, MailSentToKey(sender, Codec.Deserialize<MailRecord>(bytes).Recipient, mailId), []);
			}
		}), cancellationToken);
	}

	/// <summary>Clears <paramref name="tables"/>, runs <paramref name="build"/> and records
	/// <paramref name="migrationId"/>, all in the caller's write job; does nothing once the id is recorded.</summary>
	private static void RebuildIndex(ITx tx, string migrationId, TableDef[] tables, Action build)
	{
		var marker = Keys.Str("mig:" + migrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;
		foreach (var table in tables)
		{
			tx.DeletePrefix(table, []);
		}

		build();
		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = migrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
	}
}
