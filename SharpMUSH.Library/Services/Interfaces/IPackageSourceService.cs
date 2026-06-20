using OneOf;
using OneOf.Types;
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
	Task<OneOf<PackageRepoSnapshot, Error<string>>> RefreshAsync(
		PackageRemoteRecord remote, CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads a package's manifest: from the release tag for
	/// <paramref name="version"/>, or from the pinned/default branch tip when
	/// null. Assumes a prior <see cref="RefreshAsync"/> populated the cache
	/// (refreshes automatically when it hasn't).
	/// </summary>
	Task<OneOf<PackageManifestSource, Error<string>>> GetManifestAsync(
		PackageRemoteRecord remote, string path, string? version = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Fetches and reports update status for an installed package: newest
	/// release tag vs installed version, dev-channel path changes since
	/// installed_commit, and moved-tag detection (decision 20.14).
	/// </summary>
	Task<OneOf<PackageUpdateInfo, Error<string>>> CheckForUpdateAsync(
		PackageRemoteRecord remote, InstalledPackageRecord installed,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads the curated community-repo listings from an official repo's
	/// <c>community/</c> directory at the branch tip. Files whose names start
	/// with <c>_</c> or <c>.</c> (templates, dotfiles) are skipped; unparsable
	/// files are reported as per-file errors without hiding the valid ones.
	/// </summary>
	Task<OneOf<CommunityRepoDirectory, Error<string>>> GetCommunityListingsAsync(
		PackageRemoteRecord remote, CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads a README.md from a remote: the repo root when
	/// <paramref name="path"/> is empty, otherwise the package directory; at a
	/// release tag when <paramref name="version"/> is given, otherwise the
	/// branch tip. Returns raw markdown — render with the portal pipeline.
	/// </summary>
	Task<OneOf<string, Error<string>>> GetReadmeAsync(
		PackageRemoteRecord remote, string path, string? version = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Builds a binary reader over a managed package's carried files (Phase 4):
	/// reads the bytes of files sitting alongside <c>package.yaml</c> in the
	/// package directory, from the exact <paramref name="commit"/> the manifest
	/// was fetched at (a moved tag therefore cannot smuggle different bytes than
	/// the SHA-256 the manifest signed off on). The installer asks it for each
	/// declared file name and verifies the hash before depositing.
	/// </summary>
	Task<OneOf<IManagedPackageBinarySource, Error<string>>> GetBinarySourceAsync(
		PackageRemoteRecord remote, string path, string commit,
		CancellationToken cancellationToken = default);
}
