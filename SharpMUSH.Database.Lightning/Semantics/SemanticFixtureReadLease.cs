namespace SharpMUSH.Database.Lightning.Semantics;

/// <summary>
/// One admitted semantic read on one exact issued native lifetime. While it is admitted, activation
/// refuses busy. Close does not wait for it: it never holds a native transaction between sections, the
/// store's own close waits for a section in progress, and the read ends with
/// <c>SemanticReadResult.StorageClosed</c> once the lease is closing.
/// </summary>
internal sealed class SemanticFixtureReadLease(SemanticFixtureLease lease) : IDisposable
{
	private int _released;
	internal SemanticFixtureLease Lease { get; } = lease;

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _released, 1) != 0) return;
		Lease.Owner.ReleaseRead(this);
	}
}

internal sealed partial class SemanticFixtureCapability
{
	private readonly Dictionary<SemanticFixtureLease, HashSet<SemanticFixtureReadLease>> _reads = new(ReferenceEqualityComparer.Instance);

	private bool HasAdmittedReads(SemanticFixtureLease lease) => _reads.TryGetValue(lease, out var reads) && reads.Count != 0;

	/// <summary>Whether <paramref name="lease"/> is still the open, current lease, so a read on it may go on.</summary>
	internal bool IsOpenForReads(SemanticFixtureLease lease)
	{
		lock (_gate) return ReferenceEquals(_active, lease) && !lease.IsClosing && !lease.NativeClosed;
	}

	/// <summary>Admits a read on the current, open, issued lease; refused while closing, activating or failed.</summary>
	internal SemanticFixtureReadLease AdmitRead(SemanticFixtureLease lease)
	{
		lock (_gate)
		{
			if (!ReferenceEquals(lease.Owner, this) || !ReferenceEquals(_active, lease) || !lease.IsInitialized || lease.IsClosing
				|| lease.NativeClosed || _ownership.IsRevoked || _ownership.IsQuarantined || _poisoned
				|| _phase is SemanticFixturePhase.Activating or SemanticFixturePhase.Failed)
				throw Unavailable();
			var read = new SemanticFixtureReadLease(lease);
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
}
