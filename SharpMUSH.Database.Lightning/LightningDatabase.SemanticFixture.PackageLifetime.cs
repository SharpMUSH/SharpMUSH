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
			await BeginSemanticFixturePackageAttemptAsync(owner, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			return operation;
		}
		catch
		{
			await operation.DisposeAsync();
			throw;
		}
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
