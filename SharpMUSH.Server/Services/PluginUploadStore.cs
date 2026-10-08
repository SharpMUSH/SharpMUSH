using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Holds uploaded plugin packages between upload and apply. An upload is a <c>.zip</c> of a plugin package's folder:
/// <c>package.yaml</c> beside the files its <c>binaries:</c> block lists, all at the top level. It is unpacked into
/// its own staging folder under the installed plugins' directory, then planned and applied like a package from a
/// remote, as remote <see cref="RemoteName"/> with the upload's token as its path.
/// </summary>
public sealed partial class PluginUploadStore(
	PluginDirectories directories,
	IPackageManifestService manifests,
	ILogger<PluginUploadStore> logger)
{
	/// <summary>The remote name plan and apply read an upload from. No configured remote may take it.</summary>
	public const string RemoteName = "upload";

	/// <summary>What an installed package's source repo says when it was uploaded.</summary>
	public const string SourceRepo = "upload:portal";

	/// <summary>The largest upload accepted, compressed.</summary>
	public const long MaxUploadBytes = 20 * 1024 * 1024;

	/// <summary>The most an upload may unpack to, so a small archive cannot fill the disk.</summary>
	private const long MaxUnpackedBytes = 64 * 1024 * 1024;

	/// <summary>How long an upload waits to be applied before the next upload clears it away.</summary>
	private static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

	private const string ManifestFile = "package.yaml";

	[GeneratedRegex("^[0-9a-f]{32}$")]
	private static partial Regex TokenPattern();

	/// <summary>Whether <paramref name="remote"/> names the upload remote.</summary>
	public static bool IsUploadRemote(string? remote) => string.Equals(remote, RemoteName, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Unpacks <paramref name="archive"/> into a new staging folder and returns how to plan and apply it, or why it
	/// is not a plugin package. Only <c>package.yaml</c> and the files its <c>binaries:</c> block lists are kept.
	/// </summary>
	public async Task<Result<PluginUploadResponse>> StageAsync(Stream archive, CancellationToken cancellationToken)
	{
		ClearExpired();

		Result<Dictionary<string, byte[]>> read;
		try
		{
			read = await ReadArchiveAsync(archive, cancellationToken);
		}
		catch (InvalidDataException ex)
		{
			return new Error<string>($"The upload is not a zip archive: {ex.Message}");
		}

		return read switch
		{
			Dictionary<string, byte[]> files => await StageFilesAsync(files, cancellationToken),
			Error<string> error => error
		};
	}

	/// <summary>Checks the unpacked <paramref name="files"/> are a plugin package and writes them to a new staging folder.</summary>
	private async Task<Result<PluginUploadResponse>> StageFilesAsync(Dictionary<string, byte[]> files, CancellationToken cancellationToken)
	{
		if (!files.TryGetValue(ManifestFile, out var yamlBytes))
		{
			return new Error<string>($"The archive has no {ManifestFile} at its top level.");
		}

		var yaml = System.Text.Encoding.UTF8.GetString(yamlBytes);
		if (manifests.ParseManifest(yaml) is not ParsedPackageManifest { Manifest: var manifest })
		{
			return new Error<string>($"The archive's {ManifestFile} is not a valid package manifest; open it in Author to see why.");
		}

		if (manifest is not { Kind: PackageKind.Plugin, Binary: { } binary })
		{
			return new Error<string>($"{manifest.Name} is a {manifest.Kind.ToString().ToLowerInvariant()} package. Only plugin packages are uploaded; install others from a remote.");
		}

		var missing = binary.Files.Where(f => !files.ContainsKey(f.FileName)).Select(f => f.FileName).ToList();
		if (missing.Count > 0)
		{
			return new Error<string>($"The archive lacks {string.Join(", ", missing)}, which {ManifestFile} lists.");
		}

		var token = Guid.NewGuid().ToString("N");
		var folder = Path.Combine(directories.UploadStaging, token);
		Directory.CreateDirectory(folder);
		await File.WriteAllBytesAsync(Path.Combine(folder, ManifestFile), yamlBytes, cancellationToken);
		foreach (var file in binary.Files)
		{
			await File.WriteAllBytesAsync(Path.Combine(folder, file.FileName), files[file.FileName], cancellationToken);
		}

		logger.LogInformation("Staged uploaded plugin package {PackageId} v{Version} as {Token}.", manifest.Name, manifest.Version, token);
		return new PluginUploadResponse(RemoteName, token, manifest.Name, manifest.Version.ToString());
	}

	/// <summary>The staged upload <paramref name="token"/>'s manifest, or null when there is no such upload.</summary>
	public string? ManifestYaml(string token) =>
		Folder(token) is { } folder && File.Exists(Path.Combine(folder, ManifestFile))
			? File.ReadAllText(Path.Combine(folder, ManifestFile))
			: null;

	/// <summary>A reader over the staged upload <paramref name="token"/>'s files, or null when there is no such upload.</summary>
	public IPluginPackageBinarySource? Binaries(string token) =>
		Folder(token) is { } folder ? new FolderBinarySource(folder) : null;

	/// <summary>Removes the staged upload <paramref name="token"/>, once it is installed.</summary>
	public void Discard(string token)
	{
		if (Folder(token) is { } folder)
		{
			TryDelete(folder);
		}
	}

	private string? Folder(string token)
	{
		if (!TokenPattern().IsMatch(token ?? string.Empty))
		{
			return null;
		}

		var folder = Path.Combine(directories.UploadStaging, token!);
		return Directory.Exists(folder) ? folder : null;
	}

	/// <summary>
	/// The archive's top-level files by name. A nested path, a link, or more than <see cref="MaxUnpackedBytes"/> in
	/// all refuses the whole archive.
	/// </summary>
	private static async Task<Result<Dictionary<string, byte[]>>> ReadArchiveAsync(Stream archive, CancellationToken cancellationToken)
	{
		var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		using var zip = new ZipArchive(archive, ZipArchiveMode.Read);
		long total = 0;
		foreach (var entry in zip.Entries)
		{
			if (entry.FullName.EndsWith('/'))
			{
				return new Error<string>("The archive has folders. Zip the package's files themselves, with package.yaml at the top.");
			}

			if (entry.FullName.Contains('/') || entry.FullName.Contains('\\') || entry.FullName is "." or ".."
				|| entry.FullName.StartsWith('.'))
			{
				return new Error<string>($"'{entry.FullName}' is not a plain file name. Every file sits at the top of the archive.");
			}

			// Unix mode bits live in the top 16 bits; S_IFLNK is 0xA000.
			if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
			{
				return new Error<string>($"'{entry.FullName}' is a link. The archive carries files only.");
			}

			total += entry.Length;
			if (total > MaxUnpackedBytes)
			{
				return new Error<string>($"The archive unpacks to more than {MaxUnpackedBytes / (1024 * 1024)} MB.");
			}

			await using var content = await entry.OpenAsync(cancellationToken);
			using var buffer = new MemoryStream();
			await content.CopyToAsync(buffer, cancellationToken);
			if (buffer.Length != entry.Length)
			{
				return new Error<string>($"'{entry.FullName}' does not unpack to the size the archive gives it.");
			}

			files[entry.FullName] = buffer.ToArray();
		}

		return files;
	}

	/// <summary>Removes uploads older than <see cref="Lifetime"/> that were never applied.</summary>
	private void ClearExpired()
	{
		if (!Directory.Exists(directories.UploadStaging))
		{
			return;
		}

		foreach (var folder in Directory.EnumerateDirectories(directories.UploadStaging))
		{
			if (DateTime.UtcNow - Directory.GetCreationTimeUtc(folder) > Lifetime)
			{
				TryDelete(folder);
			}
		}
	}

	private void TryDelete(string folder)
	{
		try
		{
			Directory.Delete(folder, recursive: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(ex, "Could not remove the staged upload {Folder}.", folder);
		}
	}

	private sealed class FolderBinarySource(string folder) : IPluginPackageBinarySource
	{
		public async Task<byte[]?> ReadBinaryAsync(string fileName, CancellationToken cancellationToken = default)
		{
			// File names come from a manifest that was parsed as flat, but the folder is ours: check anyway.
			if (fileName.Contains('/') || fileName.Contains('\\') || fileName.StartsWith('.'))
			{
				return null;
			}

			var path = Path.Combine(folder, fileName);
			return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
		}
	}
}
