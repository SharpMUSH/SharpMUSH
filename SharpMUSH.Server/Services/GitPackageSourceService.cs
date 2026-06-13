using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LibGit2Sharp;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Packages;
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

	public async Task<OneOf<PackageRepoSnapshot, Error<string>>> RefreshAsync(
		PackageRemoteRecord remote, CancellationToken cancellationToken = default)
	{
		return await WithRepoAsync(remote, repository =>
		{
			var tip = ResolveTip(repository, remote.Branch);
			if (tip is null)
			{
				return OneOf<PackageRepoSnapshot, Error<string>>.FromT1(
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

	public async Task<OneOf<PackageManifestSource, Error<string>>> GetManifestAsync(
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
					return OneOf<PackageManifestSource, Error<string>>.FromT1(
						new Error<string>($"Remote '{remote.Name}': branch '{remote.Branch ?? "default"}' not found."));
				}
			}
			else
			{
				var tag = repository.Tags[TagNameFor(path, version)];
				commit = tag?.PeeledTarget as Commit;
				if (commit is null)
				{
					return OneOf<PackageManifestSource, Error<string>>.FromT1(
						new Error<string>($"No release tag '{TagNameFor(path, version)}' on remote '{remote.Name}'."));
				}
			}

			var yaml = ReadBlob(commit, ManifestPathFor(path));
			if (yaml is null)
			{
				return OneOf<PackageManifestSource, Error<string>>.FromT1(
					new Error<string>($"No package.yaml under '{path}' at {(version is null ? "branch tip" : version)}."));
			}

			return new PackageManifestSource(yaml, commit.Sha, version);
		}, cancellationToken);
	}

	public async Task<OneOf<PackageUpdateInfo, Error<string>>> CheckForUpdateAsync(
		PackageRemoteRecord remote, InstalledPackageRecord installed,
		CancellationToken cancellationToken = default)
	{
		return await WithRepoAsync(remote, repository =>
		{
			var path = (installed.SourcePath ?? "").TrimEnd('/');
			var tip = ResolveTip(repository, installed.PinnedBranch ?? remote.Branch);
			if (tip is null)
			{
				return OneOf<PackageUpdateInfo, Error<string>>.FromT1(
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

	public async Task<OneOf<CommunityRepoDirectory, Error<string>>> GetCommunityListingsAsync(
		PackageRemoteRecord remote, CancellationToken cancellationToken = default)
	{
		return await WithRepoAsync(remote, repository =>
		{
			var tip = ResolveTip(repository, remote.Branch);
			if (tip is null)
			{
				return OneOf<CommunityRepoDirectory, Error<string>>.FromT1(
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

					var parsed = manifests.ParseCommunityListing(((Blob)entry.Target).GetContentText());
					parsed.Switch(
						listing => listings.Add(listing),
						failure => errors.Add(
							$"community/{entry.Name}: {string.Join("; ", failure.Errors.Select(e => e.ToString()))}"));
				}
			}

			return new CommunityRepoDirectory(
				listings.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList(),
				errors);
		}, cancellationToken);
	}

	public async Task<OneOf<string, Error<string>>> GetReadmeAsync(
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
				return OneOf<string, Error<string>>.FromT1(new Error<string>(
					$"Remote '{remote.Name}': {(version is null ? "branch tip" : $"release tag for {version}")} not found."));
			}

			var directory = path.TrimEnd('/');
			foreach (var candidate in (string[])["README.md", "README.MD", "readme.md", "Readme.md"])
			{
				var readmePath = directory.Length == 0 ? candidate : $"{directory}/{candidate}";
				var content = ReadBlob(commit, readmePath);
				if (content is not null)
				{
					return content;
				}
			}

			return OneOf<string, Error<string>>.FromT1(new Error<string>(
				$"No README.md under '{(directory.Length == 0 ? "repo root" : directory)}' on remote '{remote.Name}'."));
		}, cancellationToken);
	}

	// ── Repo cache management ───────────────────────────────────────────────

	private async Task<OneOf<T, Error<string>>> WithRepoAsync<T>(
		PackageRemoteRecord remote,
		Func<Repository, OneOf<T, Error<string>>> action,
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
					return OneOf<T, Error<string>>.FromT1(
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

	// ── Discovery ───────────────────────────────────────────────────────────

	private List<string> DiscoverPackagePaths(Commit tip)
	{
		// index.yaml is the fast path; a broken index falls back to a tree scan.
		var indexYaml = ReadBlob(tip, "index.yaml");
		if (indexYaml is not null)
		{
			var parsed = manifests.ParseIndex(indexYaml);
			if (parsed.IsT0)
			{
				return parsed.AsT0.Packages.Select(p => p.Path.TrimEnd('/')).ToList();
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
			var parsed = manifests.ParseManifest(yaml);
			if (parsed.IsT0)
			{
				var manifest = parsed.AsT0.Manifest;
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
		foreach (var tag in repository.Tags)
		{
			if (!tag.FriendlyName.StartsWith(prefix, StringComparison.Ordinal))
			{
				continue;
			}

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

	// ── Tree reads ──────────────────────────────────────────────────────────

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
