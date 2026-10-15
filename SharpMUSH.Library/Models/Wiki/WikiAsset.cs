namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// Metadata for a binary asset (image) uploaded for use in wiki pages.
/// The asset bytes themselves are stored by an <c>IWikiAssetService</c> implementation;
/// this record carries only the descriptive metadata.
/// </summary>
/// <param name="Id">Storage identifier (opaque; URL-safe).</param>
/// <param name="FileName">Sanitized original file name (display / download name only).</param>
/// <param name="ContentType">MIME type the asset is served with.</param>
/// <param name="SizeBytes">Size of the stored bytes.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the stored bytes.</param>
/// <param name="UploaderDbref">Dbref of the character that uploaded the asset.</param>
/// <param name="UploadedAt">Upload timestamp.</param>
/// <param name="Private">
/// The file belongs to the one gallery it was uploaded to: it is not in the shared image library, and
/// removing it from that gallery deletes it. Sharing it is one way, since a shared file may be in use
/// on a wiki page or in another gallery.
/// </param>
/// <param name="Lock">
/// A permission a player must hold to find the shared file in the library; null for everyone. The
/// uploader and <c>media.admin</c> holders always find it. The file's address is served to anyone, so a
/// lock hides it from the library, not from a page that already shows it.
/// </param>
public record WikiAsset(
	string Id,
	string FileName,
	string ContentType,
	long SizeBytes,
	string Sha256,
	string UploaderDbref,
	DateTimeOffset UploadedAt,
	bool Private = false,
	string? Lock = null);
