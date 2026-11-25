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
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Shared = null)
{
	/// <summary>
	/// The terms the uploader accepted (<c>gallery_consent_text</c>), kept with the image. Null when the game asked
	/// for none, or for an image added before it did. The list endpoint gives it to <c>media.admin</c> holders only.
	/// </summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public GalleryConsent? Consent { get; init; }

	/// <summary>
	/// Official art: staff approved it as the portrait or marked it. Only <c>media.admin</c> holders set or clear it,
	/// and <c>gallery_limit_skips_official</c> leaves it out of the gallery's limits.
	/// </summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Official { get; init; }

	/// <summary>
	/// The owner asked for this image to be the portrait while <c>gallery_portrait_approval</c> is on; it waits for
	/// staff to approve it. At most one image in a gallery waits.
	/// </summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool PortraitPending { get; init; }
}

/// <summary>The terms an uploader accepted for one image.</summary>
/// <param name="Text">The consent text as it read when they accepted it.</param>
/// <param name="By">The uploader's character, as an objid.</param>
/// <param name="At">When they accepted it.</param>
public sealed record GalleryConsent(string Text, string By, DateTimeOffset At);

/// <summary>
/// The game's gallery rules, as the portal needs them before a write: <c>GET /api/gallery/policy</c>.
/// </summary>
/// <param name="ConsentText">The terms an uploader accepts before every upload; null when the game asks for none.</param>
/// <param name="ConsentVersion">A short hash of <paramref name="ConsentText"/>; an upload names it, so terms changed
/// after the uploader read them are refused rather than recorded as accepted.</param>
/// <param name="PortraitApproval">A player's portrait waits for staff approval.</param>
/// <param name="StaffOnly">Only staff (<c>media.admin</c>) change galleries.</param>
/// <param name="MaxImages">How many images a gallery holds.</param>
/// <param name="MaxBytes">How many bytes a gallery's images may take together; null for no limit.</param>
/// <param name="OfficialExempt">Official art is left out of both limits.</param>
/// <param name="IsStaff">The caller holds <c>media.admin</c>: approves portraits, marks official art, and writes a
/// locked gallery.</param>
public sealed record GalleryPolicy(
	string? ConsentText,
	string? ConsentVersion,
	bool PortraitApproval,
	bool StaffOnly,
	int MaxImages,
	long? MaxBytes,
	bool OfficialExempt,
	bool IsStaff);

/// <summary>One portrait waiting for staff: <c>GET /api/gallery/portraits</c>.</summary>
/// <param name="Character">The character's name.</param>
/// <param name="Objid">The character's objid.</param>
/// <param name="Submitted">The image waiting to become the portrait.</param>
/// <param name="Current">The portrait it would replace; null when the character has none.</param>
public sealed record GalleryPortraitSubmission(string Character, string Objid, GalleryEntry Submitted, GalleryEntry? Current);

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
