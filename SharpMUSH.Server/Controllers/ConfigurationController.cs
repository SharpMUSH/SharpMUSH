using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigurationController : ControllerBase
{
	private readonly IOptionsMonitor<PennMUSHOptions> _options;
	private readonly ILogger<ConfigurationController> _logger;

	public ConfigurationController(IOptionsMonitor<PennMUSHOptions> options, ILogger<ConfigurationController> logger)
	{
		_options = options;
		_logger = logger;
	}

	[HttpGet]
	public ActionResult<PennMUSHOptions> GetConfiguration()
	{
		try
		{
			return Ok(_options.CurrentValue);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving configuration");
			return StatusCode(500, "Error retrieving configuration");
		}
	}

	[HttpGet("metadata")]
	public ActionResult<IEnumerable<ConfigurationPropertyInfo>> GetConfigurationMetadata()
	{
		try
		{
			var sections = ConfigurationMetadata.GetAllSections();
			var allMetadata = sections.SelectMany(ConfigurationMetadata.GetSectionProperties);
			return Ok(allMetadata);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving configuration metadata");
			return StatusCode(500, "Error retrieving configuration metadata");
		}
	}

	[HttpGet("sections")]
	public ActionResult<IEnumerable<string>> GetConfigurationSections()
	{
		try
		{
			return Ok(ConfigurationMetadata.GetAllSections());
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving configuration sections");
			return StatusCode(500, "Error retrieving configuration sections");
		}
	}

	[HttpPost("import")]
	public ActionResult<PennMUSHOptions> ImportConfiguration([FromBody] string configContent)
	{
		try
		{
			// Create a temporary file with the content
			var tempFile = Path.GetTempFileName();
			File.WriteAllText(tempFile, configContent);

			// Use ReadPennMushConfig to parse it
			var configReader = new ReadPennMushConfig(_logger as ILogger<ReadPennMushConfig> ?? 
				LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ReadPennMushConfig>(), tempFile);
			var importedOptions = configReader.Create(string.Empty);

			// Clean up temp file
			File.Delete(tempFile);

			return Ok(importedOptions);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error importing configuration");
			return BadRequest($"Error importing configuration: {ex.Message}");
		}
	}
}