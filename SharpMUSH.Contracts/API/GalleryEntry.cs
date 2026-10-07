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
/// <param name="IsIcon">The avatar (portrait). Exactly one while the gallery has an image other than the banner;
/// mirrored into <c>IMAGE</c>.</param>
/// <param name="IsBanner">The profile banner. At most one, and none is allowed (the profile then draws its
/// hue gradient); mirrored into <c>IMAGE`BANNER</c>.</param>
public record GalleryEntry(string AssetId, string FileName, string Url, string? Caption, int Order, bool IsIcon, bool IsBanner = false);
