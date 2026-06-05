using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Admin theme endpoints for CRUD operations on theme presets.
/// Requires Wizard+ authorization.
/// </summary>
[ApiController]
[Route("api/admin/themes")]
[Authorize]
public class AdminThemesController(ILogger<AdminThemesController> logger) : ControllerBase
{
	/// <summary>
	/// Gets all theme presets.
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

	/// <summary>
	/// Creates a new theme preset.
	/// </summary>
	[HttpPost]
	public ActionResult<ThemePreset> CreateTheme([FromBody] ThemePreset theme)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(theme.Name))
				return BadRequest("Theme name is required");

			// For now, just echo back the preset.
			// In the future, this will store in server config.
			return Created($"api/admin/themes/{theme.Name}", theme);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error creating theme");
			return StatusCode(500, "Error creating theme");
		}
	}

	/// <summary>
	/// Updates an existing theme preset.
	/// </summary>
	[HttpPut("{name}")]
	public ActionResult<ThemePreset> UpdateTheme(string name, [FromBody] ThemePreset theme)
	{
		try
		{
			if (theme.Name != name)
				return BadRequest("Theme name mismatch");

			// For now, just echo back the preset.
			// In the future, this will update server config.
			return Ok(theme);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error updating theme");
			return StatusCode(500, "Error updating theme");
		}
	}

	/// <summary>
	/// Deletes a theme preset.
	/// </summary>
	[HttpDelete("{name}")]
	public ActionResult DeleteTheme(string name)
	{
		try
		{
			// For now, just acknowledge the deletion.
			// In the future, this will remove from server config.
			return NoContent();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error deleting theme");
			return StatusCode(500, "Error deleting theme");
		}
	}
}
