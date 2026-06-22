using System.Text.Json;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Plugins;

/// <summary>
/// A small sidecar written next to a managed package's deployed binaries (<c>plugins/{id}/</c>) recording
/// the installer-verified SHA-256 of each carried file. The runtime UI-assembly serving endpoint reads it to
/// re-verify a compiled component's bytes against the Phase-4 install-time hash before handing them to the
/// browser, extending the managed-package trust chain to serve time.
/// </summary>
/// <param name="Files">Map of file name → lowercase-hex SHA-256, copied verbatim from the package manifest.</param>
public sealed record PluginUiBinaryManifest(IReadOnlyDictionary<string, string> Files)
{
	/// <summary>The sidecar file name deposited into each managed package's plugin directory.</summary>
	public const string FileName = "plugin-ui.json";

	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	/// <summary>Build the sidecar from the manifest's verified binary file list.</summary>
	public static PluginUiBinaryManifest FromManifestFiles(IEnumerable<PackageBinaryFile> files) =>
		new(files.ToDictionary(f => f.FileName, f => f.Sha256.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase));

	/// <summary>Serialize to indented JSON for the sidecar file.</summary>
	public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

	/// <summary>Parse a sidecar's JSON, or null when the content is malformed.</summary>
	public static PluginUiBinaryManifest? FromJson(string json)
	{
		try
		{
			return JsonSerializer.Deserialize<PluginUiBinaryManifest>(json, JsonOptions);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>The expected lowercase-hex SHA-256 for <paramref name="fileName"/>, or null when absent.</summary>
	public string? HashFor(string fileName) =>
		Files.TryGetValue(fileName, out var hash) ? hash : null;
}
