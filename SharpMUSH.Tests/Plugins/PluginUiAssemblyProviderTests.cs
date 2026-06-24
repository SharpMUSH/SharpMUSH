using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// Proves the UI-assembly serving seam: <see cref="FileSystemPluginUiAssemblyProvider"/> returns a plugin's
/// compiled UI assembly bytes only when they match the install-time SHA-256 recorded in the
/// <c>plugin-ui.json</c> sidecar, and 404s (NotFound) on an unknown plugin/assembly, a missing sidecar, a
/// hash mismatch, or a path-traversal attempt. The controller layers the <c>allow_browser_code</c> gate on
/// top; this proves the verification/serving core.
/// </summary>
[NotInParallel]
public class PluginUiAssemblyProviderTests
{
	private const string PluginId = "ui-sample";
	private const string Assembly = "Sample.Ui.dll";

	private static string Sha256Of(byte[] bytes) =>
		Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

	private static string StagePlugin(byte[] assemblyBytes, bool writeSidecar = true, string? sidecarHash = null)
	{
		var root = Path.Combine(Path.GetTempPath(), $"ui-prov-{Guid.NewGuid():N}");
		var dir = Path.Combine(root, PluginId);
		Directory.CreateDirectory(dir);
		File.WriteAllBytes(Path.Combine(dir, Assembly), assemblyBytes);

		if (writeSidecar)
		{
			var manifest = new PluginUiBinaryManifest(
				new Dictionary<string, string> { [Assembly] = sidecarHash ?? Sha256Of(assemblyBytes) });
			File.WriteAllText(Path.Combine(dir, PluginUiBinaryManifest.FileName), manifest.ToJson());
		}

		return root;
	}

	private static FileSystemPluginUiAssemblyProvider NewProvider(string root) =>
		new(NullLogger<FileSystemPluginUiAssemblyProvider>.Instance, root);

	[Test]
	public async Task Get_VerifiedBytes_Returned()
	{
		var bytes = Encoding.UTF8.GetBytes("PRETEND-WASM-ASSEMBLY-BYTES");
		var provider = NewProvider(StagePlugin(bytes));

		var result = await provider.GetVerifiedAssemblyAsync(PluginId, Assembly);

		await Assert.That(result.IsT0).IsTrue().Because("matching bytes verify and are served");
		await Assert.That(result.AsT0).IsEquivalentTo(bytes);
	}

	[Test]
	public async Task Get_HashMismatch_NotFound()
	{
		var bytes = Encoding.UTF8.GetBytes("THE-REAL-BYTES");
		var provider = NewProvider(StagePlugin(bytes, sidecarHash: new string('a', 64)));

		var result = await provider.GetVerifiedAssemblyAsync(PluginId, Assembly);

		await Assert.That(result.IsT1).IsTrue().Because("a hash mismatch must 404, never serve");
	}

	[Test]
	public async Task Get_UnknownAssembly_NotFound()
	{
		var provider = NewProvider(StagePlugin(Encoding.UTF8.GetBytes("x")));

		var result = await provider.GetVerifiedAssemblyAsync(PluginId, "NotDeclared.dll");

		await Assert.That(result.IsT1).IsTrue().Because("an assembly not in the sidecar is unverified");
	}

	[Test]
	public async Task Get_UnknownPlugin_NotFound()
	{
		var provider = NewProvider(StagePlugin(Encoding.UTF8.GetBytes("x")));

		var result = await provider.GetVerifiedAssemblyAsync("no-such-plugin", Assembly);

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task Get_MissingSidecar_NotFound()
	{
		var provider = NewProvider(StagePlugin(Encoding.UTF8.GetBytes("x"), writeSidecar: false));

		var result = await provider.GetVerifiedAssemblyAsync(PluginId, Assembly);

		await Assert.That(result.IsT1).IsTrue().Because("no install-time hash sidecar means no trust anchor");
	}

	[Test]
	[Arguments("../escape", "x.dll")]
	[Arguments("ui-sample", "../../etc/passwd")]
	[Arguments("ui-sample", "sub/dir.dll")]
	public async Task Get_PathTraversal_Rejected(string pluginId, string assembly)
	{
		var provider = NewProvider(StagePlugin(Encoding.UTF8.GetBytes("x")));

		var result = await provider.GetVerifiedAssemblyAsync(pluginId, assembly);

		await Assert.That(result.IsT1).IsTrue().Because("non-flat ids/assemblies must be rejected up front");
	}
}
