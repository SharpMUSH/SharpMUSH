using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LibGit2Sharp;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// LibGit2Sharp-backed implementation of <see cref="IPackageSourceService"/>
/// (decisions 20.4, 20.14). Remotes are cloned to a per-URL cache directory
/// and fetched on demand. All content reads come from commit trees, never the
/// working copy; tag fetches use forced refspecs so moved tags are visible to
/// the moved-tag trust check rather than silently ignored.
/// </summary>
public class GitPackageSourceService(
	IPackageManifestService manifests,
	string? cacheRoot = null) : IPackageSourceService
{
	private readonly string _cacheRoot = cacheRoot
		?? Path.Combine(Path.GetTempPath(), "sharpmush-packages");

	private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepoLocks = new(StringComparer.Ordinal);

	private static readonly string[] FetchRefSpecs =
	[
		"+refs/heads/*:refs/remotes/origin/*",
		"+refs/tags/*:refs/tags/*"
	];

	public async Task<Result<PackageRepoSnapshot>> RefreshAsync(
		PackageRemoteRecord remote, CancellationToken cancellationToken = default)
	{
		return await WithRepoAsync(remote, repository =>
		{
			var tip = ResolveTip(repository, remote.Branch);
			if (tip is null)
			{
				return new Result<PackageRepoSnapshot>(
					new Error<string>($"Remote '{remote.Name}': branch '{remote.Branch ?? "default"}' not found."));
			}

			var paths = DiscoverPackagePaths(tip);
			var entries = new List<PackageRepoEntry>();
			foreach (var path in paths.Order(StringComparer.Ordinal))
			{
				entries.Add(BuildEntry(repository, tip, path));
			}

			return new PackageRepoSnapshot(remote.Name, remote.Url, tip.Sha, entries);
		}, cancellationToken);
	}

	public async Task<Result<PackageManifestSource>> GetManifestAsync(
		PackageRemoteRecord remote, string path, string? version = null,
		CancellationToken cancellationToken = default)
	{
		return await WithRepoAsync(remote, repository =>
		{
			Commit? commit;
			if (version is null)
			{
				commit = ResolveTip(repository, remote.Branch);
				if (commit is null)
				{
					return new Result<PackageManifestSource>(
						new Error<string>($"Remote '{remote.Name}': branch '{remote.Branch ?? "default"}' not found."));
				}
			}
			else
			{
				var tag = repository.Tags[TagNameFor(path, version)];
				commit = tag?.PeeledTarget as Commit;
				if (commit is null)
				{
					return new Result<PackageManifestSource>(
						new Error<string>($"No release tag '{TagNameFor(path, version)}' on remote '{remote.Name}'."));
				}
			}

			var yaml = ReadBlob(commit, ManifestPathFor(path));
			if (yaml is null)
			{
				return new Result<PackageManifestSource>(
					new Error<string>($"No package.yaml under '{path}' at {(version is null ? "branch tip" : version)}."));
			}

			return new PackageManifestSource(yaml, commit.Sha, version);
		}, cancellationToken);
	}

	public async Task<Result<PackageUpdateInfo>> CheckForUpdateAsync(
		PackageRemoteRecord remote, InstalledPackageRecord installed,
		CancellationToken cancellationToken = default)
	{
		return await WithRepoAsync(remote, repository =>
		{
			var path = (installed.SourcePath ?? "").TrimEnd('/');
			var tip = ResolveTip(repository, installed.PinnedBranch ?? remote.Branch);
			if (tip is null)
			{
				return new Result<PackageUpdateInfo>(
					new Error<string>($"Remote '{remote.Name}': branch not found."));
			}

			var versions = CollectVersionTags(repository, path);
			var latest = versions.FirstOrDefault();

			var updateAvailable = false;
			if (latest is not null
				&& PackageVersion.TryParse(latest.Version, out var latestVersion)
				&& PackageVersion.TryParse(installed.Version, out var installedVersion))
			{
				updateAvailable = latestVersion.CompareTo(installedVersion) > 0;
			}

			// Moved-tag trust check: the tag for the installed version must
			// still point at the commit we installed from.
			var installedTag = repository.Tags[TagNameFor(path, installed.Version)];
			var installedTagCommit = (installedTag?.PeeledTarget as Commit)?.Sha;
			var tagMoved = installedTagCommit is not null && installedTagCommit != installed.InstalledCommit;

			// Dev-channel signal: anything under the path changed since install?
			var pathChanged = false;
			var installedCommit = repository.Lookup<Commit>(installed.InstalledCommit);
			if (installedCommit is null)
			{
				// History rewritten or commit unreachable — surface as changed.
				pathChanged = true;
			}
			else if (installedCommit.Sha != tip.Sha)
			{
				var prefix = path.Length == 0 ? "" : path + "/";
				pathChanged = repository.Diff
					.Compare<TreeChanges>(installedCommit.Tree, tip.Tree)
					.Any(change => change.Path.StartsWith(prefix, StringComparison.Ordinal)
						|| change.OldPath.StartsWith(prefix, StringComparison.Ordinal));
			}

			return new PackageUpdateInfo(
				installed.Version, latest?.Version, latest?.Commit, updateAvailable, pathChanged, tagMoved);
		}, cancellationToken);
	}

	public async Task<Result<CommunityRepoDirectory>> GetCommunityListingsAsync(
		PackageRemoteRecord remote, CancellationToken cancellationToken = default)
	{
		return await WithRepoAsync(remote, repository =>
		{
			var tip = ResolveTip(repository, remote.Branch);
			if (tip is null)
			{
				return new Result<CommunityRepoDirectory>(
					new Error<string>($"Remote '{remote.Name}': branch '{remote.Branch ?? "default"}' not found."));
			}

			var listings = new List<CommunityRepoListing>();
			var errors = new List<string>();

			if (tip["community"]?.Target is Tree communityTree)
			{
				foreach (var entry in communityTree)
				{
					if (entry.TargetType != TreeEntryTargetType.Blob
						|| entry.Name.StartsWith('_') || entry.Name.StartsWith('.')
						|| !(entry.Name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
							|| entry.Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)))
					{
						continue;
					}

					switch (manifests.ParseCommunityListing(((Blob)entry.Target).GetContentText()))
					{
						case CommunityRepoListing listing:
							listings.Add(listing);
							break;
						case PackageManifestFailure failure:
							errors.Add(
								$"community/{entry.Name}: {string.Join("; ", failure.Errors.Select(e => e.ToString()))}");
							break;
					}
				}
			}

			return new CommunityRepoDirectory(
				listings.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList(),
				errors);
		}, cancellationToken);
	}

	public async Task<Result<string>> GetReadmeAsync(
		PackageRemoteRecord remote, string path, string? version = null,
		CancellationToken cancellationToken = default)
	{
		return await WithRepoAsync(remote, repository =>
		{
			Commit? commit;
			if (version is null)
			{
				commit = ResolveTip(repository, remote.Branch);
			}
			else
			{
				commit = repository.Tags[TagNameFor(path, version)]?.PeeledTarget as Commit;
			}

			if (commit is null)
			{
				return new Result<string>(new Error<string>(
					$"Remote '{remote.Name}': {(version is null ? "branch tip" : $"release tag for {version}")} not found."));
			}

			var directory = path.TrimEnd('/');
			foreach (var candidate in PackageReadme.FileNames)
			{
				var readmePath = directory.Length == 0 ? candidate : $"{directory}/{candidate}";
				switch (ReadBlobBytes(repository, commit, readmePath, PackageReadme.MaxBytes, cancellationToken))
				{
					case byte[] bytes:
						return PackageReadme.Decode(bytes) is string content
							? content
							: new Result<string>(new Error<string>($"{readmePath} is not UTF-8 text."));
					case PackageFileTooLarge:
						return new Result<string>(new Error<string>(
							$"{readmePath} is larger than {PackageReadme.MaxBytes / 1024} KB."));
				}
			}

			return new Result<string>(new Error<string>(
				$"No README.md under '{(directory.Length == 0 ? "repo root" : directory)}' on remote '{remote.Name}'."));
		}, cancellationToken);
	}

	public async Task<Result<IPackageFileSource>> GetFileSourceAsync(
		PackageRemoteRecord remote, string path, string commit, CancellationToken cancellationToken = default)
	{
		return await WithRepoAsync(remote, repository =>
		{
			if (repository.Lookup<Commit>(commit) is not { } resolved)
			{
				return new Result<IPackageFileSource>(
					new Error<string>($"Remote '{remote.Name}': commit '{commit}' is not in the cache."));
			}

			// Only the directory's trees are read here, for each file's object id; a file's bytes are read when
			// it is asked for, and only if it fits.
			var directory = path.TrimEnd('/');
			var blobs = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
			var visited = 0;
			if ((directory.Length == 0 ? resolved.Tree : resolved[directory]?.Target as Tree) is { } tree
				&& !IndexBlobs(tree, "", blobs, ref visited, cancellationToken))
			{
				return new Result<IPackageFileSource>(new Error<string>(
					$"Remote '{remote.Name}': '{directory}' holds more than {MaxPackageFiles} files."));
			}

			return new Result<IPackageFileSource>(new GitCommitFileSource(this, remote, blobs));
		}, cancellationToken);
	}

	/// <summary>The most files a package directory may hold.</summary>
	public const int MaxPackageFiles = 100_000;

	/// <summary>
	/// Every blob under <paramref name="tree"/> by its path below it with <c>/</c> between folders, without reading
	/// any of them; false when there are more than <see cref="MaxPackageFiles"/> entries, folders included, since
	/// a tree can name the same folder many times over. A symbolic link is left out.
	/// </summary>
	private static bool IndexBlobs(Tree tree, string prefix, Dictionary<string, ObjectId> blobs, ref int visited,
		CancellationToken cancellationToken)
	{
		foreach (var entry in tree)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (++visited > MaxPackageFiles)
			{
				return false;
			}

			switch (entry.TargetType)
			{
				case TreeEntryTargetType.Blob when entry.Mode != Mode.SymbolicLink:
					blobs[prefix + entry.Name] = entry.Target.Id;
					break;
				case TreeEntryTargetType.Tree
					when !IndexBlobs((Tree)entry.Target, $"{prefix}{entry.Name}/", blobs, ref visited, cancellationToken):
					return false;
			}
		}

		return true;
	}

	/// <summary>
	/// The files under one package directory at one commit, read from the cached clone one at a time and never
	/// fetched again, so what is read is what the commit holds. A file is copied only when it fits the reader's
	/// limit; anything the package does not ask for is never read. A pack reads its files one after another, so
	/// the clone stays open between reads and is closed once they stop.
	/// </summary>
	private sealed class GitCommitFileSource(GitPackageSourceService service, PackageRemoteRecord remote,
		IReadOnlyDictionary<string, ObjectId> blobs) : IPackageFileSource
	{
		private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(2);

		private readonly SemaphoreSlim _gate = RepoLocks.GetOrAdd(remote.Url, _ => new SemaphoreSlim(1, 1));

		// Used and replaced only while _gate is held.
		private Repository? _open;
		private Timer? _idle;

		public async Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default)
			=> await ReadFileAsync(path, Array.MaxLength, cancellationToken) is byte[] bytes ? bytes : null;

		public async Task<PackageFileRead> ReadFileAsync(string path, long maxBytes, CancellationToken cancellationToken = default)
		{
			if (PackageFilePath.Normalize(path) is not { } normalized || !blobs.TryGetValue(normalized, out var id))
			{
				return new NotFound();
			}

			await _gate.WaitAsync(cancellationToken);
			try
			{
				return await Task.Run(() => Read(normalized, id, maxBytes, cancellationToken), cancellationToken);
			}
			finally
			{
				_idle ??= new Timer(_ => Close());
				_idle.Change(IdleClose, Timeout.InfiniteTimeSpan);
				_gate.Release();
			}
		}

		/// <summary>
		/// The file the commit holds at <paramref name="path"/>; an error, not <see cref="NotFound"/>, when the clone
		/// can no longer give it, since the package does have the file.
		/// </summary>
		private PackageFileRead Read(string path, ObjectId id, long maxBytes, CancellationToken cancellationToken)
		{
			try
			{
				return Open() is not { } repository
					? new Error<string>($"Remote '{remote.Name}': the cached clone is gone; refresh the remote.")
					: repository.Lookup<Blob>(id) is { } blob
						? ReadBlobBytes(repository, blob, maxBytes, cancellationToken)
						: new Error<string>($"Remote '{remote.Name}': the cached clone is missing {path}; refresh the remote.");
			}
			catch (Exception ex) when (ex is LibGit2SharpException or IOException)
			{
				return new Error<string>($"Remote '{remote.Name}': {path} could not be read from the cached clone: {ex.Message}");
			}
		}

		/// <summary>The cached clone, without fetching or cloning; null when there is none.</summary>
		private Repository? Open()
		{
			var directory = service.CacheDirectoryFor(remote.Url);
			return _open ??= Repository.IsValid(directory) ? new Repository(directory) : null;
		}

		private void Close()
		{
			// A read in progress keeps it open; that read schedules the next close.
			if (!_gate.Wait(0))
			{
				return;
			}

			try
			{
				_open?.Dispose();
				_open = null;
			}
			finally
			{
				_gate.Release();
			}
		}
	}

	/// <summary>
	/// The blob at <paramref name="path"/> in <paramref name="commit"/>, when it is at most <paramref name="maxBytes"/>
	/// long. A symbolic link is not a file the package carries.
	/// </summary>
	private static PackageFileRead ReadBlobBytes(Repository repository, Commit commit, string path, long maxBytes,
		CancellationToken cancellationToken)
		=> commit[path] is { TargetType: TreeEntryTargetType.Blob, Target: Blob blob } entry && entry.Mode != Mode.SymbolicLink
			? ReadBlobBytes(repository, blob, maxBytes, cancellationToken)
			: new NotFound();

	/// <summary>
	/// <paramref name="blob"/>'s bytes when it is at most <paramref name="maxBytes"/> long. Its length comes from the
	/// object header, so a larger blob is refused before it is copied.
	/// </summary>
	private static PackageFileRead ReadBlobBytes(Repository repository, Blob blob, long maxBytes, CancellationToken cancellationToken)
	{
		var length = repository.ObjectDatabase.RetrieveObjectMetadata(blob.Id).Size;
		if (length > maxBytes || length > Array.MaxLength)
		{
			return new PackageFileTooLarge(length);
		}

		var bytes = new byte[length];
		using var stream = blob.GetContentStream();
		var offset = 0;
		while (offset < bytes.Length)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var read = stream.Read(bytes, offset, Math.Min(81920, bytes.Length - offset));
			if (read == 0)
			{
				break;
			}

			offset += read;
		}

		if (offset != bytes.Length || stream.ReadByte() != -1)
		{
			throw new IOException($"Blob {blob.Sha} did not have the length its header gave.");
		}

		return bytes;
	}

	private async Task<Result<T>> WithRepoAsync<T>(
		PackageRemoteRecord remote,
		Func<Repository, Result<T>> action,
		CancellationToken cancellationToken)
	{
		var gate = RepoLocks.GetOrAdd(remote.Url, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken);
		try
		{
			return await Task.Run(() =>
			{
				try
				{
					var directory = CacheDirectoryFor(remote.Url);
					if (!Repository.IsValid(directory))
					{
						Directory.CreateDirectory(directory);
						Repository.Clone(remote.Url, directory);
					}

					using var repository = new Repository(directory);
					// TagFetchMode.None disables libgit2's opportunistic tag
					// following (which refuses to move existing tags) so the
					// explicit forced tag refspec wins — moved tags MUST become
					// visible for the trust check.
					Commands.Fetch(repository, "origin", FetchRefSpecs,
						new FetchOptions { TagFetchMode = TagFetchMode.None }, null);
					return action(repository);
				}
				catch (LibGit2SharpException ex)
				{
					return new Result<T>(
						new Error<string>($"Remote '{remote.Name}' ({remote.Url}): {ex.Message}"));
				}
			}, cancellationToken);
		}
		finally
		{
			gate.Release();
		}
	}

	private string CacheDirectoryFor(string url) => Path.Combine(
		_cacheRoot,
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant());

	private static Commit? ResolveTip(Repository repository, string? branch)
	{
		if (branch is not null)
		{
			return repository.Branches[$"origin/{branch}"]?.Tip ?? repository.Branches[branch]?.Tip;
		}

		return repository.Branches["origin/HEAD"]?.Tip ?? repository.Head?.Tip;
	}

	private List<string> DiscoverPackagePaths(Commit tip)
	{
		// index.yaml is the fast path; a broken index falls back to a tree scan.
		var indexYaml = ReadBlob(tip, "index.yaml");
		if (indexYaml is not null)
		{
			if (manifests.ParseIndex(indexYaml) is PackageIndex index)
			{
				return index.Packages.Select(p => p.Path.TrimEnd('/')).ToList();
			}
		}

		var found = new List<string>();
		ScanTree(tip.Tree, "", found);
		return found;
	}

	private static void ScanTree(Tree tree, string prefix, List<string> found)
	{
		foreach (var entry in tree)
		{
			switch (entry.TargetType)
			{
				case TreeEntryTargetType.Tree:
					ScanTree((Tree)entry.Target, $"{prefix}{entry.Name}/", found);
					break;
				case TreeEntryTargetType.Blob when entry.Name == "package.yaml":
					found.Add(prefix.TrimEnd('/'));
					break;
			}
		}
	}

	private PackageRepoEntry BuildEntry(Repository repository, Commit tip, string path)
	{
		string? packageId = null;
		string? version = null;
		string? description = null;

		var yaml = ReadBlob(tip, ManifestPathFor(path));
		if (yaml is not null)
		{
			if (manifests.ParseManifest(yaml) is ParsedPackageManifest { Manifest: var manifest })
			{
				packageId = manifest.Name;
				version = manifest.Version.ToString();
				description = manifest.Description;
			}
		}

		return new PackageRepoEntry(path, packageId, version, description, CollectVersionTags(repository, path));
	}

	/// <summary>Release tags for a package path, newest version first.</summary>
	private static List<PackageVersionTag> CollectVersionTags(Repository repository, string path)
	{
		var prefix = path.Length == 0 ? "v" : $"{path}/v";
		var tags = new List<(PackageVersion Version, PackageVersionTag Tag)>();
		foreach (var tag in repository.Tags.Where(tag => tag.FriendlyName.StartsWith(prefix, StringComparison.Ordinal)))
		{
			var versionText = tag.FriendlyName[prefix.Length..];
			// Reject deeper paths that share the prefix (e.g. "bbs/v1" vs "bbs/extras/v1").
			if (versionText.Contains('/') || !PackageVersion.TryParse(versionText, out var version))
			{
				continue;
			}

			if (tag.PeeledTarget is Commit commit)
			{
				tags.Add((version, new PackageVersionTag(version.ToString(), tag.FriendlyName, commit.Sha)));
			}
		}

		return tags
			.OrderByDescending(t => t.Version)
			.Select(t => t.Tag)
			.ToList();
	}

	private static string ManifestPathFor(string path) =>
		path.Length == 0 ? "package.yaml" : $"{path.TrimEnd('/')}/package.yaml";

	private static string TagNameFor(string path, string version)
	{
		var trimmed = path.TrimEnd('/');
		return trimmed.Length == 0 ? $"v{version}" : $"{trimmed}/v{version}";
	}

	private static string? ReadBlob(Commit commit, string path) =>
		commit[path]?.Target is Blob blob ? blob.GetContentText() : null;
}
