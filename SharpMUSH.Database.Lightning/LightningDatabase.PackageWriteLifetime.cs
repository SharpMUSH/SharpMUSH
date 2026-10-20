using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase : IPackageWriteTransactionLifetime
{
	async ValueTask<IAsyncDisposable> IPackageWriteTransactionLifetime.BeginPackageWritesAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_fixtureLease is not { } lease) return CompletedPackageWriteLifetime.Instance;
		return await BeginRetainedSemanticFixturePackageOperationAsync(lease.Owner, cancellationToken);
	}

	private sealed class CompletedPackageWriteLifetime : IAsyncDisposable
	{
		internal static readonly CompletedPackageWriteLifetime Instance = new();
		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
