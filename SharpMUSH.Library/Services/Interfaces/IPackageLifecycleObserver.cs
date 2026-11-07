using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>Optional local verdict capability on the same lifecycle runner used for installs.</summary>
public interface IPackageLifecycleObserver
{
	/// <summary>
	/// Returns read/parse/cancellation metadata without making hook errors fatal to an install.
	/// Deferred means ownership passed to the collecting coordinator, not that execution succeeded.
	/// Parsed only describes the parser's returned verdict; queued descendants and command business
	/// failures not represented by that verdict need independent observation before settlement.
	/// </summary>
	Task<PackageLifecycleOutcome> RunObservedLifecycleAsync(string objId, string attribute, CancellationToken cancellationToken = default);

	/// <summary>Observes a retained hook without losing the committed apply's version provenance.</summary>
	Task<PackageLifecycleOutcome> RunObservedLifecycleAsync(PackageLifecycleHook hook, CancellationToken cancellationToken = default)
		=> RunObservedLifecycleAsync(hook.ObjId, hook.Attribute, cancellationToken);
}
