using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Storage service for wiki image/file assets.
/// Implementations store the raw bytes plus a <see cref="WikiAsset"/> metadata record.
/// </summary>
/// <remarks>
/// Mirrors the conventions of <see cref="IWikiService"/>: lookups that may miss return
/// <see cref="Found{T}"/>; operations that can fail with a message return
/// <see cref="Result{T}"/>.
/// </remarks>
public interface IWikiAssetService
{
	/// <summary>
	/// Stores a new asset from <paramref name="content"/>, computing its SHA-256 while writing.
	/// Returns the stored metadata, or <c>Error&lt;string&gt;</c> with a human-readable message on failure.
	/// </summary>
	Task<Result<WikiAsset>> SaveAsync(
		string fileName,
		string contentType,
		Stream content,
		string uploaderDbref,
		CancellationToken ct = default);

	/// <summary>
	/// Opens an asset for reading. The caller owns (and must dispose) the returned stream.
	/// Returns <c>NotFound</c> when no asset with <paramref name="id"/> exists.
	/// </summary>
	Task<Found<(WikiAsset Asset, Stream Content)>> OpenAsync(string id, CancellationToken ct = default);

	/// <summary>
	/// Lists stored asset metadata, newest first, with skip/take pagination.
	/// </summary>
	Task<IReadOnlyList<WikiAsset>> ListAsync(int skip = 0, int take = 100);

	/// <summary>
	/// Deletes an asset and its metadata.
	/// Returns <c>None</c> if an asset was found and deleted; <c>NotFound</c> otherwise.
	/// </summary>
	Task<Found<None>> DeleteAsync(string id);
}
