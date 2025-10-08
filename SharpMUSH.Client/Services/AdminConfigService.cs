using OneOf.Types;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
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

	public void ResetToDefault()
	{
		_currentOptions = null;
	}

	private async Task<ConfigurationResponse> FetchConfigurationFromServer()
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
	}
}

public static class SharpMUSHOptionsExtension
{
	public static OneOf.OneOf<IEnumerable<AdminConfigService.ConfigItem>, Error<string>> ToConfigItems(this ConfigurationResponse options)
	{
		var configItems = new List<AdminConfigService.ConfigItem>();

		try
		{
			// Use reflection to get all properties and their values
			var optionsType = typeof(SharpMUSHOptions);
			var properties = optionsType.GetProperties();

			foreach (var prop in properties)
			{
				try
				{
					var sectionName = prop.Name;
					var sectionValue = prop.GetValue(options);

					if (sectionValue == null)
					{
						continue;
					}

					var sectionType = prop.PropertyType;
					var sectionProperties = sectionType.GetProperties();

					foreach (var sectionProp in sectionProperties)
					{
						try
						{
							var value = sectionProp.GetValue(sectionValue);
							var valueString = value switch
							{
								null => "",
								bool b => b.ToString(),
								string s => s,
								System.Collections.IEnumerable enumerable and not string =>
									string.Join(", ", enumerable.Cast<object>().Select(x => x?.ToString() ?? "null")),
								_ => value.ToString() ?? "null"
							};

							// Get metadata from centralized source

							configItems.Add(new AdminConfigService.ConfigItem
							{
								Section = options.Metadata[sectionProp.Name].Category,
								Key = sectionProp.Name,
								Value = valueString,
								Type = sectionProp.PropertyType.Name,
								RawValue = value,
								Description = options.Metadata[sectionProp.Name].Description ?? "No Description",
								Category = options.Metadata[sectionProp.Name].Category
							});
						}
						catch (Exception ex)
						{
							// If we can't get a specific property, add an error entry
							configItems.Add(new AdminConfigService.ConfigItem
							{
								Section = sectionName,
								Key = sectionProp.Name,
								Value = $"Error: {ex.Message}",
								Type = sectionProp.PropertyType.Name,
								Description = "Error loading property",
								Category = sectionName
							});
						}
					}
				}
				catch (Exception ex)
				{
					// If we can't process a section, add an error entry
					configItems.Add(new AdminConfigService.ConfigItem
					{
						Section = prop.Name,
						Key = "Error",
						Value = $"Failed to load section: {ex.Message}",
						Type = "Error",
						Description = "Error loading section",
						Category = "Error"
					});
				}
			}
		}
		catch (Exception ex)
		{
			// If everything fails, return a single error item
			return OneOf.OneOf<IEnumerable<AdminConfigService.ConfigItem>, Error<string>>.FromT0([new AdminConfigService.ConfigItem
			{
				Section = "Error",
				Key = "ConfigurationError",
				Value = $"Failed to load configuration: {ex.Message}",
				Type = "Error",
				Description = "Critical configuration error",
				Category = "Error"
			}]);
		}

		return OneOf.OneOf<IEnumerable<AdminConfigService.ConfigItem>, Error<string>>.FromT0(configItems
			.OrderBy(x => x.Section)
			.ThenBy(x => x.Key));
	}
}