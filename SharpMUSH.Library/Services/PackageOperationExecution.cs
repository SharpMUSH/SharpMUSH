using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// One coordinator's retained completion, including body/compensation and release of its directly
/// admitted hook entries. A returned result can still contain an error, hook scripts can fail,
/// and future queued descendants are not joined here. With an observed-release scheduler the body
/// reservation is also joined; other schedulers retain their own release callback contract.
/// This is not a wiki-settlement certificate.
/// </summary>
public sealed class PackageOperationExecution<T>
{
	internal PackageOperationExecution(Task<PackageOperationOutcome<T>> completion, Task<PackageOperationObservation> observation)
	{
		Completion = completion;
		Observation = observation;
	}
	/// <summary>Frozen evidence published after owned releases, even when Completion faults.</summary>
	public Task<PackageOperationObservation> Observation { get; }
	/// <summary>The actual owned task. Use WaitAsync on it to cancel observation independently.</summary>
	public Task<PackageOperationOutcome<T>> Completion { get; }
}
