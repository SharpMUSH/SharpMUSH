namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Reads the files a package carries beside its <c>package.yaml</c>: a plugin package's binaries and a content
/// pack's wiki page bodies. The git-backed source reads them from the commit the manifest came from, so a moved
/// tag cannot change what was reviewed; an upload reads its staging folder; the bundled catalogue reads the
/// server's embedded resources.
/// </summary>
public interface IPackageFileSource
{
	/// <summary>
	/// The bytes of <paramref name="path"/>, relative to the package directory with <c>/</c> between folders
	/// (<see cref="PackageFilePath.Normalize"/>), or <c>null</c> when the package has no such file. A path that
	/// leaves the package directory is never read.
	/// </summary>
	Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default);
}
