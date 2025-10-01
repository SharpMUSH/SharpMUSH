using AutoBogus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using System.Reflection;

namespace SharpMUSH.Client.Services;

public class AdminConfigService
{
	private readonly ILogger<AdminConfigService> _logger;
	private PennMUSHOptions? _currentOptions;

	public AdminConfigService(ILogger<AdminConfigService> logger)
	{
		_logger = logger;
	}

	public PennMUSHOptions GetOptions()
	{
		return _currentOptions ?? new AutoFaker<PennMUSHOptions>().Generate();
	}

	public async Task<bool> ImportFromConfigFileAsync(string configContent, string fileName = "imported.cnf")
	{
		try
		{
			// Create a temporary file to write the content to
			var tempFilePath = Path.GetTempFileName();
			await File.WriteAllTextAsync(tempFilePath, configContent);

			// Create a null logger for ReadPennMushConfig
			var configLogger = NullLogger<ReadPennMushConfig>.Instance;

			// Use the existing ReadPennMushConfig to parse the file
			var configReader = new ReadPennMushConfig(configLogger, tempFilePath);
			var newOptions = configReader.Create(string.Empty);

			// Update current options
			_currentOptions = newOptions;

			// Clean up temp file
			File.Delete(tempFilePath);

			_logger.LogInformation("Successfully imported configuration from {FileName}", fileName);
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to import configuration from {FileName}", fileName);
			return false;
		}
	}

	public void ResetToDefault()
	{
		_currentOptions = null;
	}

	public class ConfigItem
	{
		public string Section { get; set; } = string.Empty;
		public string Key { get; set; } = string.Empty;
		public string Value { get; set; } = string.Empty;
		public string Type { get; set; } = string.Empty;
	}
}


public static class PennMUSHOptionsExtension
{
	public static IEnumerable<object> ToDatagrid(this PennMUSHOptions options)
	{
		return [];
	}

	public static IEnumerable<AdminConfigService.ConfigItem> ToConfigItems(this PennMUSHOptions options)
	{
		var configItems = new List<AdminConfigService.ConfigItem>();

		// Use reflection to get all properties and their values
		var optionsType = typeof(PennMUSHOptions);
		var properties = optionsType.GetProperties();

		foreach (var prop in properties)
		{
			var sectionName = prop.Name;
			var sectionValue = prop.GetValue(options);
			
			if (sectionValue != null)
			{
				var sectionType = prop.PropertyType;
				var sectionProperties = sectionType.GetProperties();

				foreach (var sectionProp in sectionProperties)
				{
					var value = sectionProp.GetValue(sectionValue);
					var valueString = value switch
					{
						null => "null",
						bool b => b.ToString().ToLower(),
						string s => s,
						_ => value.ToString() ?? "null"
					};

					configItems.Add(new AdminConfigService.ConfigItem
					{
						Section = sectionName,
						Key = sectionProp.Name,
						Value = valueString,
						Type = sectionProp.PropertyType.Name
					});
				}
			}
		}

		return configItems.OrderBy(x => x.Section).ThenBy(x => x.Key);
	}
}