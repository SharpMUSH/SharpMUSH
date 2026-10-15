using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// Who finds a stored file in the shared image library, where anyone signed in picks pictures for a wiki
/// page or a gallery instead of uploading the same file again, and who may change how it is shared.
/// </summary>
public static class WikiAssetLibrary
{
	/// <summary>
	/// Whether a viewer holding <paramref name="scopes"/> and playing <paramref name="viewerDbref"/> finds
	/// <paramref name="asset"/>: it is shared, and it has no lock, or the viewer holds the lock's permission,
	/// uploaded it, or holds <c>media.admin</c>.
	/// </summary>
	public static bool Finds(WikiAsset asset, IReadOnlySet<string> scopes, string? viewerDbref) =>
		!asset.Private
		&& (asset.Lock is null || scopes.Contains(asset.Lock) || Manages(asset, scopes, viewerDbref));

	/// <summary>Whether the viewer may lock or share <paramref name="asset"/>: they uploaded it, or hold <c>media.admin</c>.</summary>
	public static bool Manages(WikiAsset asset, IReadOnlySet<string> scopes, string? viewerDbref) =>
		scopes.Contains(PortalPermission.MediaAdmin)
		|| (viewerDbref is not null && string.Equals(asset.UploaderDbref, viewerDbref, StringComparison.Ordinal));
}
