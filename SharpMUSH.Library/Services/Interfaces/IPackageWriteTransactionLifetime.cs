namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Provider-owned package operation lifetime. The returned hold is retained through actual source
/// and compensation completion; successful acquisition is invalidation, never a settlement grant.
/// </summary>
public interface IPackageWriteTransactionLifetime
{
	ValueTask<IPackageWriteHold> BeginPackageWritesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One transaction's hold on its provider. The transaction reports how it ended, once, before it
/// disposes the hold. A provider may use the verdict to refuse settling an operation; no verdict,
/// <see cref="PackageWriteVerdict.Committed"/> included, settles anything by itself.
/// </summary>
public interface IPackageWriteHold : IAsyncDisposable
{
	void Report(PackageWriteVerdict verdict);
}

/// <summary>How a package transaction ended, as it reports to its <see cref="IPackageWriteHold"/>.</summary>
public enum PackageWriteVerdict
{
	/// <summary>Committed, and any work it owned after the commit succeeded.</summary>
	Committed,
	/// <summary>Closed uncommitted; every write it made was undone.</summary>
	Reverted,
	/// <summary>Closed uncommitted, and some write could not be undone, or recovery failed.</summary>
	CompensationFailed,
	/// <summary>Committed, but work it owned after the commit failed.</summary>
	ReleasedWorkFailed,
	/// <summary>Work that holds no transaction ended without completing, by an exception or cancellation.</summary>
	Abandoned
}
