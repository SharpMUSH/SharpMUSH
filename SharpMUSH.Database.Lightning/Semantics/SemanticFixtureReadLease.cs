namespace SharpMUSH.Database.Lightning.Semantics;

/// <summary>
/// One admitted semantic read on one exact issued native lifetime. While it is admitted, close,
/// reopen, revoke and activation refuse busy; it never holds a native transaction itself.
/// <see cref="Completion"/> and <see cref="Deadline"/> are what a bounded store shutdown drain needs.
/// </summary>
internal sealed class SemanticFixtureReadLease(SemanticFixtureLease lease, CancellationToken deadline) : IDisposable
{
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _released;
	internal SemanticFixtureLease Lease { get; } = lease;
	internal CancellationToken Deadline { get; } = deadline;
	internal Task Completion => _completion.Task;

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _released, 1) != 0) return;
		try { Lease.Owner.ReleaseRead(this); }
		finally { _completion.TrySetResult(); }
	}
}

internal sealed partial class SemanticFixtureCapability
{
	private readonly Dictionary<SemanticFixtureLease, HashSet<SemanticFixtureReadLease>> _reads = new(ReferenceEqualityComparer.Instance);

	private bool HasAdmittedReads(SemanticFixtureLease lease) => _reads.TryGetValue(lease, out var reads) && reads.Count != 0;

	/// <summary>Admits a read on the current, open, issued lease; refused while closing, activating or failed.</summary>
	internal SemanticFixtureReadLease AdmitRead(SemanticFixtureLease lease, CancellationToken deadline)
	{
		lock (_gate)
		{
			if (!ReferenceEquals(lease.Owner, this) || !ReferenceEquals(_active, lease) || !lease.IsInitialized || lease.IsClosing
				|| lease.NativeClosed || _ownership.IsRevoked || _ownership.IsQuarantined || _poisoned
				|| _phase is SemanticFixturePhase.Activating or SemanticFixturePhase.Failed)
				throw Unavailable();
			var read = new SemanticFixtureReadLease(lease, deadline);
			if (!_reads.TryGetValue(lease, out var reads)) _reads.Add(lease, reads = new(ReferenceEqualityComparer.Instance));
			reads.Add(read);
			return read;
		}
	}

	internal void ReleaseRead(SemanticFixtureReadLease read)
	{
		lock (_gate)
		{
			if (!_reads.TryGetValue(read.Lease, out var reads) || !reads.Remove(read))
				throw new InvalidOperationException("Fixture read ownership is inconsistent.");
			if (reads.Count == 0) _reads.Remove(read.Lease);
		}
	}

	/// <summary>The reads admitted on <paramref name="lease"/> now, for a shutdown that must drain them.</summary>
	internal IReadOnlyList<SemanticFixtureReadLease> AdmittedReads(SemanticFixtureLease lease)
	{
		lock (_gate) return _reads.TryGetValue(lease, out var reads) ? [.. reads] : [];
	}
}
