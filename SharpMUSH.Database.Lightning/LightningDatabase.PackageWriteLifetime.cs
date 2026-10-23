using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase : IPackageWriteTransactionLifetime
{
	ValueTask<IPackageWriteHold> IPackageWriteTransactionLifetime.BeginPackageWritesAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult<IPackageWriteHold>(CompletedPackageWriteLifetime.Instance);
	}

	private sealed class CompletedPackageWriteLifetime : IPackageWriteHold
	{
		internal static readonly CompletedPackageWriteLifetime Instance = new();
		public void Report(PackageWriteVerdict verdict) { }
		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
