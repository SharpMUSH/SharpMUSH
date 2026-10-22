using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.SchedulerModels;

namespace SharpMUSH.Library.Services;

/// <summary>Task-level observation only. Returned can contain a business failure.</summary>
public enum PackageTaskObservation { Unknown, NotRun, Returned, Faulted, Cancelled }
public enum PackageAdmissionObservation { Unknown, Accepted, Refused, Failed }

/// <summary>
/// Bounded, frozen observations of the owned body and directly admitted hooks. This is not a
/// success or settlement certificate; descendants and command-resolution failures may be unobserved.
/// Null release metadata and WithoutReleaseObservation counts include missing or invalid metadata;
/// the capability flag describes the actual scheduler independently. Histograms count released hooks
/// only; refusal and admission failure are separate. Unknown outcomes never imply success.
/// </summary>
public sealed record PackageOperationObservation(
	PackageTaskObservation Coordinator,
	PackageTaskObservation Body,
	PackageAdmissionObservation BodyAdmission,
	QueueRejectionReason BodyRejection,
	bool SchedulerReleaseObservationSupported,
	QueueEntryRelease? BodyRelease,
	long HooksCollected,
	long HooksAdmitted,
	long HooksRefused,
	long HookAdmissionFailures,
	long HooksReleased,
	long HooksWithoutReleaseObservation,
	IReadOnlyDictionary<PackageLifecycleOutcome, long> LifecycleOutcomes,
	IReadOnlyDictionary<QueueOutcome, long> HookQueueOutcomes);
