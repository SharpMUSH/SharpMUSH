using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using FileOptions = SharpMUSH.Configuration.Options.FileOptions;

namespace SharpMUSH.Configuration;

public partial class ReadPennMushConfig(ILogger<ReadPennMushConfig> Logger, string configFile) : IOptionsFactory<PennMUSHOptions>
{
	public PennMUSHOptions Create(string _)
	{
		string[] text;
		var keys = typeof(PennMUSHOptions)
			.GetProperties()
			.Select(property => property.PropertyType)
			.SelectMany(configType => configType
				.GetProperties()
				.Select(configProperty => configProperty
					.GetCustomAttributes<PennConfigAttribute>()
					.Select(attribute => (configProperty, attribute))
					.FirstOrDefault()
				)).ToImmutableHashSet();

		var propertyDictionary = keys.ToDictionary(
			key => key.configProperty.Name,
			key => key.attribute.Name);
		var configDictionary = keys.ToDictionary(
			key => key.attribute.Name,
			_ => string.Empty);

		var splitter = KeyValueSplittingRegex();
		
		try
		{
			text = File.ReadAllLines(configFile);
		}
		catch (Exception ex) when (ex is FileNotFoundException or IOException)
		{
			Logger.LogCritical(ex, nameof(Create));
			// Return default configuration if file doesn't exist
			return ConfigurationMetadata.CreateDefaultOptions();
		}
		
		// Use a Regex to split the values.
		foreach (var configLine in text
			         .Where(line => configDictionary.Keys.Any(line.Trim().StartsWith))
			         .Select(line => splitter.Match(line.Trim()))
			         .Where(match => match.Success)
			         .Select(match => match.Groups))
		{
			configDictionary[configLine["Key"].Value] = configLine["Value"].Value;
		}

		// Create options using centralized defaults and override with config file values
		var defaultOptions = ConfigurationMetadata.CreateDefaultOptions();
		
		// Apply any values from the config file
		ApplyConfigFileValues(defaultOptions, configDictionary, propertyDictionary);
		
		return defaultOptions;
	}

	private void ApplyConfigFileValues(PennMUSHOptions options, Dictionary<string, string> configDictionary, Dictionary<string, string> propertyDictionary)
	{
		// Apply config file values to override the defaults from attributes
		foreach (var kvp in configDictionary.Where(x => !string.IsNullOrEmpty(x.Value)))
		{
			var configKey = kvp.Key;
			var configValue = kvp.Value;
			
			// Find the property name from the config key
			var propertyName = propertyDictionary.FirstOrDefault(x => x.Value == configKey).Key;
			if (!string.IsNullOrEmpty(propertyName))
			{
				// Use reflection to set the property value on the appropriate options section
				ApplyConfigValueToProperty(options, propertyName, configValue);
			}
		}
	}
	
	private void ApplyConfigValueToProperty(PennMUSHOptions options, string propertyName, string configValue)
	{
		// Find the property across all option sections
		var optionsSections = new object[]
		{
			options.Net, options.Chat, options.Database, options.Attribute,
			options.Command, options.Compatibility, options.Cosmetic, options.Cost,
			options.Debug, options.Dump, options.File, options.Flag,
			options.Function, options.Limit, options.Log, options.Message
		};
		
		foreach (var section in optionsSections)
		{
			var sectionType = section.GetType();
			var property = sectionType.GetProperty(propertyName);
			if (property != null && property.CanWrite)
			{
				// Convert and set the value based on property type
				var convertedValue = ConvertConfigValue(configValue, property.PropertyType);
				property.SetValue(section, convertedValue);
				break;
			}
		}
	}
	
	private object? ConvertConfigValue(string configValue, Type targetType)
	{
		if (string.IsNullOrWhiteSpace(configValue))
			return null;
			
		if (targetType == typeof(string))
			return configValue;
		if (targetType == typeof(bool))
			return Boolean(configValue, false);
		if (targetType == typeof(int))
			return Integer(configValue, 0);
		if (targetType == typeof(uint))
			return UnsignedInteger(configValue, 0);
		if (targetType == typeof(uint?))
			return string.IsNullOrWhiteSpace(configValue) ? null : UnsignedInteger(configValue, 0);
		if (targetType == typeof(char))
			return string.IsNullOrWhiteSpace(configValue) ? '\0' : configValue[0];
		if (targetType == typeof(string[]))
			return configValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			
		// Default fallback
		return configValue;
	}

	private static bool Boolean(string value, bool fallback) =>
		string.IsNullOrWhiteSpace(value)
			? fallback
			: value is not ("-1" or "0" or "false" or "no");

	private static string RequiredString(string value, string fallback) =>
		string.IsNullOrWhiteSpace(value)
			? fallback
			: value;

	private static string? String(string value, string? fallback) =>
		string.IsNullOrWhiteSpace(value)
			? fallback
			: value;

	private static uint UnsignedInteger(string value, uint fallback) =>
		string.IsNullOrWhiteSpace(value)
			? fallback
			: uint.TryParse(value, out var result)
				? result
				: fallback;


	private static int Integer(string value, int fallback) =>
		string.IsNullOrWhiteSpace(value)
			? fallback
			: int.TryParse(value, out var result)
				? result
				: fallback;

	private static uint? DatabaseReference(string value, uint? fallback) =>
		string.IsNullOrWhiteSpace(value)
			? fallback
			: uint.TryParse(value, out var result)
				? result
				: fallback;

	private static uint RequiredDatabaseReference(string value, uint fallback) =>
		UnsignedInteger(value, fallback);

	[GeneratedRegex(@"^(?<Key>[^\s]+)\s+(?<Value>.+)\s*$")]
	private static partial Regex KeyValueSplittingRegex();
}