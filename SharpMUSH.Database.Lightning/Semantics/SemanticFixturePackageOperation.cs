namespace SharpMUSH.Database.Lightning.Semantics;

/// <summary>A retained package operation on one exact issued native lifetime; never a settlement grant.</summary>
internal sealed class SemanticFixturePackageOperation(SemanticFixtureLease lease) : IAsyncDisposable
{
	private SemanticFixtureLease? _retained = lease;
	internal SemanticFixtureLease Lease { get; } = lease;

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _retained, null) is { } retained)
			retained.Owner.ReleasePackageOperation(this);
		return ValueTask.CompletedTask;
	}
}

internal sealed class SemanticFixtureOperationInProgressException()
	: InvalidOperationException("An issued fixture operation is still in progress.");

internal sealed partial class SemanticFixtureCapability
{
	private readonly Dictionary<SemanticFixtureLease, HashSet<SemanticFixturePackageOperation>> _packageOperations = new(ReferenceEqualityComparer.Instance);

	private bool HasRetainedOperations(SemanticFixtureLease lease)
		=> _packageOperations.TryGetValue(lease, out var operations) && operations.Count != 0;
	internal SemanticFixturePackageOperation RetainPackageOperation(SemanticFixtureLease lease)
	{
		lock (_gate)
		{
			RequirePackageAttempt(lease);
			var operation = new SemanticFixturePackageOperation(lease);
			if (!_packageOperations.TryGetValue(lease, out var operations))
				_packageOperations.Add(lease, operations = new(ReferenceEqualityComparer.Instance));
			operations.Add(operation);
			return operation;
		}
	}

	internal void ReleasePackageOperation(SemanticFixturePackageOperation operation)
	{
		lock (_gate)
		{
			var lease = operation.Lease;
			if (!ReferenceEquals(lease.Owner, this) || !_packageOperations.TryGetValue(lease, out var operations)
				|| !operations.Remove(operation))
				throw new InvalidOperationException("Fixture operation ownership is inconsistent.");
			// Only the exact holder minted under this gate can release its captured native lifetime.
			if (operations.Count == 0) _packageOperations.Remove(lease);
		}
	}

	internal void BeginNativeOpen(SemanticFixtureLease lease)
	{
		lock (_gate)
		{
			if (!ReferenceEquals(_active, lease) || lease.NativeOpenAttempted) throw Unavailable();
			lease.NativeOpenAttempted = true;
		}
	}

	internal void RequireCurrentMutationLease(SemanticFixtureLease lease)
	{
		lock (_gate)
		{
			if (!ReferenceEquals(_active, lease) || lease.IsClosing || lease.NativeClosed
				|| _ownership.IsRevoked || _ownership.IsQuarantined || _poisoned || _phase == SemanticFixturePhase.Failed)
				throw Unavailable();
		}
	}

	internal void BeginNativeClose(SemanticFixtureLease lease)
	{
		lock (_gate)
		{
			if (lease.NativeClosed) return;
			if (!ReferenceEquals(_active, lease)) throw new InvalidOperationException("Fixture storage lease is not current.");
			if (HasRetainedOperations(lease)) throw new SemanticFixtureOperationInProgressException();
			// This transition and hold admission use the same gate. Do not retain the gate while
			// draining the writer or closing native handles (native callbacks acquire it too).
			lease.IsClosing = true;
		}
	}

	internal void ConfirmNativeClose(SemanticFixtureLease lease)
	{
		lock (_gate)
		{
			if (!ReferenceEquals(_active, lease) || !lease.IsClosing || HasRetainedOperations(lease))
				throw new InvalidOperationException("Fixture native close ownership is inconsistent.");
			lease.NativeClosed = true;
		}
	}
}
