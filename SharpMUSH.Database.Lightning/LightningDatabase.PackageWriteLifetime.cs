using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase : IPackageWriteTransactionLifetime
{
	async ValueTask<IPackageWriteHold> IPackageWriteTransactionLifetime.BeginPackageWritesAsync(CancellationToken cancellationToken)
		=> await BeginSemanticFixturePackageHoldAsync(cancellationToken);

	/// <summary>
	/// A hold for package work in this world: on an issued fixture, a child of the live coordinated
	/// operation whose flow this is, or else an attempt of its own, which invalidates and never settles.
	/// </summary>
	internal async ValueTask<IPackageWriteHold> BeginSemanticFixturePackageHoldAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_fixtureLease is not { } lease) return CompletedPackageWriteLifetime.Instance;
		if (_fixtureClosed) throw SemanticFixtureCapability.Unavailable();
		if (lease.Owner.TryJoinPackageOperation(lease, SemanticFixturePackageOperation.Ambient) is { } child) return child;
		return await BeginRetainedSemanticFixturePackageOperationAsync(lease.Owner, cancellationToken);
	}

	private sealed class CompletedPackageWriteLifetime : IPackageWriteHold
	{
		internal static readonly CompletedPackageWriteLifetime Instance = new();
		public void Report(PackageWriteVerdict verdict) { }
		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
