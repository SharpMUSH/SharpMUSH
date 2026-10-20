using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning.Semantics;

/// <summary>
/// A retained package operation on one exact issued native lifetime; never a settlement grant by
/// itself. A root is one coordinated attempt: it is the only operation whose expected generation
/// the store advances, and only for writes submitted from a flow it owns. A child joins a live root
/// (a transaction, or lifecycle work) and reports how it ended before it is released.
/// </summary>
internal sealed class SemanticFixturePackageOperation(SemanticFixtureLease lease, SemanticFixturePackageOperation? root = null)
	: IPackageWriteHold
{
	private static readonly AsyncLocal<SemanticFixturePackageOperation?> AmbientRoot = new();

	private SemanticFixtureLease? _retained = lease;
	private int _verdict = -1;
	internal SemanticFixtureLease Lease { get; } = lease;
	internal SemanticFixturePackageOperation? Root { get; } = root;
	internal bool IsRoot => Root is null;
	internal bool IsReleased => Volatile.Read(ref _retained) is null;
	internal PackageWriteVerdict? Verdict => Volatile.Read(ref _verdict) is var v and >= 0 ? (PackageWriteVerdict)v : null;

	// Root-only state, read and written under the capability gate.
	internal ulong? ExpectedGeneration { get; set; }
	internal bool Tainted { get; set; }
	internal int OpenChildren { get; set; }
	internal int CommittedChildren { get; set; }
	internal bool ChildFailed { get; set; }

	/// <summary>
	/// The root whose flow is running. Work that captured it may outlive it, so a holder must still
	/// check that it is live on the current lease before joining it.
	/// </summary>
	internal static SemanticFixturePackageOperation? Ambient => AmbientRoot.Value;

	internal static IDisposable EnterAmbient(SemanticFixturePackageOperation root)
	{
		if (!root.IsRoot) throw new InvalidOperationException("Only a root package operation can own a flow.");
		var previous = AmbientRoot.Value;
		AmbientRoot.Value = root;
		return new AmbientScope(previous);
	}

	public void Report(PackageWriteVerdict verdict) => Interlocked.CompareExchange(ref _verdict, (int)verdict, -1);

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _retained, null) is { } retained)
			retained.Owner.ReleasePackageOperation(this);
		return ValueTask.CompletedTask;
	}

	private sealed class AmbientScope(SemanticFixturePackageOperation? previous) : IDisposable
	{
		public void Dispose() => AmbientRoot.Value = previous;
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

	/// <summary>
	/// Joins <paramref name="root"/> when it is still a live, unreleased root on <paramref name="lease"/>;
	/// otherwise null, and the caller takes its own attempt, which invalidates and never settles.
	/// </summary>
	internal SemanticFixturePackageOperation? TryJoinPackageOperation(SemanticFixtureLease lease, SemanticFixturePackageOperation? root)
	{
		lock (_gate)
		{
			if (root is not { IsRoot: true, IsReleased: false } || !ReferenceEquals(root.Lease, lease)
				|| !_packageOperations.TryGetValue(lease, out var operations) || !operations.Contains(root))
				return null;
			RequirePackageAttempt(lease);
			var child = new SemanticFixturePackageOperation(lease, root);
			operations.Add(child);
			root.OpenChildren++;
			return child;
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
			if (operation.Root is { } root)
			{
				root.OpenChildren--;
				if (operation.Verdict == Library.Services.Interfaces.PackageWriteVerdict.Committed) root.CommittedChildren++;
				else root.ChildFailed = true;
			}
			// Only the exact holder minted under this gate can release its captured native lifetime.
			if (operations.Count == 0) _packageOperations.Remove(lease);
		}
	}

	/// <summary>
	/// Attributes one durable package-state bump, from <paramref name="previous"/> to <paramref name="generation"/>,
	/// made by a job submitted from <paramref name="submitter"/>'s flow. The live root that owns that flow
	/// advances its expected generation only when it was still current; every other live root is tainted.
	/// A bump from no owner, or from a released one, taints every live root.
	/// </summary>
	internal void RecordPackageBump(SemanticFixtureLease lease, SemanticFixturePackageOperation? submitter, ulong previous, ulong generation)
	{
		lock (_gate)
		{
			if (!_packageOperations.TryGetValue(lease, out var operations)) return;
			var owner = submitter?.Root ?? submitter;
			foreach (var root in operations)
			{
				if (!root.IsRoot || root.ExpectedGeneration is null && !ReferenceEquals(root, owner)) continue;
				if (!ReferenceEquals(root, owner)) root.Tainted = true;
				else if (root.ExpectedGeneration is null || root.ExpectedGeneration == previous) root.ExpectedGeneration = generation;
				else root.Tainted = true;
			}
		}
	}

	/// <summary>
	/// Whether <paramref name="root"/> may settle <paramref name="generation"/>: it is the only retained
	/// operation on the current lease, untainted, its expected generation is the marker's, every child it
	/// had joined has been released and reported a clean commit, and at least one did.
	/// </summary>
	internal bool MaySettlePackageOperation(SemanticFixtureLease lease, SemanticFixturePackageOperation root, ulong generation)
	{
		lock (_gate)
		{
			RequirePackageAttempt(lease);
			return root is { IsRoot: true, IsReleased: false, Tainted: false, OpenChildren: 0, ChildFailed: false }
				&& root.CommittedChildren > 0 && root.ExpectedGeneration == generation && ReferenceEquals(root.Lease, lease)
				&& _packageOperations.TryGetValue(lease, out var operations) && operations.Count == 1 && operations.Contains(root);
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
