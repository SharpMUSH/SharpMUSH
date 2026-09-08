namespace SharpMUSH.Library.Plugins.Storage.Lightning;

/// <summary>
/// One LMDB transaction, read-only or read-write, valid only inside the delegate that received it.
/// Every result is a copy; nothing returned points into the memory map.
/// </summary>
public interface ITx
{
	bool TryGet(TableDef table, ReadOnlySpan<byte> key, out byte[] value);
	void Put(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);
	bool Delete(TableDef table, ReadOnlySpan<byte> key);
	bool Delete(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);
	long Count(TableDef table);
	/// <summary>All entries whose key starts with <paramref name="prefix"/>, in key order (duplicates in value order).</summary>
	IEnumerable<(byte[] Key, byte[] Value)> Range(TableDef table, byte[] prefix);
	/// <summary>As <see cref="Range"/>, resuming strictly after (<paramref name="afterKey"/>, <paramref name="afterValue"/>).</summary>
	IEnumerable<(byte[] Key, byte[] Value)> RangeFrom(TableDef table, byte[] prefix, byte[] afterKey, byte[]? afterValue);
	/// <summary>All entries whose key is greater than or equal to <paramref name="startKey"/>, in key order, with no
	/// prefix restriction — the rest of the table from that point on.</summary>
	IEnumerable<(byte[] Key, byte[] Value)> RangeFromKey(TableDef table, byte[] startKey);
	/// <summary>All duplicate values stored under <paramref name="key"/>, in order.</summary>
	IEnumerable<byte[]> Dups(TableDef table, byte[] key);
	/// <summary>Deletes every entry under the prefix; returns how many.</summary>
	int DeletePrefix(TableDef table, byte[] prefix);
}
