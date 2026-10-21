using System.Collections.ObjectModel;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.SchedulerModels;

namespace SharpMUSH.Library.Services;

/// <summary>One coordinator owns this bounded accumulator; callbacks may arrive before admission returns.</summary>
internal sealed class PackageOperationObservationBuilder(bool releaseSupported)
{
	private readonly object _sync = new();
	private PackageTaskObservation _body = PackageTaskObservation.NotRun;
	private PackageAdmissionObservation _admission;
	private QueueRejectionReason _rejection;
	private QueueEntryRelease? _bodyRelease;
	private long _collected, _admitted, _refused, _admissionFailures, _released, _unobserved;
	private readonly Dictionary<PackageLifecycleOutcome, long> _lifecycle = [];
	private readonly Dictionary<QueueOutcome, long> _queue = [];

	public void BodyAdmission(QueueAdmissionResult admission)
	{
		lock (_sync)
		{
			_admission = admission.Accepted ? PackageAdmissionObservation.Accepted : PackageAdmissionObservation.Refused;
			_rejection = admission.Reason;
		}
	}
	public void BodyAdmissionFailed() { lock (_sync) _admission = PackageAdmissionObservation.Failed; }
	public void BodyReleased(QueueEntryRelease? release)
	{
		lock (_sync) _bodyRelease = release is not null && Enum.IsDefined(release.Outcome) ? release : null;
	}
	public void BodyCompleted<T>(Task<T> task, int hooks)
	{
		lock (_sync)
		{
			_body = task.IsCanceled ? PackageTaskObservation.Cancelled : task.IsFaulted
				? PackageTaskObservation.Faulted : PackageTaskObservation.Returned;
			_collected = hooks;
		}
	}
	public void HookAdmission(QueueAdmissionResult admission)
	{
		lock (_sync)
		{
			if (admission.Accepted) _admitted++;
			else _refused++;
		}
	}
	public void HookAdmissionFailed() { lock (_sync) _admissionFailures++; }
	public void HookReleased(PackageLifecycleOutcome outcome, QueueEntryRelease? release)
	{
		lock (_sync)
		{
			_released++;
			if (!Enum.IsDefined(outcome)) outcome = PackageLifecycleOutcome.Unknown;
			_lifecycle[outcome] = _lifecycle.GetValueOrDefault(outcome) + 1;
			if (release is not null && Enum.IsDefined(release.Outcome))
				_queue[release.Outcome] = _queue.GetValueOrDefault(release.Outcome) + 1;
			else _unobserved++;
		}
	}
	public PackageOperationObservation Snapshot(PackageTaskObservation coordinator)
	{
		lock (_sync) return new(coordinator, _body, _admission, _rejection, releaseSupported, _bodyRelease,
			_collected, _admitted, _refused, _admissionFailures, _released, _unobserved,
			new ReadOnlyDictionary<PackageLifecycleOutcome, long>(new Dictionary<PackageLifecycleOutcome, long>(_lifecycle)),
			new ReadOnlyDictionary<QueueOutcome, long>(new Dictionary<QueueOutcome, long>(_queue)));
	}
}
