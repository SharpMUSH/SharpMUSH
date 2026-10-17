using System.Collections.Immutable;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Plugins.Storage.Lightning;

/// <summary>Optional synchronous copied reads. Native memory never escapes the transaction.</summary>
public interface IBoundedReadTx
{
	/// <summary>Returns an owned copy or NotFound. Only plain, nonduplicate tables are supported.</summary>
	Found<NativeReadValue> ReadValue(TableDef table, ReadOnlySpan<byte> key, NativeReadBudget budget);

	/// <summary>
	/// Copies at most 128 entries/128KiB of key+value bytes satisfying prefix, inclusive start, exclusive end
	/// and strict afterKey. Empty end/resume means unbounded. Resume using the last returned key. A populated
	/// local byte cap returns nonexhausted; an oversized single row or exhausted shared budget throws.
	/// </summary>
	NativeReadBatch ReadRange(TableDef table, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> inclusiveStart,
		ReadOnlySpan<byte> exclusiveEnd, ReadOnlySpan<byte> afterKey, NativeReadBudget budget);
}

public sealed record NativeReadEntry(byte[] Key, byte[] Value);

/// <summary>The copied array is borrowed until disposal, independently of the native transaction.</summary>
/// <remarks>The supplied exclusive reservation must already cover this owner and its array.</remarks>
public sealed class NativeReadValue(byte[] value, NativeReadReservation reservation) : IDisposable
{
	private byte[]? _value = value;
	public byte[] Value => _value ?? throw new ObjectDisposedException(nameof(NativeReadValue));
	public void Dispose() { _value = null; reservation.Dispose(); }
}

/// <summary>Copied entries are borrowed until disposal. Nonexhausted is not a public has-more claim.</summary>
/// <remarks>The supplied exclusive reservation must already cover this owner and all entries/arrays.</remarks>
public sealed class NativeReadBatch(ImmutableArray<NativeReadEntry> entries, bool exhausted, NativeReadReservation reservation) : IDisposable
{
	private ImmutableArray<NativeReadEntry> _entries = entries;
	public ImmutableArray<NativeReadEntry> Entries => !_entries.IsDefault ? _entries : throw new ObjectDisposedException(nameof(NativeReadBatch));
	public bool Exhausted => exhausted;
	public void Dispose() { _entries = default; reservation.Dispose(); }
}
