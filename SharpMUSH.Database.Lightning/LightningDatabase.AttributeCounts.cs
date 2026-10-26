using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// How many attribute nodes each object holds, kept beside the attributes in <see cref="Tables.AttrCount"/> so
/// <c>max_attrs_per_obj</c> is checked with one read rather than a scan of the object's attributes. Every write that
/// adds or removes an <see cref="Tables.AttrMeta"/> row adjusts it in the same transaction.
/// </summary>
public partial class LightningDatabase
{
	internal const string AttributeCountMigrationId = "0007_attribute_counts";

	public ValueTask<long> GetAttributeCountAsync(DBRef dbref, CancellationToken cancellationToken = default)
		=> new(Store.Read(tx => ReadAttributeCount(tx, dbref.Number)));

	private static long ReadAttributeCount(ITx tx, long n)
		=> tx.TryGet(Tables.AttrCount, Keys.Dbref(n), out var value) ? Keys.ReadDbref(value) : 0;

	private static void AdjustAttributeCount(ITx tx, long n, long delta)
	{
		if (delta == 0) return;
		var key = Keys.Dbref(n);
		var count = ReadAttributeCount(tx, n) + delta;
		if (count > 0) tx.Put(Tables.AttrCount, key, Keys.Dbref(count));
		else tx.Delete(Tables.AttrCount, key);
	}

	/// <summary>Counts every object's attribute rows once, for a world written before the counts were kept.</summary>
	private static void MigrateAttributeCounts(ITx tx)
	{
		var marker = Keys.Str("mig:" + AttributeCountMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;
		tx.DeletePrefix(Tables.AttrCount, []);
		var counts = new Dictionary<long, long>();
		foreach (var (key, _) in tx.Range(Tables.AttrMeta, []))
		{
			var dbref = Keys.ReadDbref(key);
			counts[dbref] = counts.GetValueOrDefault(dbref) + 1;
		}

		foreach (var (dbref, count) in counts)
			tx.Put(Tables.AttrCount, Keys.Dbref(dbref), Keys.Dbref(count));
		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = AttributeCountMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
	}
}
