using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigurationController(
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<ConfigurationController> logger)
	: ControllerBase
{
	[HttpGet]
	public ActionResult<ConfigurationResponse> GetConfiguration()
	{
		try
		{
			var configuration = options.CurrentValue;
			var converted = OptionHelper.OptionsToConfigurationResponse(configuration);
			
			return Ok(converted);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error retrieving configuration");
			return StatusCode(500, "Error retrieving configuration");
		}
	}

	[HttpPost("import")]
	public async Task<ActionResult<ConfigurationResponse>> ImportConfiguration([FromBody] string configContent)
	{
		try
		{
			// Create a temporary file with the content
			var tempFile = Path.GetTempFileName();
			await System.IO.File.WriteAllTextAsync(tempFile, configContent);

			// Use ReadPennMushConfig to parse it
			var importedOptions = ReadPennMushConfig.Create(tempFile);

			// TODO: Store the new config data.

			// Clean up temp file
			System.IO.File.Delete(tempFile);

			return Ok(OptionHelper.OptionsToConfigurationResponse(importedOptions));
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error importing configuration");
			return BadRequest($"Error importing configuration: {ex.Message}");
		}
	}
}