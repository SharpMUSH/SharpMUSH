namespace SharpMUSH.Library.Plugins.Storage.Lightning;

/// <summary>An internal work dimension was exhausted; no partial read result is available.</summary>
public sealed class NativeReadLimitException(string dimension) : Exception($"Native read limit: {dimension}.")
{
	public string Dimension { get; } = dimension;
}

/// <summary>Caller-owned cumulative native work and outstanding managed allocation accounting. Not thread-safe.</summary>
/// <remarks>
/// Probe and inspected byte charges are cumulative, including failed reads. Working charges last until their
/// reservation is disposed. A reservation conservatively costs 64 bytes itself, in addition to the requested
/// allocation estimate. Callers also own scopes for input keys, decoded models and containers. Cancellation
/// is checked before each accounting/native operation, but cannot preempt a synchronous OS page fault.
/// </remarks>
public sealed class NativeReadBudget
{
	public const int MaxRecordBytes = 128 * 1024;
	public const int MaxBatchRecords = 128;
	private const long ReservationBytes = 64;
	private readonly int _maxProbes;
	private readonly long _maxBytes;
	private readonly long _maxWorkingBytes;

	public NativeReadBudget(int maxProbes, long maxBytes, long maxWorkingBytes, CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxProbes);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWorkingBytes);
		(_maxProbes, _maxBytes, _maxWorkingBytes, CancellationToken) = (maxProbes, maxBytes, maxWorkingBytes, cancellationToken);
	}

	public int Probes { get; private set; }
	public long Bytes { get; private set; }
	public long WorkingBytes { get; private set; }
	public CancellationToken CancellationToken { get; }

	public void ChargeProbe()
	{
		CancellationToken.ThrowIfCancellationRequested();
		if (Probes == _maxProbes) throw new NativeReadLimitException("probes");
		Probes++;
	}

	public void ChargeBytes(long bytes)
	{
		CancellationToken.ThrowIfCancellationRequested();
		ArgumentOutOfRangeException.ThrowIfNegative(bytes);
		if (bytes > _maxBytes - Bytes) throw new NativeReadLimitException("bytes");
		Bytes += bytes;
	}

	public NativeReadReservation ReserveWorking(long bytes)
	{
		CancellationToken.ThrowIfCancellationRequested();
		ArgumentOutOfRangeException.ThrowIfNegative(bytes);
		if (bytes > long.MaxValue - ReservationBytes) throw new NativeReadLimitException("working-bytes");
		var total = bytes + ReservationBytes;
		AddWorking(total);
		try { return new NativeReadReservation(this, total); }
		catch { ReleaseWorking(total); throw; }
	}

	internal void AddWorking(long bytes)
	{
		CancellationToken.ThrowIfCancellationRequested();
		ArgumentOutOfRangeException.ThrowIfNegative(bytes);
		if (bytes > _maxWorkingBytes - WorkingBytes) throw new NativeReadLimitException("working-bytes");
		WorkingBytes += bytes;
	}

	internal void ReleaseWorking(long bytes) => WorkingBytes -= bytes;
}

/// <summary>An exclusive working-allocation lease. Dispose once all borrowed allocations have been dropped.</summary>
public sealed class NativeReadReservation : IDisposable
{
	private NativeReadBudget? _budget;
	private long _bytes;

	internal NativeReadReservation(NativeReadBudget budget, long bytes) => (_budget, _bytes) = (budget, bytes);

	/// <summary>Reserve additional allocation before creating it. A failed reservation changes no counters.</summary>
	public void ReserveAdditional(long bytes)
	{
		ObjectDisposedException.ThrowIf(_budget is null, this);
		_budget.AddWorking(bytes);
		// The shared total is bounded by a positive long maximum; this lease cannot overflow independently.
		_bytes += bytes;
	}

	public void Dispose()
	{
		var budget = _budget;
		_budget = null;
		budget?.ReleaseWorking(_bytes);
	}
}
