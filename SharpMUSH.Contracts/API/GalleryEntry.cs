using System.Text.Json.Serialization;

namespace SharpMUSH.Library.API;

/// <summary>
/// One image in a character's profile gallery: the wire contract for <c>/api/profile/{name}/gallery</c>,
/// declared once for the server controller and the browser's <c>GalleryService</c>.
/// </summary>
/// <param name="AssetId">The stored file's id in the shared asset store.</param>
/// <param name="FileName">The uploaded file name.</param>
/// <param name="Url">Site-relative URL of the image.</param>
/// <param name="Caption">Optional caption; the icon's caption is mirrored into <c>IMAGE`ALT</c>.</param>
/// <param name="Order">Display order, 0-based and contiguous after every write.</param>
/// <param name="IsIcon">The avatar (portrait). Exactly one while the gallery has an image other than the banner
/// that carries no warning; mirrored into <c>IMAGE</c>.</param>
/// <param name="IsBanner">The profile banner. At most one, and none is allowed (the profile then draws its
/// hue gradient); mirrored into <c>IMAGE`BANNER</c>.</param>
/// <param name="Tags">Lower-case words the gallery page filters by; null when there are none.</param>
/// <param name="Warning">A content warning ("blood"). The portal covers the image until the viewer chooses to see
/// it, and an image with one is never the avatar or the banner, which show without a click.</param>
/// <param name="Shared">The file is in the shared image library, so removing it from the gallery keeps it. Filled
/// in by the list endpoint from the file itself, never stored in <c>PROFILE`GALLERY</c>.</param>
public record GalleryEntry(
	string AssetId,
	string FileName,
	string Url,
	string? Caption,
	int Order,
	bool IsIcon,
	bool IsBanner = false,
	IReadOnlyList<string>? Tags = null,
	string? Warning = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Shared = null);

/// <summary>A file from the shared image library to add to a gallery: <c>POST /api/profile/{name}/gallery/library</c>.</summary>
/// <param name="AssetId">The file's id in the library.</param>
/// <param name="Use">Null for a gallery image, or <c>avatar</c> or <c>banner</c>, as on an upload.</param>
public sealed record GalleryLibraryPick(string AssetId, string? Use = null);

/// <summary>A report of a gallery image to the game's staff: <c>POST /api/profile/{name}/gallery/{assetId}/report</c>.</summary>
/// <param name="Reason">Why the image should be looked at; required.</param>
public sealed record GalleryReportRequest(string Reason);

/// <summary>Whether the game takes reports of gallery images: <c>GET /api/gallery/reports</c>.</summary>
/// <param name="Taken">True when the event handler has a <c>GALLERY`REPORT</c> attribute to receive them.</param>
public sealed record GalleryReporting(bool Taken);
