using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Server.Controllers;

public record ConfigurationResponse(SharpMUSHOptions Configuration);

public class ImportRequest
{
	public string Content { get; set; } = string.Empty;
}

[ApiController]
[Route("api/[controller]")]
public class ConfigurationController(IOptionsMonitor<SharpMUSHOptions> options, ILogger<ConfigurationController> logger)
	: ControllerBase
{
	[HttpGet]
	public ActionResult<ConfigurationResponse> GetConfiguration()
	{
		try
		{
			var configuration = options.CurrentValue;

			return Ok(new ConfigurationResponse(configuration));
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

			// Clean up temp file
			System.IO.File.Delete(tempFile);

			return Ok(new ConfigurationResponse(importedOptions));
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error importing configuration");
			return BadRequest($"Error importing configuration: {ex.Message}");
		}
	}
}