using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Serves a managed plugin's compiled UI assembly bytes to the WASM client so it can
/// <c>Assembly.Load(bytes)</c> a plugin-shipped Blazor component (Phase: compiled components).
///
/// <para>Two defenses: (1) the <c>allow_browser_code</c> gate — when off, the endpoint 404s as if it did not
/// exist, matching the registry overlay that also omits Component-kind apps; (2) the bytes are re-verified
/// against the Phase-4 install-time SHA-256 sidecar before serving, so an unknown/unlisted assembly or a
/// tampered file 404s rather than reaching the browser.</para>
///
/// Route: <c>GET /api/plugins/{pluginId}/ui/{assembly}</c>
/// </summary>
[ApiController]
[Route("api/plugins")]
public sealed class PluginsUiController(
	IPluginUiAssemblyProvider provider,
	IOptionsWrapper<SharpMUSHOptions> options) : ControllerBase
{
	[HttpGet("{pluginId}/ui/{assembly}")]
	[AllowAnonymous]
	public async Task<IActionResult> GetUiAssembly(string pluginId, string assembly, CancellationToken cancellationToken)
	{
		// Gate: when browser code is not allowed, the UI-assembly surface does not exist (404, not 403, so it
		// leaks nothing about what a plugin ships).
		if (!options.CurrentValue.Database.AllowBrowserCode)
		{
			return NotFound();
		}

		var result = await provider.GetVerifiedAssemblyAsync(pluginId, assembly, cancellationToken);
		return result.Match<IActionResult>(
			bytes => File(bytes, "application/wasm"),
			_ => NotFound());
	}
}
