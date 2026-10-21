using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Migration;

internal readonly record struct CategorySlice(int Offset, int Length, int Null, int Ordinal);

/// <summary>Owns the decoded managed projection until disposal.</summary>
/// <remarks>
/// Category spans borrow this projection's storage and must not outlive its disposal.
/// The byte counts describe managed storage estimates, not hard RSS limits. Native decoding owns
/// a reservation on the caller's budget; that budget alone tracks outstanding native-path storage.
/// </remarks>
internal sealed class WikiCategoryProjection(string? title, string? author, bool? published, bool categoriesNull,
	CategorySlice[] slices, char[] chars, char[] folded, int[] indices, byte[] first, int distinct, bool hasNull,
	long peak, long retained, NativeReadReservation? retainedLease = null) : IDisposable
{
	private NativeReadReservation? _retainedLease = retainedLease;
	private string? _title = title;
	private string? _author = author;
	private CategorySlice[]? _slices = slices;
	private char[]? _chars = chars;
	private char[]? _folded = folded;
	private int[]? _indices = indices;
	private byte[]? _first = first;
	private bool _disposed;
	private void Check() => ObjectDisposedException.ThrowIf(_disposed, this);
	public string? Title { get { Check(); return _title; } }
	public string? AuthorDbref { get { Check(); return _author; } }
	public bool? Published { get { Check(); return published; } }
	public bool CategoriesAreNull { get { Check(); return categoriesNull; } }
	public int CategoryCount { get { Check(); return _slices!.Length; } }
	public bool IsNullCategory(int ordinal) { Check(); return _slices![ordinal].Null != 0; }
	/// <summary>Returns category text borrowed from this projection's owned storage.</summary>
	/// <remarks>The returned span is valid only while this projection remains undisposed.</remarks>
	public ReadOnlySpan<char> CategoryAt(int ordinal)
	{
		Check();
		var slice = _slices![ordinal];
		if (slice.Null != 0) throw new InvalidOperationException("Null category cannot be indexed.");
		return _chars.AsSpan(slice.Offset, slice.Length);
	}
	public int DistinctCategoryCount { get { Check(); if (hasNull) throw new InvalidOperationException("Null category cannot be indexed."); return distinct; } }
	public int DistinctCategoryOrdinalAt(int ordinal)
	{
		Check();
		if (hasNull) throw new InvalidOperationException("Null category cannot be indexed.");
		if ((uint)ordinal >= (uint)distinct) throw new ArgumentOutOfRangeException(nameof(ordinal));
		return _indices![ordinal];
	}
	/// <summary>Peak of the numeric-only storage model; zero for native-budget decoding.</summary>
	public long PeakReservedBytes { get { Check(); return peak; } }
	/// <summary>Calculated retained payload estimate, not an independent admission counter.</summary>
	public long RetainedBytes { get { Check(); return retained; } }
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_title = null;
		_author = null;
		_slices = null;
		_chars = null;
		_folded = null;
		_indices = null;
		_first = null;
		var lease = _retainedLease;
		_retainedLease = null;
		lease?.Dispose();
	}
}
