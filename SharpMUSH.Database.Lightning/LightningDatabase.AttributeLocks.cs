using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IAttributeLockStore"/>: an attribute's read and write keys sit on its metadata row, and an entry's on its
/// table row, so a rename, a move or a copy of the row carries them.
/// </summary>
public partial class LightningDatabase : IAttributeLockStore
{
	internal const string AttributeLockMigrationId = "0013_attribute_locks";

	public async ValueTask SetAttributeLockAsync(SharpAttribute attribute, AttributeLockKind kind, string? key,
		CancellationToken cancellationToken = default)
	{
		var (dbref, longName) = ParseAttributeKey(attribute.Key);
		var stored = string.IsNullOrWhiteSpace(key) ? null : key;
		await Store.WriteAsync(tx =>
		{
			var rowKey = Keys.Attr(dbref, longName);
			if (!tx.TryGet(Tables.AttrMeta, rowKey, out var bytes)) return;

			var meta = ReadAttributeMetadata(bytes);
			var updated = kind is AttributeLockKind.Read ? meta with { ReadLock = stored } : meta with { WriteLock = stored };
			tx.Put(Tables.AttrMeta, rowKey, Codec.Serialize(updated));
		}, cancellationToken);
	}

	public async ValueTask<Found<SharpAttributeEntry>> SetAttributeEntryLockAsync(string name, AttributeLockKind kind,
		string? key, CancellationToken cancellationToken = default)
	{
		var stored = string.IsNullOrWhiteSpace(key) ? null : key;
		return await Store.WriteAsync<Found<SharpAttributeEntry>>(tx =>
		{
			if (ReadAttributeEntryRecord(tx, name) is not { } record)
			{
				return new NotFound();
			}

			var updated = kind is AttributeLockKind.Read ? record with { ReadLock = stored } : record with { WriteLock = stored };
			tx.Put(Tables.AttrEntry, Keys.Upper(name), Codec.Serialize(updated));
			return MapEntry(updated);
		}, cancellationToken);
	}

	/// <summary>
	/// Changes no row: attribute and entry rows gain two optional keys, which an older server would read past and so grant
	/// nobody what they grant. The new world format makes such a server refuse the world instead.
	/// </summary>
	private static void MigrateAttributeLocks(ITx tx)
	{
		var marker = Keys.Str("mig:" + AttributeLockMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;

		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = AttributeLockMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
	}
}
