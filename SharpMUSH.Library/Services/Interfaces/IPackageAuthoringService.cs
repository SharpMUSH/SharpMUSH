using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Authoring support (Phase 7, decision 20.9): turn live game objects into a
/// format-v2 package manifest. Scan finds the dbrefs the admin must classify;
/// export converts every dbref into a symbolic ref and emits YAML with all
/// MUSHcode as block scalars — a manifest is never allowed to carry a dbref.
/// </summary>
public interface IPackageAuthoringService
{
	/// <summary>
	/// Reads the selected objects (attrs, flags, parents) and reports every
	/// dbref in their attribute values that is NOT itself in the selection.
	/// </summary>
	Task<OneOf<PackageAuthoringScan, Error<string>>> ScanAsync(
		IReadOnlyList<string> objids, CancellationToken cancellationToken = default);

	/// <summary>
	/// Exports the selection as a package.yaml document. Fails when any dbref
	/// remains unclassified (in-selection → <c>{{ref}}</c>, classified →
	/// <c>{{$well_known}}</c>/<c>{{?configure}}</c>), and validates the result
	/// through the manifest parser before returning it.
	/// </summary>
	Task<OneOf<string, Error<string>>> ExportAsync(
		PackageAuthoringRequest request, CancellationToken cancellationToken = default);
}
