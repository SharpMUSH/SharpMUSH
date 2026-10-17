using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tools.WikiSemantics;

public sealed record DatasetFailure(string Detail);
public sealed class VerifiedDataset
{
	internal VerifiedDataset(PackageManifest manifest, ImmutableDictionary<string, string> content, ImmutableDictionary<string, string> blobs)
	{
		Pages = manifest.WikiPages.Select(page => page with { Categories = page.Categories.ToImmutableArray() }).ToImmutableArray();
		(Content, Blobs) = (content, blobs);
	}
	/// <summary>Frozen extraction-consumed declarations; unused package permission/configuration graphs are not interpreted.</summary>
	public ImmutableArray<PackageWikiPageSpec> Pages { get; }
	public ImmutableDictionary<string, string> Content { get; }
	public ImmutableDictionary<string, string> Blobs { get; }
}
public union DatasetResult(VerifiedDataset, DatasetFailure);
public union SourcePathResult(string, DatasetFailure);
public union ManifestResult(PackageManifest, DatasetFailure);

/// <summary>Authenticates only the declared source files and three license/manifest files against a checked content lock.</summary>
public static class PinnedDataset
{
	public const string Commit = "2659a733a4519b76e64a727be2687794afc04f6b";
	private sealed record LockedFile(string Path, long Bytes, string GitBlob, string Sha256);
	private sealed record ContentLock(string SourceCommit, string Package, string Version, LockedFile[] Files);
	private static readonly UTF8Encoding StrictUtf8 = new(false, true);

	public static SourcePathResult SafePath(string root, string path)
	{
		if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\')
			|| path.Split('/').Any(p => p is "" or "." or ".."))
			return new DatasetFailure("Unsafe source path: " + path);
		try
		{
			var current = Path.GetFullPath(root);
			// Refuse symlinks, including parent directories of the supplied root; no link resolution race is trusted.
			for (var parent = new DirectoryInfo(current); parent is not null; parent = parent.Parent)
				if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return new DatasetFailure("Symbolic source root is unsupported.");
			foreach (var part in path.Split('/'))
			{
				current = Path.Combine(current, part);
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
					return new DatasetFailure("Symbolic source path is unsupported: " + path);
			}
			return current;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
		{
			return new DatasetFailure("Unavailable source path: " + path);
		}
	}

	public static ManifestResult ReadManifest(string yaml)
	{
		var result = new PackageManifestService().ParseManifest(yaml);
		return result switch
		{
			ParsedPackageManifest parsed when parsed.Manifest.WikiPages.All(p => p.File is { } file && file.EndsWith(".md", StringComparison.Ordinal) && p.Markdown is null)
				=> parsed.Manifest,
			ParsedPackageManifest => new DatasetFailure("Only declared external Markdown wiki bodies are supported."),
			_ => new DatasetFailure("Malformed package manifest or colliding source keys.")
		};
	}

	public static DatasetResult Load(string root, CancellationToken cancellationToken = default)
	{
		try
		{
			using var stream = typeof(PinnedDataset).Assembly.GetManifestResourceStream("SharpMUSH.Tools.WikiSemantics.pf2e-source-lock.json")
				?? throw new InvalidOperationException("Checked source lock is missing.");
			var contentLock = JsonSerializer.Deserialize<ContentLock>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
				?? throw new InvalidOperationException("Checked source lock is malformed.");
			if (contentLock.SourceCommit != Commit || contentLock.Package != "pf2e-remaster" || contentLock.Version != "1.1.1"
				|| contentLock.Files.Length != 4714 || contentLock.Files.Sum(f => f.Bytes) != 4560161)
				return new DatasetFailure("Checked lock identity or coverage differs from the reviewed pin.");
			var content = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
			var blobs = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
			foreach (var file in contentLock.Files)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (file.Bytes is < 0 or > 1024 * 1024 || content.ContainsKey(file.Path))
					return new DatasetFailure("Invalid source lock entry.");
				if (SafePath(root, file.Path) is not string path) return new DatasetFailure("Missing or unsafe source file: " + file.Path);
				using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
				if (input.Length != file.Bytes) return new DatasetFailure("Source length mismatch: " + file.Path);
				var bytes = new byte[(int)file.Bytes];
				var offset = 0;
				while (offset < bytes.Length)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var read = input.Read(bytes, offset, Math.Min(16384, bytes.Length - offset));
					if (read == 0) return new DatasetFailure("Truncated source file: " + file.Path);
					offset += read;
				}
				if (input.ReadByte() != -1 || Convert.ToHexStringLower(SHA256.HashData(bytes)) != file.Sha256)
					return new DatasetFailure("Source content mismatch: " + file.Path);
				using var gitHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
				gitHash.AppendData(Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0"));
				gitHash.AppendData(bytes);
				if (Convert.ToHexStringLower(gitHash.GetHashAndReset()) != file.GitBlob)
					return new DatasetFailure("Source blob mismatch: " + file.Path);
				content.Add(file.Path, StrictUtf8.GetString(bytes));
				blobs.Add(file.Path, file.GitBlob);
			}
			if (ReadManifest(content["package.yaml"]) is not PackageManifest manifest)
				return new DatasetFailure("Verified package manifest is unsupported.");
			var required = manifest.WikiPages.Select(p => p.File!).Concat(["package.yaml", "NOTICE.md", "README.md"]).ToHashSet(StringComparer.Ordinal);
			if (!required.SetEquals(content.Keys) || manifest.WikiPages.Count != 4711)
				return new DatasetFailure("Manifest declarations do not exactly match the checked content lock.");
			return new VerifiedDataset(manifest, content.ToImmutable(), blobs.ToImmutable());
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or JsonException or ArgumentException)
		{
			return new DatasetFailure("Source verification failed: " + exception.GetType().Name);
		}
	}
}
