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
	/// <param name="isPrivate">Keep the file out of the shared library (<see cref="WikiAsset.Private"/>).</param>
	Task<Result<WikiAsset>> SaveAsync(
		string fileName,
		string contentType,
		Stream content,
		string uploaderDbref,
		bool isPrivate = false,
		CancellationToken ct = default);

	/// <summary>An asset's metadata; <c>NotFound</c> when no asset with <paramref name="id"/> exists.</summary>
	Task<Found<WikiAsset>> FindAsync(string id, CancellationToken ct = default);

	/// <summary>
	/// Opens an asset for reading. The caller owns (and must dispose) the returned stream.
	/// Returns <c>NotFound</c> when no asset with <paramref name="id"/> exists.
	/// </summary>
	Task<Found<OpenedWikiAsset>> OpenAsync(string id, CancellationToken ct = default);

	/// <summary>
	/// Lists stored asset metadata, newest first, with skip/take pagination.
	/// </summary>
	/// <param name="where">Keeps only the assets it admits, before paging.</param>
	Task<IReadOnlyList<WikiAsset>> ListAsync(int skip = 0, int take = 100, Func<WikiAsset, bool>? where = null);

	/// <summary>
	/// Replaces an asset's <see cref="WikiAsset.Private"/> and <see cref="WikiAsset.Lock"/>, returning the
	/// stored metadata; <c>NotFound</c> when no asset with <paramref name="id"/> exists.
	/// </summary>
	Task<Found<WikiAsset>> SetSharingAsync(string id, bool isPrivate, string? lockPermission, CancellationToken ct = default);

	/// <summary>
	/// Deletes an asset and its metadata.
	/// Returns <c>None</c> if an asset was found and deleted; <c>NotFound</c> otherwise.
	/// </summary>
	Task<Found<None>> DeleteAsync(string id);
}
