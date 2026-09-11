using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Parses and validates softcode package manifests (package.yaml) and repo
/// indexes (index.yaml). Pure text-in, model-out — no database or git access.
/// </summary>
public interface IPackageManifestService
{
	/// <summary>
	/// Parses a package.yaml document. Returns the validated manifest (with
	/// any non-blocking warnings) or a failure listing every issue found.
	/// </summary>
	PackageManifestResult<ParsedPackageManifest> ParseManifest(string yaml);

	/// <summary>
	/// Parses an index.yaml repo listing.
	/// </summary>
	PackageManifestResult<PackageIndex> ParseIndex(string yaml);

	/// <summary>
	/// Parses one community repo listing file (a <c>community/*.yaml</c>
	/// document in an official repo). Unknown keys are tolerated silently for
	/// forward compatibility of the listing format.
	/// </summary>
	PackageManifestResult<CommunityRepoListing> ParseCommunityListing(string yaml);
}
