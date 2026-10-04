using SharpMUSH.Library.DiscriminatedUnions;
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

	private HttpClient Client => httpClient.CreateClient("api");

	public async Task<Result<IEnumerable<ConfigItem>>> GetOptionsAsync() =>
		await FetchConfigurationFromServer() switch
		{
			ConfigurationResponse response => Remember(response).ToConfigItems(),
			ApiFailure failure => new Error<string>(failure.Message)
		};

	/// <summary>Sends a PennMUSH-style config file for the server to apply.</summary>
	/// <remarks>
	/// This used to throw, and the import page caught the exception into the log: a refused file
	/// stopped the spinner and said nothing, which reads as the click not landing.
	/// </remarks>
	public async Task<ApiResult<ConfigurationResponse>> ImportFromConfigFileAsync(string configFileContent)
	{
		var result = await Client.PostApiAsync<string, ConfigurationResponse>(
			"/api/configuration/import", configFileContent, "The server returned no configuration.");

		switch (result)
		{
			case ConfigurationResponse response:
				Remember(response);
				break;
			case ApiFailure failure:
				logger.LogError("Importing the configuration file failed: {Reason}", failure.Message);
				break;
		}

		return result;
	}

	/// <summary>Sends the changed values; the server answers with the configuration it now runs with.</summary>
	/// <remarks>
	/// Not on <see cref="ApiCall"/>, deliberately: a refused save answers
	/// <c>{ "errors": { "&lt;path&gt;": "…", "_global": "…" } }</c>, and the editor places each message
	/// next to its field. <see cref="ApiFailure.ServerSentence"/> keeps one sentence, so a body of that
	/// shape comes back as a <see cref="ConfigSaveRefusal"/>; any other failure is an
	/// <see cref="ApiFailure"/> like every other call's.
	/// </remarks>
	public async Task<ConfigSaveResult> UpdateConfigAsync(Dictionary<string, object?> changes)
	{
		try
		{
			using var response = await Client.PatchAsJsonAsync("/api/configuration", changes);

			if (!response.IsSuccessStatusCode)
			{
				var body = await response.Content.ReadAsStringAsync();
				logger.LogError("Config update failed: {StatusCode} {Error}", response.StatusCode, body);
				return ConfigSaveRefusal.Parse(body) switch
				{
					ConfigSaveRefusal refusal => refusal,
					NotFound => ApiFailure.FromStatus(response.StatusCode, body)
				};
			}

			if (await response.Content.ReadFromJsonAsync<ConfigurationResponse>() is not { } saved)
				return new ApiFailure(ApiFailureKind.Unexpected, "The server returned no configuration.", response.StatusCode);

			if (saved.Configuration != null)
				_currentOptions = saved.Configuration;

			return saved;
		}
		catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
		{
			logger.LogError(ex, "Error updating configuration");
			return ApiFailure.Malformed(ex);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error updating configuration");
			return ApiFailure.Transport(ex);
		}
	}

	/// <summary>The server's configuration as the JSON file it exports.</summary>
	public async Task<ApiResult<string>> ExportConfigAsync()
	{
		var result = await Client.GetTextApiAsync("/api/configuration/export");

		if (result is ApiFailure failure)
			logger.LogError("Exporting the configuration failed: {Reason}", failure.Message);

		return result;
	}

	public void ResetToDefault()
	{
		_currentOptions = null;
	}

	/// <summary>The configuration the server is running with, its metadata and its schema.</summary>
	/// <remarks>
	/// A failure used to come back as a response whose <see cref="ConfigurationResponse.Configuration"/>
	/// was <see langword="null"/> behind a <c>null!</c>, so the reason was lost and a caller that forgot
	/// to check dereferenced it. A response that names no configuration is a failure here too.
	/// </remarks>
	public async Task<ApiResult<ConfigurationResponse>> FetchConfigurationFromServer()
	{
		var result = await Client.GetApiAsync<ConfigurationResponse>(
			"/api/configuration", "The server returned no configuration.");

		if (result is ConfigurationResponse { Configuration: null })
			result = new ApiFailure(ApiFailureKind.Unexpected, "The configuration response carried no configuration.");

		if (result is ApiFailure failure)
			logger.LogError("Fetching the configuration failed: {Reason}", failure.Message);

		return result;
	}

	private ConfigurationResponse Remember(ConfigurationResponse response)
	{
		_currentOptions = response.Configuration;
		_metadata = response.Metadata;
		return response;
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
	public static Result<IEnumerable<AdminConfigService.ConfigItem>> ToConfigItems(this ConfigurationResponse options)
	{
		if (options.Configuration is null)
		{
			return new Error<string>("The configuration response carried no configuration.");
		}

		var configItems = ConfigMetadata.PropertyMetadata
			.Select(entry => ToConfigItem(options, entry.Key, entry.Value))
			.OrderBy(x => x.Section)
			.ThenBy(x => x.Key)
			.ToList();

		return new Result<IEnumerable<AdminConfigService.ConfigItem>>(configItems);
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

		// The owning property on SharpMUSHOptions ("Net"), not SharpConfigAttribute.Category. Those
		// disagree: ConfigMetadataGenerator overwrites the declared Category with the containing record's
		// type name ("NetOptions"), while SchemaBuilder reports the property name. Section and Category
		// carried the same value before, so keep them agreeing on the spelling the schema also uses.
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
			Category = section
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
