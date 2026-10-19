namespace SharpMUSH.Database.Lightning.Semantics;

public enum WikiFixtureMutationFailure { Quota, WorkLimit, Unavailable, MutationMismatch, UnsupportedOperation }

public sealed class WikiFixtureMutationException(WikiFixtureMutationFailure reason)
	: Exception("The wiki fixture mutation is unavailable or exceeds its supported bounds.")
{
	public WikiFixtureMutationFailure Reason { get; } = reason;
}

internal enum SemanticFixturePhase { Inactive, Activating, Active, Failed }
internal enum SemanticFixtureMutation { LegacyWiki, Unsupported, Pin, Raw }

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
	private bool _everMutatingPackageAdmitted;
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
	internal bool AdmitPackageMutation()
	{
		lock (_gate)
		{
			if (_phase != SemanticFixturePhase.Inactive || _ownership.IsRevoked || _poisoned) return false;
			_everMutatingPackageAdmitted = true;
			return true;
		}
	}
	internal SemanticFixtureMutationLease EnterMutation(SemanticFixtureMutation operation)
	{
		lock (_gate)
		{
			if (_active is null || _ownership.IsRevoked || _poisoned || _phase is SemanticFixturePhase.Activating or SemanticFixturePhase.Failed)
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
			if (!ReferenceEquals(lease.Owner, this) || _active is null || _poisoned || _phase != lease.Phase
				|| _phase is SemanticFixturePhase.Activating or SemanticFixturePhase.Failed) throw Unavailable();
		}
	}
	internal void BeginActivation(SemanticFixtureLease lease)
	{
		lock (_gate)
		{
			if (!ReferenceEquals(lease.Owner, this) || !ReferenceEquals(_active, lease) || !_initialized || !lease.IsInitialized || _poisoned
				|| _ownership.IsRevoked || _ownership.IsQuarantined || _phase != SemanticFixturePhase.Inactive
				|| _legacyMutationsInFlight != 0 || _everMutatingPackageAdmitted || !_installerFenceInstalled || !_runnerFenceInstalled)
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
