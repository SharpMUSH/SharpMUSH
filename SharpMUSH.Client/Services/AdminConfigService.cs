using OneOf.Types;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using System.Collections;
using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

public class AdminConfigService(ILogger<AdminConfigService> logger, IHttpClientFactory httpClient)
{
	private SharpMUSHOptions? _currentOptions = null;
	private Dictionary<string, SharpConfigAttribute> _metadata = [];

	public async Task<OneOf.OneOf<IEnumerable<ConfigItem>, Error<string>>> GetOptionsAsync()
	{
		try
		{
			var configResponse = await FetchConfigurationFromServer();
			_currentOptions = configResponse.Configuration;
			_metadata = configResponse.Metadata;

			return configResponse.ToConfigItems();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error fetching options from server, using defaults");
			return OneOf.OneOf<IEnumerable<ConfigItem>, Error<string>>.FromT0([]);
		}
	}

	public async Task<SharpMUSHOptions> ImportFromConfigFileAsync(string configFileContent)
	{
		try
		{
			var response = await httpClient.CreateClient("api").PostAsJsonAsync("/api/configuration/import", configFileContent);
			response.EnsureSuccessStatusCode();

			var configResponse = await response.Content.ReadFromJsonAsync<ConfigurationResponse>();
			if (configResponse?.Configuration != null)
			{
				_currentOptions = configResponse.Configuration;
			}
			return configResponse?.Configuration!;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error importing configuration file");
			throw;
		}
	}

	public async Task<OneOf.OneOf<ConfigurationResponse, Error<string>>> UpdateConfigAsync(
		Dictionary<string, object?> changes)
	{
		try
		{
			var client = httpClient.CreateClient("api");
			var response = await client.PatchAsJsonAsync("/api/configuration", changes);

			if (!response.IsSuccessStatusCode)
			{
				var errorContent = await response.Content.ReadAsStringAsync();
				logger.LogError("Config update failed: {StatusCode} {Error}", response.StatusCode, errorContent);
				return new Error<string>(errorContent);
			}

			var configResponse = await response.Content.ReadFromJsonAsync<ConfigurationResponse>();
			if (configResponse?.Configuration != null)
			{
				_currentOptions = configResponse.Configuration;
			}
			return configResponse!;
		}
		catch (HttpRequestException ex)
		{
			logger.LogError(ex, "Error updating configuration");
			return new Error<string>(ex.Message);
		}
		catch (TaskCanceledException ex)
		{
			logger.LogError(ex, "Error updating configuration");
			return new Error<string>(ex.Message);
		}
		catch (System.Text.Json.JsonException ex)
		{
			logger.LogError(ex, "Error updating configuration");
			return new Error<string>(ex.Message);
		}
	}

	public async Task<string?> ExportConfigAsync()
	{
		try
		{
			var client = httpClient.CreateClient("api");
			return await client.GetStringAsync("/api/configuration/export");
		}
		catch (HttpRequestException ex)
		{
			logger.LogError(ex, "Error exporting configuration");
			return null;
		}
		catch (TaskCanceledException ex)
		{
			logger.LogError(ex, "Error exporting configuration");
			return null;
		}
	}

	public void ResetToDefault()
	{
		_currentOptions = null;
	}

	public async Task<ConfigurationResponse> FetchConfigurationFromServer()
	{
		try
		{
			var response = await httpClient.CreateClient("api").GetAsync("/api/configuration");
			response.EnsureSuccessStatusCode();

			var configResponse = await response.Content.ReadFromJsonAsync<ConfigurationResponse>();
			return configResponse!;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error fetching configuration from server");
			return new ConfigurationResponse
			{
				Configuration = null!
			};
		}
	}

	public class ConfigItem
	{
		public string Section { get; set; } = string.Empty;
		public string Key { get; set; } = string.Empty;
		public string Value { get; set; } = string.Empty;
		public string Type { get; set; } = string.Empty;
		public object? RawValue { get; set; }
		public string Description { get; set; } = string.Empty;
		public string Category { get; set; } = string.Empty;
		public bool IsAdvanced { get; set; }

		public bool IsBoolean => Type == "Boolean";
		public bool IsNumber => Type is "Int32" or "UInt32" or "Double" or "Single" or "Decimal";
		public bool IsArray => Type.EndsWith("[]");
		public bool IsNullable => Type.StartsWith("Nullable");
		public bool IsDictionary => Type.Contains("Dictionary");
	}
}

public static class SharpMUSHOptionsExtension
{
	/// <summary>
	/// Flattens a configuration response into one row per configured property.
	/// </summary>
	/// <remarks>
	/// The property list, each property's category, and its declared type all come from
	/// <c>ConfigAccessor</c>/<c>ConfigMetadata</c>, which the config generators emit from the
	/// <see cref="SharpConfigAttribute"/>-annotated members of <see cref="SharpMUSHOptions"/>. That is the
	/// same table the server sends as <see cref="ConfigurationResponse.Metadata"/> (see
	/// <c>OptionHelper.OptionsToConfigurationResponse</c>), so the two agree by construction and a property
	/// added to an options record shows up here without any further wiring.
	/// </remarks>
	public static OneOf.OneOf<IEnumerable<AdminConfigService.ConfigItem>, Error<string>> ToConfigItems(this ConfigurationResponse options)
	{
		if (options.Configuration is null)
		{
			return OneOf.OneOf<IEnumerable<AdminConfigService.ConfigItem>, Error<string>>.FromT1(
				new Error<string>("The configuration response carried no configuration."));
		}

		var configItems = ConfigMetadata.PropertyMetadata
			.Select(entry => ToConfigItem(options, entry.Key, entry.Value))
			.OrderBy(x => x.Section)
			.ThenBy(x => x.Key)
			.ToList();

		return OneOf.OneOf<IEnumerable<AdminConfigService.ConfigItem>, Error<string>>.FromT0(configItems);
	}

	private static AdminConfigService.ConfigItem ToConfigItem(
		ConfigurationResponse options,
		string propertyName,
		SharpConfigAttribute generated)
	{
		// The server's metadata wins when it sent any, so a description it overrides survives the trip;
		// the locally generated attribute is the fallback for a response that omitted the table.
		var metadata = options.Metadata.GetValueOrDefault(propertyName) ?? generated;
		var value = ConfigAccessor.GetValue(options.Configuration, propertyName);
		var section = ConfigAccessor.GetCategoryForProperty(propertyName) ?? metadata.Category;

		return new AdminConfigService.ConfigItem
		{
			Section = section,
			Key = propertyName,
			Value = Render(value),
			// Nullable value types render as "Nullable`1" and dictionaries as "Dictionary`2"; ConfigItem's
			// IsNullable/IsDictionary predicates are written against exactly those names.
			Type = ConfigAccessor.GetPropertyType(propertyName)?.Name ?? string.Empty,
			RawValue = value,
			Description = string.IsNullOrEmpty(metadata.Description) ? "No Description" : metadata.Description,
			Category = metadata.Category
		};
	}

	private static string Render(object? value) => value switch
	{
		null => string.Empty,
		bool b => b.ToString(),
		string s => s,
		IEnumerable enumerable => string.Join(", ", enumerable.Cast<object>().Select(x => x?.ToString() ?? "null")),
		_ => value.ToString() ?? "null"
	};
}
