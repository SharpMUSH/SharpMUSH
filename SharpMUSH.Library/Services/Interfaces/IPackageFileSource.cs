using SharpMUSH.Library.DiscriminatedUnions;

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

	/// <summary>
	/// The bytes of <paramref name="path"/> when it is at most <paramref name="maxBytes"/> long. A source that
	/// knows a file's length before it reads it overrides this, so a larger file is never copied; this default
	/// reads the file first.
	/// </summary>
	async Task<PackageFileRead> ReadFileAsync(string path, long maxBytes, CancellationToken cancellationToken = default)
		=> await ReadFileAsync(path, cancellationToken) switch
		{
			null => new NotFound(),
			{ } bytes when bytes.LongLength > maxBytes => new PackageFileTooLarge(bytes.LongLength),
			{ } bytes => bytes
		};
}

/// <summary>A package file longer than the reader allowed.</summary>
/// <param name="Length">Its length in bytes.</param>
public readonly record struct PackageFileTooLarge(long Length);

/// <summary>
/// A package file's bytes, or why there are none: no such file, one too large to read, or a file the package has
/// that its source could not read.
/// </summary>
public union PackageFileRead(byte[], NotFound, PackageFileTooLarge, Error<string>);
