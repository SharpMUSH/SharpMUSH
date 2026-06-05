using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Public theme endpoints for the web portal.
/// </summary>
[ApiController]
[Route("api/portal/themes")]
public class PortalThemesController(ILogger<PortalThemesController> logger) : ControllerBase
{
	/// <summary>
	/// Gets the list of available theme presets.
	/// </summary>
	[HttpGet]
	public ActionResult<List<ThemePreset>> GetThemes()
	{
		try
		{
			// For now, return the default theme set.
			// In the future, this will load from server config storage.
			var themes = new List<ThemePreset>
			{
				new ThemePreset(
					Name: "Default Dark",
					IsDark: true,
					IsDefault: true,
					Primary: "#00f5b7",
					Secondary: "#00f5b7",
					Tertiary: "#00f5b7",
					Info: "#0071ff",
					Success: "#4caf50",
					Warning: "#ff9800",
					Error: "#f44336",
					Dark: "#424242",
					TextPrimary: "rgba(255, 255, 255, 0.87)",
					TextSecondary: "rgba(255, 255, 255, 0.60)",
					TextDisabled: "rgba(255, 255, 255, 0.38)",
					Surface: "#242424",
					Background: "#1a1a1a",
					AppbarBackground: "#1a1a1a",
					DrawerBackground: "#242424",
					ActionDefault: "rgba(255, 255, 255, 0.54)",
					ActionDisabled: "rgba(255, 255, 255, 0.26)",
					Dividers: "rgba(255, 255, 255, 0.12)",
					OverlayDark: "rgba(0, 0, 0, 0.50)"
				)
			};

			return Ok(themes);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error retrieving themes");
			return StatusCode(500, "Error retrieving themes");
		}
	}
}
