using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Semantics;

internal sealed class GuardedSemanticFixtureTx(ITx inner) : ITx, IBoundedReadTx
{
	internal bool Touched { get; private set; }
	private bool _poisoned;
	internal static bool Protected(string name) => !NativeTableName.IsValid(name) || name.StartsWith("prototype.semantic.", StringComparison.Ordinal)
		|| Tables.All.Any(table => table.Name == name);
	private void Mutating(TableDef table)
	{
		if (Protected(table.Name))
		{
			_poisoned = true;
			throw new WikiFixtureMutationException(WikiFixtureMutationFailure.UnsupportedOperation);
		}
		Touched = true;
	}
	internal void RequireNotPoisoned()
	{
		if (_poisoned) throw new WikiFixtureMutationException(WikiFixtureMutationFailure.UnsupportedOperation);
	}
	public void Put(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) { Mutating(table); inner.Put(table, key, value); }
	public bool Delete(TableDef table, ReadOnlySpan<byte> key) { Mutating(table); return inner.Delete(table, key); }
	public bool Delete(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) { Mutating(table); return inner.Delete(table, key, value); }
	public int DeletePrefix(TableDef table, byte[] prefix) { Mutating(table); return inner.DeletePrefix(table, prefix); }
	public bool TryGet(TableDef table, ReadOnlySpan<byte> key, out byte[] value) => inner.TryGet(table, key, out value);
	public long Count(TableDef table) => inner.Count(table);
	public IEnumerable<(byte[] Key, byte[] Value)> Range(TableDef table, byte[] prefix) => inner.Range(table, prefix);
	public IEnumerable<(byte[] Key, byte[] Value)> RangeFrom(TableDef table, byte[] prefix, byte[] afterKey, byte[]? afterValue) => inner.RangeFrom(table, prefix, afterKey, afterValue);
	public IEnumerable<(byte[] Key, byte[] Value)> RangeFromKey(TableDef table, byte[] startKey) => inner.RangeFromKey(table, startKey);
	public IEnumerable<(byte[] Key, byte[] Value)> RangeReverse(TableDef table, byte[] prefix) => inner.RangeReverse(table, prefix);
	public IEnumerable<(byte[] Key, byte[] Value)> RangeReverseBefore(TableDef table, byte[] prefix, byte[] beforeKey) => inner.RangeReverseBefore(table, prefix, beforeKey);
	public long CountRange(TableDef table, byte[] prefix, byte[] startKey) => inner.CountRange(table, prefix, startKey);
	public IEnumerable<byte[]> Dups(TableDef table, byte[] key) => inner.Dups(table, key);
	public Result<long> CountDups(TableDef table, ReadOnlySpan<byte> key) => inner.CountDups(table, key);
	public Found<NativeReadValue> ReadValue(TableDef table, ReadOnlySpan<byte> key, NativeReadBudget budget) => ((IBoundedReadTx)inner).ReadValue(table, key, budget);
	public NativeReadBatch ReadRange(TableDef table, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> inclusiveStart, ReadOnlySpan<byte> exclusiveEnd, ReadOnlySpan<byte> afterKey, NativeReadBudget budget)
		=> ((IBoundedReadTx)inner).ReadRange(table, prefix, inclusiveStart, exclusiveEnd, afterKey, budget);
}
