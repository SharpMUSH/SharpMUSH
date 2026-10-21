namespace SharpMUSH.Database.Lightning.Semantics;

public enum WikiFixtureMutationFailure { Quota, WorkLimit, Unavailable, MutationMismatch, UnsupportedOperation }

public sealed class WikiFixtureMutationException(WikiFixtureMutationFailure reason)
	: Exception("The wiki fixture mutation is unavailable or exceeds its supported bounds.")
{
	public WikiFixtureMutationFailure Reason { get; } = reason;
}

internal enum SemanticFixturePhase { Inactive, Activating, Active, Failed }
/// <remarks>
/// <see cref="PackageWiki"/> writes a package's wiki ownership rows, moving owned pages' projection sources with
/// them. <see cref="Requirements"/> writes requirement rows; an active fixture admits only one that leaves every
/// read requirement as it was, since read requirements decide which visibility class a page projects under.
/// </remarks>
internal enum SemanticFixtureMutation { LegacyWiki, Unsupported, Pin, Raw, PackageWiki, Requirements }

internal sealed class SemanticFixtureMutationLease : IDisposable
{
	private SemanticFixtureCapability? _owner;
	internal SemanticFixtureMutationLease(SemanticFixtureCapability owner, SemanticFixturePhase phase)
		=> (_owner, Phase) = (owner, phase);
	internal SemanticFixturePhase Phase { get; }
	internal SemanticFixtureCapability Owner => _owner ?? throw new ObjectDisposedException(nameof(SemanticFixtureMutationLease));
	public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseMutation(Phase);
}

internal sealed partial class SemanticFixtureCapability
{
	private SemanticFixturePhase _phase;
	private int _legacyMutationsInFlight;
	// Package work admitted without a coordinator that can prove it ended: it permanently precludes activation.
	private bool _uncoordinatedPackageAdmitted;
	private bool _installerFenceInstalled;
	private bool _runnerFenceInstalled;
	private bool _layoutMutationStarted;
	internal SemanticFixturePhase Phase { get { lock (_gate) return _phase; } }
	internal int LegacyMutationsInFlight { get { lock (_gate) return _legacyMutationsInFlight; } }
	internal void RegisterPackageFence(bool runner)
	{
		lock (_gate)
		{
			if (_phase != SemanticFixturePhase.Inactive && !(runner ? _runnerFenceInstalled : _installerFenceInstalled)) throw Unavailable();
			if (runner) _runnerFenceInstalled = true; else _installerFenceInstalled = true;
		}
	}
	/// <summary>
	/// Admits package work. An inactive fixture admits any; work without a coordinator is remembered and then
	/// precludes activation. An active fixture admits only coordinated work, whose settlement proves it ended.
	/// </summary>
	internal bool AdmitPackageMutation(bool coordinated = false)
	{
		lock (_gate)
		{
			if (_active is null || _active.IsClosing || _active.NativeClosed || _ownership.IsRevoked
				|| _ownership.IsQuarantined || _poisoned) return false;
			if (_phase == SemanticFixturePhase.Inactive)
			{
				if (!coordinated) _uncoordinatedPackageAdmitted = true;
				return true;
			}
			return _phase == SemanticFixturePhase.Active && coordinated;
		}
	}
	internal void RequirePackageAttempt(SemanticFixtureLease lease, bool coordinated = false)
	{
		lock (_gate)
		{
			if (!ReferenceEquals(lease.Owner, this) || !ReferenceEquals(_active, lease)
				|| !_initialized || !lease.IsInitialized || lease.IsClosing || lease.NativeClosed
				|| !(_phase == SemanticFixturePhase.Inactive || _phase == SemanticFixturePhase.Active && coordinated)
				|| _ownership.IsRevoked || _ownership.IsQuarantined || _poisoned) throw Unavailable();
			if (_phase == SemanticFixturePhase.Inactive && !coordinated) _uncoordinatedPackageAdmitted = true;
		}
	}

	/// <summary>Whether <paramref name="operation"/> is a live root package operation on <paramref name="lease"/>.</summary>
	internal bool IsLiveRoot(SemanticFixtureLease lease, SemanticFixturePackageOperation? operation)
	{
		lock (_gate)
			return operation is { IsRoot: true, IsReleased: false } && ReferenceEquals(operation.Lease, lease)
				&& _packageOperations.TryGetValue(lease, out var operations) && operations.Contains(operation);
	}
	internal SemanticFixtureMutationLease EnterMutation(SemanticFixtureMutation operation)
	{
		lock (_gate)
		{
			if (_active is null || _active.IsClosing || _active.NativeClosed || _ownership.IsRevoked || _poisoned || _phase is SemanticFixturePhase.Activating or SemanticFixturePhase.Failed)
				throw Unavailable();
			if (_phase == SemanticFixturePhase.Active && operation == SemanticFixtureMutation.Unsupported)
				throw new WikiFixtureMutationException(WikiFixtureMutationFailure.UnsupportedOperation);
			var lease = new SemanticFixtureMutationLease(this, _phase);
			if (_phase == SemanticFixturePhase.Inactive) _legacyMutationsInFlight = checked(_legacyMutationsInFlight + 1);
			return lease;
		}
	}
	internal void ReleaseMutation(SemanticFixturePhase phase)
	{
		lock (_gate) { if (phase == SemanticFixturePhase.Inactive) _legacyMutationsInFlight--; }
	}
	internal void RequireMutationPhase(SemanticFixtureMutationLease lease)
	{
		lock (_gate)
		{
			if (!ReferenceEquals(lease.Owner, this) || _active is null || _active.IsClosing || _active.NativeClosed || _poisoned || _phase != lease.Phase
				|| _phase is SemanticFixturePhase.Activating or SemanticFixturePhase.Failed) throw Unavailable();
		}
	}
	internal void BeginActivation(SemanticFixtureLease lease)
	{
		lock (_gate)
		{
			if (HasAdmittedReads(lease)) throw new SemanticFixtureOperationInProgressException();
			if (!ReferenceEquals(lease.Owner, this) || !ReferenceEquals(_active, lease) || !_initialized || !lease.IsInitialized || _poisoned
				|| _ownership.IsRevoked || _ownership.IsQuarantined || lease.IsClosing || lease.NativeClosed || _phase != SemanticFixturePhase.Inactive
				|| _legacyMutationsInFlight != 0 || _uncoordinatedPackageAdmitted || HasRetainedOperations(lease)
				|| !_installerFenceInstalled || !_runnerFenceInstalled)
				throw Unavailable();
			_phase = SemanticFixturePhase.Activating;
			_layoutMutationStarted = false;
		}
	}
	internal void LayoutMutationStarting()
	{
		lock (_gate)
		{
			if (_phase != SemanticFixturePhase.Activating) throw Unavailable();
			_layoutMutationStarted = true;
		}
	}
	internal void ActivationCommitted()
	{
		lock (_gate)
		{
			if (_phase != SemanticFixturePhase.Activating || !_layoutMutationStarted) throw Unavailable();
			_phase = SemanticFixturePhase.Active;
		}
	}
	internal void ActivationFailed()
	{
		lock (_gate) _phase = _layoutMutationStarted ? SemanticFixturePhase.Failed : SemanticFixturePhase.Inactive;
	}
	internal void MutationUnavailable() { lock (_gate) _phase = SemanticFixturePhase.Failed; }
	internal static WikiFixtureMutationException Unavailable() => new(WikiFixtureMutationFailure.Unavailable);
}
