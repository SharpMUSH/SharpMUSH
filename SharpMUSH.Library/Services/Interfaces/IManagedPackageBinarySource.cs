namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Resolves the raw bytes of a file carried alongside <c>package.yaml</c> in a
/// managed package's source (Phase 4). The git-backed implementation reads the
/// blob from the same commit tree the manifest came from; a directory-backed
/// implementation reads from the package source directory. The installer asks
/// only for the flat file names the manifest's <c>binaries:</c> block declares,
/// then verifies each against its SHA-256 before depositing it.
/// </summary>
public interface IManagedPackageBinarySource
{
	/// <summary>
	/// Returns the bytes of <paramref name="fileName"/> from the package source,
	/// or <c>null</c> when the file is absent (the install then rejects the apply
	/// with a clear "declared but missing" error).
	/// </summary>
	Task<byte[]?> ReadBinaryAsync(string fileName, CancellationToken cancellationToken = default);
}
