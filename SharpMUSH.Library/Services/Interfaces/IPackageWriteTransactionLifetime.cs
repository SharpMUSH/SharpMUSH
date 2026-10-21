namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Provider-owned package operation lifetime. The returned owner is retained through actual source
/// and compensation completion; successful acquisition is invalidation, never a settlement grant.
/// </summary>
public interface IPackageWriteTransactionLifetime
{
	ValueTask<IAsyncDisposable> BeginPackageWritesAsync(CancellationToken cancellationToken);
}
