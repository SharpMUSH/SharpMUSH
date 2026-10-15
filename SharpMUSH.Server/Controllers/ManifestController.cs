using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The web app manifest: what lets a player install the portal as an app, under the game's own name. Built
/// here rather than shipped as a file because the name is the game's <c>mud_name</c>. On iPhone and iPad, web
/// push works only for a portal added to the home screen, which reads this.
/// </summary>
[ApiController]
public class ManifestController(IOptionsWrapper<SharpMUSHOptions> options) : ControllerBase
{
	/// <summary>The default theme's page colour (<c>--bg</c> in <c>css/tokens.css</c>), behind the app while it starts.</summary>
	public const string BackgroundColour = "#0e0f11";

	/// <summary>The longest name an installed app is shown under on a home screen before it is cut.</summary>
	public const int ShortNameLength = 12;

	[HttpGet("/manifest.webmanifest")]
	public IActionResult Get()
	{
		var name = options.CurrentValue.Net.MudName is { Length: > 0 } mudName ? mudName.Trim() : "SharpMUSH";
		var manifest = new Dictionary<string, object>
		{
			["id"] = "/",
			["name"] = name,
			["short_name"] = name.Length <= ShortNameLength ? name : name[..ShortNameLength].TrimEnd(),
			["start_url"] = "/",
			["scope"] = "/",
			["display"] = "standalone",
			["background_color"] = BackgroundColour,
			["theme_color"] = BackgroundColour,
			["icons"] = new[]
			{
				new Dictionary<string, string> { ["src"] = "/icon-192.png", ["sizes"] = "192x192", ["type"] = "image/png", ["purpose"] = "any" },
				new Dictionary<string, string> { ["src"] = "/assets/logo.png", ["sizes"] = "512x512", ["type"] = "image/png", ["purpose"] = "any" },
				new Dictionary<string, string> { ["src"] = "/icon-maskable-512.png", ["sizes"] = "512x512", ["type"] = "image/png", ["purpose"] = "maskable" }
			}
		};

		Response.Headers.CacheControl = "no-cache";
		return Content(JsonSerializer.Serialize(manifest), "application/manifest+json");
	}
}
