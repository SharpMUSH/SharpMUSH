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
public record WikiAsset(
	string Id,
	string FileName,
	string ContentType,
	long SizeBytes,
	string Sha256,
	string UploaderDbref,
	DateTimeOffset UploadedAt);
