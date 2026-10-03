using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;

namespace SharpMUSH.Server;

/// <summary>
/// Names the portal build this server hands out, so a browser tab still running an earlier one can tell
/// that the game was deployed under it (<c>api/server-info</c>'s <c>BuildId</c>).
/// </summary>
/// <remarks>
/// A published image is identified by its portal endpoints manifest (<see cref="PortalStaticFiles"/>): it
/// carries every asset's content hash, so its own hash changes with any deploy that changed what the browser
/// runs and stays put across a restart of the same image. The server assembly's MVID would not do: builds are
/// deterministic, so a deploy that changed only the portal leaves it as it was. Without a manifest (a
/// development run, a test host) the MVID is all there is, and it does change when the server is rebuilt.
/// </remarks>
/// <param name="Id">An opaque id, the same for every request this process answers.</param>
public sealed record PortalBuild(string Id)
{
	public static PortalBuild For(IHostEnvironment environment)
		=> new(PortalStaticFiles.FindManifest(environment) is { } manifest
			? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(manifest)))[..16]
			: typeof(PortalBuild).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
}
