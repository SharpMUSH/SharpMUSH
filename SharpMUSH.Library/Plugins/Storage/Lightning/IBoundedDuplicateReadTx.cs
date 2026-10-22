namespace SharpMUSH.Library.Plugins.Storage.Lightning;

/// <summary>Optional copied, bounded reads of one exact key in a variable-width duplicate-sorted table.</summary>
public interface IBoundedDuplicateReadTx
{
	/// <summary>
	/// Returns at most 128 entries/128KiB of copied key+value payload in unsigned duplicate-value order.
	/// Null continuation starts at the first value; nonnull (including empty) means strictly after that value.
	/// Continue only within the same live readonly callback and shared budget. Only Exhausted proves completion;
	/// local caps return nonexhausted, shared limits and native errors throw without returning partial success.
	/// Actual native flags must be exactly variable DUPSORT. The copied batch owns its working reservation
	/// independently of callback lifetime; this capability itself expires when the callback exits.
	/// </summary>
	NativeReadBatch ReadDuplicates(TableDef table, ReadOnlySpan<byte> key, byte[]? afterValue, NativeReadBudget budget);
}
