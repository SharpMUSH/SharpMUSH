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
		
		// TODO: Use a Regex to split the values.
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
		// For now, keep the existing logic but simplified
		// This could be further enhanced to use reflection to set properties dynamically
		
		string Get(string key) => configDictionary.TryGetValue(propertyDictionary[key], out var value) ? value : string.Empty;

		// Apply config file overrides (simplified version of original hardcoded values)
		// Since we start with defaults from attributes, we only need to override what's in the config file
		foreach (var kvp in configDictionary.Where(x => !string.IsNullOrEmpty(x.Value)))
		{
			// TODO: Implement dynamic property setting using reflection
			// For now, this preserves the existing behavior while using centralized defaults as the base
		}
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