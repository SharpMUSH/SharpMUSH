using System.Security.Cryptography;
using System.Text.Json;
using SharpMUSH.Library.Plugins;

namespace SharpMUSH.Library.Applications;

/// <summary>Read-only installed UI definitions and explicitly declared, hash-verified public browser assets.</summary>
public sealed class UiPackageStore(PluginDirectories directories)
{
	public async Task<byte[]?> ReadAsync(string id, string file, CancellationToken ct = default)
	{
		if (!UiDefinition.IsName(id) || !UiDefinition.IsName(file) || UiDefinition.ContentType(file) is null) return null;
		var folder = Path.Combine(directories.UiPackages, id);
		try
		{
			var sidecar = PluginUiBinaryManifest.FromJson(await File.ReadAllTextAsync(Path.Combine(folder, PluginUiBinaryManifest.FileName), ct));
			if (sidecar?.HashFor(file) is not { } hash) return null;
			var path = Path.Combine(folder, file);
			if (new FileInfo(path).Length > 2 * 1024 * 1024) return null;
			var bytes = await File.ReadAllBytesAsync(path, ct);
			return string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), hash, StringComparison.OrdinalIgnoreCase) ? bytes : null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
	}

	public async Task<UiDefinition?> GetAsync(string id, CancellationToken ct = default)
	{
		if (await ReadAsync(id, UiDefinition.FileName, ct) is not { } bytes) return null;
		try
		{
			var definition = UiDefinition.Parse(bytes);
			return definition.Id == id ? definition : null;
		}
		catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException) { return null; }
	}
}
