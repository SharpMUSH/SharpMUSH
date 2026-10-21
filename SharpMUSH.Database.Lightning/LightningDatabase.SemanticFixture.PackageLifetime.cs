using SharpMUSH.Database.Lightning.Semantics;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	internal async ValueTask<SemanticFixturePackageOperation> BeginRetainedSemanticFixturePackageOperationAsync(
		SemanticFixtureCapability owner, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_fixtureLease is not { } lease || !ReferenceEquals(lease.Owner, owner) || _fixtureClosed)
			throw SemanticFixtureCapability.Unavailable();
		var operation = owner.RetainPackageOperation(lease);
		try
		{
			await BeginSemanticFixturePackageAttemptAsync(owner, cancellationToken, operation);
			cancellationToken.ThrowIfCancellationRequested();
			return operation;
		}
		catch
		{
			await operation.DisposeAsync();
			throw;
		}
	}

	/// <summary>
	/// A retained child of the live coordinated operation whose flow this is, joined atomically under the
	/// capability gate; null when there is none, or it has been released, and the caller takes its own attempt.
	/// </summary>
	internal SemanticFixturePackageOperation? JoinSemanticFixturePackageOperation()
		=> _fixtureLease is { } lease && !_fixtureClosed
			? lease.Owner.TryJoinPackageOperation(lease, SemanticFixturePackageOperation.Ambient) : null;

	/// <summary>The live coordinated operation whose flow this is, on this provider's current lease; null otherwise.</summary>
	internal SemanticFixturePackageOperation? CurrentSemanticFixturePackageOperation
		=> _fixtureLease is { } lease && SemanticFixturePackageOperation.Ambient is { IsReleased: false } root
			&& ReferenceEquals(root.Lease, lease) ? root : null;

	/// <summary>
	/// Settles <paramref name="root"/>'s generation when the store and capability agree it may; see
	/// <c>LightningStore.SettleFixturePackageAttemptAsync</c>. Only the package coordinator calls this.
	/// </summary>
	internal ValueTask<bool> SettleSemanticFixturePackageOperationAsync(SemanticFixturePackageOperation root)
	{
		if (_fixtureLease is not { } lease || !ReferenceEquals(root.Lease, lease) || _fixtureClosed)
			throw SemanticFixtureCapability.Unavailable();
		return Store.SettleFixturePackageAttemptAsync(lease, root);
	}

	/// <summary>The current issued lease, whose epoch and in-memory key seal query continuations.</summary>
	internal SemanticFixtureLease SemanticFixtureLeaseForQueries
		=> _fixtureLease is { } lease && !_fixtureClosed ? lease : throw SemanticFixtureCapability.Unavailable();

	/// <summary>Admits a semantic read on this provider's current issued lease; see <see cref="SemanticFixtureReadLease"/>.</summary>
	internal SemanticFixtureReadLease AdmitSemanticRead(CancellationToken deadline)
	{
		if (_fixtureLease is not { } lease || _fixtureClosed) throw SemanticFixtureCapability.Unavailable();
		return lease.Owner.AdmitRead(lease, deadline);
	}

	internal void PrepareSemanticFixtureClose()
	{
		Store.RequireExternalDisposal();
		lock (_fixtureDisposeGate)
		{
			_fixtureCloseFailure?.Throw();
			if (!_fixtureClosed && _fixtureLease is { } lease) lease.Owner.BeginNativeClose(lease);
		}
	}
}
