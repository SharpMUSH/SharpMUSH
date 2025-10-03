using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
	public ActionResult<ConfigurationResponse> GetConfiguration()
	{
		try
		{
			var configuration = _options.CurrentValue;
			var metadata = GetConfigurationMetadata();
			
			return Ok(new ConfigurationResponse
			{
				Configuration = configuration,
				Metadata = metadata
			});
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving configuration");
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
			var configReader = new ReadPennMushConfig(_logger as ILogger<ReadPennMushConfig> ?? 
				LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ReadPennMushConfig>(), tempFile);
			var importedOptions = configReader.Create(string.Empty);
			var metadata = GetConfigurationMetadata();

			// Clean up temp file
			System.IO.File.Delete(tempFile);

			return Ok(new ConfigurationResponse
			{
				Configuration = importedOptions,
				Metadata = metadata
			});
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error importing configuration");
			return BadRequest($"Error importing configuration: {ex.Message}");
		}
	}

	private IEnumerable<ConfigurationPropertyInfo> GetConfigurationMetadata()
	{
		var sections = ConfigurationMetadata.GetAllSections();
		return sections.SelectMany(ConfigurationMetadata.GetSectionProperties);
	}
}

public class ConfigurationResponse
{
	public PennMUSHOptions Configuration { get; set; } = null!;
	public IEnumerable<ConfigurationPropertyInfo> Metadata { get; set; } = [];
}