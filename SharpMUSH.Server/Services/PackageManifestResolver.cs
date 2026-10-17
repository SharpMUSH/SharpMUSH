using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>A source failure with the ordinary package endpoint's HTTP status and manifest issues.</summary>
public sealed record PackageManifestResolutionFailure(int StatusCode, string Message, IReadOnlyList<string>? Issues = null);
public union PackageManifestResolution(ResolvedPackageManifest, PackageManifestResolutionFailure);

/// <summary>The exact manifest and files reviewed, together with their configured trust and content identity.</summary>
public sealed record ResolvedPackageManifest(PackageManifest Manifest, IReadOnlyList<string> Warnings,
	PackageManifestSource Source, PackageRemoteRecord Remote, string ApplyPath, IPackageFileSource? Files,
	string? Readme, string Fingerprint);

/// <summary>One source resolution path for ordinary and reviewed batch package operations.</summary>
public sealed class PackageManifestResolver(IPackageRegistryService registry, IPackageSourceService source,
	IPackageManifestService manifests, PluginUploadStore uploads)
{
	private const long MaxBinaryBytes = 64 * 1024 * 1024;

	/// <summary>Batch reviews refresh remote refs; ordinary operations may use their existing fetched snapshot.</summary>
	public async Task<PackageManifestResolution> ResolveAsync(PackageBatchSource requested, bool refresh, CancellationToken cancellationToken)
	{
		var catalogue = BundledPackages.IsCatalogueRemote(requested.Remote);
		var upload = PluginUploadStore.IsUploadRemote(requested.Remote);
		PackageRemoteRecord remote;
		PackageManifestSource fetched;
		if (catalogue)
		{
			var id = requested.Path.Trim().Trim('/');
			if (!BundledPackages.Contains(id)) return new PackageManifestResolutionFailure(404, $"'{id}' is not shipped with this server.");
			remote = new(BundledPackages.RemoteName, BundledPackages.SourceRepo, PackageRemoteTrust.Official, null);
			fetched = new(BundledPackages.ManifestYaml(id), BundledPackages.SourceCommit, null);
		}
		else if (upload)
		{
			if (uploads.ManifestYaml(requested.Path) is not { } yaml) return MissingUpload();
			remote = new(PluginUploadStore.RemoteName, PluginUploadStore.SourceRepo, PackageRemoteTrust.Unknown, null);
			fetched = new(yaml, requested.Path, null);
		}
		else
		{
			if (await registry.GetPackageRemoteAsync(requested.Remote) is not PackageRemoteRecord configured)
				return new PackageManifestResolutionFailure(404, $"No configured remote named '{requested.Remote}'.");
			remote = configured;
			if (refresh && await source.RefreshAsync(remote, cancellationToken) is Error<string> refreshError)
				return new PackageManifestResolutionFailure(404, refreshError.Value);
			switch (await source.GetManifestAsync(remote, requested.Path, requested.Version, cancellationToken))
			{
				case PackageManifestSource found: fetched = found; break;
				case Error<string> error: return new PackageManifestResolutionFailure(404, error.Value);
				default: throw new InvalidOperationException("Unknown package source result.");
			}
		}

		var parseResult = manifests.ParseManifest(fetched.ManifestYaml);
		if (parseResult is PackageManifestFailure failure)
			return new PackageManifestResolutionFailure(422, "The manifest is invalid.", failure.Issues.Select(i => i.ToString()).ToList());
		if (parseResult is not ParsedPackageManifest parsed) throw new InvalidOperationException("Unknown manifest parse result.");
		var manifest = parsed.Manifest;
		if (catalogue && !string.IsNullOrWhiteSpace(requested.Version)
			&& !string.Equals(requested.Version, manifest.Version.ToString(), StringComparison.OrdinalIgnoreCase))
			return new PackageManifestResolutionFailure(404, $"This server ships {manifest.Name} v{manifest.Version}; the catalogue has no other versions.");
		var binary = manifest.Kind is PackageKind.Plugin or PackageKind.Ui;
		if (catalogue && binary) return new PackageManifestResolutionFailure(400, "A plugin package cannot be installed from the bundled catalogue.");

		IPackageFileSource? files = null;
		if (upload)
		{
			files = uploads.Files(requested.Path);
			if (files is null) return MissingUpload();
		}
		else if (!catalogue)
		{
			switch (await source.GetFileSourceAsync(remote, requested.Path, fetched.Commit, cancellationToken))
			{
				case IPackageFileSource found: files = found; break;
				case Error<string> error when binary || PackageWikiBodies.NeedsFiles(manifest):
					return new PackageManifestResolutionFailure(400, error.Value);
			}
		}
		var warnings = parsed.Warnings.Select(w => w.ToString()).ToList();
		string? readme = null;
		if (catalogue && BundledPackages.Find(manifest.Name) is BundledPackages.Descriptor descriptor)
			readme = BundledPackages.Readme(descriptor, manifest);
		else if (await PackageReadme.ReadAsync(files, cancellationToken) is string text) readme = text;

		if (PackageWikiBodies.NeedsFiles(manifest))
		{
			switch (await PackageWikiBodies.LoadAsync(manifest, files, cancellationToken))
			{
				case PackageWikiLoad loaded:
					manifest = loaded.Manifest;
					warnings.AddRange(loaded.Warnings.Select(w => w.ToString()));
					break;
				case Error<string> error:
					return new PackageManifestResolutionFailure(422, "The manifest is invalid.", [error.Value]);
			}
		}

		// Binary files must remain exactly what was reviewed even when an upload's staging files change.
		// No arbitrary file paths are retained; only the manifest's declared binary payload is needed by apply.
		var binaries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		if (binary && manifest.Binary is { } payload)
		{
			long size = 0;
			foreach (var file in payload.Files)
			{
				if (files is null || await files.ReadFileAsync(file.FileName, cancellationToken) is not { } bytes)
					return new PackageManifestResolutionFailure(400, $"{file.FileName} is not in the package.");
				size += bytes.LongLength;
				if (size > MaxBinaryBytes) return new PackageManifestResolutionFailure(400, "The package's binary files exceed 64 MiB.");
				binaries.Add(file.FileName, bytes.ToArray());
			}
			files = new BinarySnapshot(binaries);
		}
		var applyPath = catalogue ? manifest.Name : requested.Path;
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		Add(JsonSerializer.Serialize(new { Remote = remote, Path = applyPath, fetched.Commit, fetched.ManifestYaml, Manifest = manifest, Readme = readme }));
		foreach (var pair in binaries.OrderBy(p => p.Key, StringComparer.Ordinal))
		{
			Add(pair.Key);
			hash.AppendData(BitConverter.GetBytes(pair.Value.LongLength));
			hash.AppendData(pair.Value);
		}
		return new ResolvedPackageManifest(manifest, warnings, fetched, remote, applyPath, files, readme,
			Convert.ToHexStringLower(hash.GetHashAndReset()));

		void Add(string value)
		{
			var bytes = Encoding.UTF8.GetBytes(value);
			hash.AppendData(BitConverter.GetBytes(bytes.LongLength));
			hash.AppendData(bytes);
		}
	}

	private static PackageManifestResolutionFailure MissingUpload() => new(404, "That upload is no longer waiting; upload the package again.");

	private sealed class BinarySnapshot(IReadOnlyDictionary<string, byte[]> files) : IPackageFileSource
	{
		public Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(files.TryGetValue(path, out var bytes) ? bytes.ToArray() : null);
		}
	}
}
