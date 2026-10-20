using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Migration;

/// <summary>Private copied reads for the bounded category-value migration only.</summary>
internal interface IWikiCategoryMigrationReadTx
{
	Found<NativeReadValue> ReadWikiCategoryPage(ReadOnlySpan<byte> pageKey, int maxSourceBytes, NativeReadBudget budget);
	WikiCategoryKeyBatch ReadWikiCategoryKeys(TableDef table, ReadOnlySpan<byte> afterKey,
		int maxKeys, int maxKeyBytes, NativeReadBudget budget);
}

/// <summary>Copied keys borrowed until disposal; no native cursor or value escapes.</summary>
internal sealed class WikiCategoryKeyBatch : IDisposable
{
	private byte[][]? _keys;
	private readonly int _count;
	private readonly NativeReadReservation _reservation;

	internal WikiCategoryKeyBatch(byte[][] keys, int count, bool exhausted, NativeReadReservation reservation)
	{
		_keys = keys;
		_count = count;
		Exhausted = exhausted;
		_reservation = reservation;
	}

	public int Count
	{
		get
		{
			ObjectDisposedException.ThrowIf(_keys is null, this);
			return _count;
		}
	}

	public bool Exhausted { get; }

	public ReadOnlySpan<byte> KeyAt(int index)
	{
		ObjectDisposedException.ThrowIf(_keys is null, this);
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
		return _keys[index];
	}

	public void Dispose()
	{
		var keys = _keys;
		_keys = null;
		if (keys is not null) Array.Clear(keys);
		_reservation.Dispose();
	}
}
