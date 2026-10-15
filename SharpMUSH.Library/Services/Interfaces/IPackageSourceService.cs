using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Git access to package remotes (decisions 20.4, 20.14): repos are cloned to
/// a local cache and re-fetched on browse — never background-polled. Versions
/// are release tags named <c>&lt;package-dir&gt;/v&lt;semver&gt;</c> (bare
/// <c>v&lt;semver&gt;</c> for single-package repos); the branch tip is the dev
/// channel. Reads happen from commit trees — the working copy is never
/// consulted, so a moved tag can't smuggle different content past the
/// recorded commit.
/// </summary>
public interface IPackageSourceService
{
	/// <summary>
	/// Clones or fetches the remote's cache, then discovers packages: from
	/// index.yaml when present, otherwise by scanning for package.yaml files.
	/// </summary>
	Task<Result<PackageRepoSnapshot>> RefreshAsync(
		PackageRemoteRecord remote, CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads a package's manifest: from the release tag for
	/// <paramref name="version"/>, or from the pinned/default branch tip when
	/// null. Assumes a prior <see cref="RefreshAsync"/> populated the cache
	/// (refreshes automatically when it hasn't).
	/// </summary>
	Task<Result<PackageManifestSource>> GetManifestAsync(
		PackageRemoteRecord remote, string path, string? version = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Fetches and reports update status for an installed package: newest
	/// release tag vs installed version, dev-channel path changes since
	/// installed_commit, and moved-tag detection (decision 20.14).
	/// </summary>
	Task<Result<PackageUpdateInfo>> CheckForUpdateAsync(
		PackageRemoteRecord remote, InstalledPackageRecord installed,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads the curated community-repo listings from an official repo's
	/// <c>community/</c> directory at the branch tip. Files whose names start
	/// with <c>_</c> or <c>.</c> (templates, dotfiles) are skipped; unparsable
	/// files are reported as per-file errors without hiding the valid ones.
	/// </summary>
	Task<Result<CommunityRepoDirectory>> GetCommunityListingsAsync(
		PackageRemoteRecord remote, CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads a README.md from a remote: the repo root when
	/// <paramref name="path"/> is empty, otherwise the package directory; at a
	/// release tag when <paramref name="version"/> is given, otherwise the
	/// branch tip. Returns raw markdown — render with the portal pipeline.
	/// </summary>
	Task<Result<string>> GetReadmeAsync(
		PackageRemoteRecord remote, string path, string? version = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Builds a reader over the files a package carries beside <c>package.yaml</c>: a plugin package's binaries and
	/// a content pack's wiki page bodies, by their path in the package directory. It reads from the exact
	/// <paramref name="commit"/> the manifest was fetched at, so a moved tag cannot smuggle in other bytes than the
	/// ones reviewed (and, for a binary, the SHA-256 the manifest signed off on).
	/// </summary>
	Task<Result<IPackageFileSource>> GetFileSourceAsync(
		PackageRemoteRecord remote, string path, string commit,
		CancellationToken cancellationToken = default);
}
