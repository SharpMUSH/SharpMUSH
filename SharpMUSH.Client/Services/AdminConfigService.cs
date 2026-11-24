using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

public class AdminConfigService(ILogger<AdminConfigService> logger, IHttpClientFactory httpClient)
{
	private HttpClient Client => httpClient.CreateClient("api");

	/// <summary>Sends a PennMUSH-style config file for the server to apply.</summary>
	/// <remarks>
	/// This used to throw, and the import page caught the exception into the log: a refused file
	/// stopped the spinner and said nothing, which reads as the click not landing.
	/// </remarks>
	public Task<ApiResult<ConfigurationResponse>> ImportFromConfigFileAsync(string configFileContent)
		=> ImportFromConfigFileAsync(configFileContent, new Dictionary<string, string>());

	/// <summary>
	/// Sends a PennMUSH-style config file for the server to apply, with the files its <c>include</c> lines name, by
	/// file name.
	/// </summary>
	public async Task<ApiResult<ConfigurationResponse>> ImportFromConfigFileAsync(string configFileContent,
		IReadOnlyDictionary<string, string> includes)
	{
		var result = includes.Count == 0
			? await Client.PostApiAsync<string, ConfigurationResponse>(
				"/api/configuration/import", configFileContent, "The server returned no configuration.")
			: await Client.PostApiAsync<ConfigurationImportRequest, ConfigurationResponse>(
				"/api/configuration/import/files", new ConfigurationImportRequest(configFileContent, includes),
				"The server returned no configuration.");

		if (result is ApiFailure failure)
			logger.LogError("Importing the configuration file failed: {Reason}", failure.Message);

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
}
