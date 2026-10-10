using System.Security.Cryptography;
using System.Text;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// A plugin package built over the CommandOnlyPlugin fixture DLL: the DLL plus a <c>plugin.json</c> whose id is the
/// package's, as the installer requires.
/// </summary>
public static class PluginPackageFixture
{
	public const string DllName = "CommandOnlyPlugin.dll";

	public static string DllPath =>
		Path.Combine(AppContext.BaseDirectory, "plugins-unit", "command-only", DllName);

	public static string Sha256Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

	public static string DllSha => Sha256Of(File.ReadAllBytes(DllPath));

	/// <summary>The <c>plugin.json</c> a package <paramref name="id"/> carries.</summary>
	public static byte[] PluginJson(string id) =>
		Encoding.UTF8.GetBytes($$"""{ "id": "{{id}}", "version": "1.0.0", "entry": "{{DllName}}" }""");

	/// <summary>
	/// A plugin package manifest for <paramref name="id"/>, carrying the DLL (under <paramref name="dllSha"/>, the real
	/// hash by default) and its <c>plugin.json</c>, with <paramref name="relations"/> lines spliced in above the
	/// binaries.
	/// </summary>
	public static string Yaml(string id, string version = "1.0.0", string relations = "", string minServerVersion = ">=2.0",
		string? dllSha = null) => $"""
		package: {id}
		version: "{version}"
		kind: plugin
		{relations}
		binaries:
		  min_server_version: "{minServerVersion}"
		  files:
		    - file: {DllName}
		      sha256: {dllSha ?? DllSha}
		    - file: {PluginManifest.FileName}
		      sha256: {Sha256Of(PluginJson(id))}
		""";

	/// <summary>Reads the fixture DLL and package <paramref name="id"/>'s <c>plugin.json</c>.</summary>
	public sealed class BinarySource(string id) : IPluginPackageBinarySource
	{
		public async Task<byte[]?> ReadBinaryAsync(string fileName, CancellationToken cancellationToken = default) => fileName switch
		{
			DllName => await File.ReadAllBytesAsync(DllPath, cancellationToken),
			PluginManifest.FileName => PluginJson(id),
			_ => null
		};
	}

	/// <summary>Scratch plugin directories: an empty shipped folder and an installed one, both under the temp folder.</summary>
	public static PluginDirectories ScratchDirectories()
	{
		var root = Path.Combine(Path.GetTempPath(), $"plugin-fixture-{Guid.NewGuid():N}");
		return new PluginDirectories(Path.Combine(root, "built-in"), Path.Combine(root, "installed"));
	}

	/// <summary>Removes what <see cref="ScratchDirectories"/> made.</summary>
	public static void Delete(PluginDirectories directories)
	{
		var root = Path.GetDirectoryName(directories.Installed)!;
		if (Directory.Exists(root))
		{
			Directory.Delete(root, recursive: true);
		}
	}

	public static PluginInstallOptions Allowed { get; } = new(true);
}
