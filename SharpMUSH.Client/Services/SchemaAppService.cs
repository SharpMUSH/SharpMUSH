using System.Net.Http.Json;
using System.Text.Json;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side reader/poster for Dynamic Applications (Area 21). Generalizes <see cref="ProfileService"/>:
/// fetches a Portal Schema Document and its data from the in-game http_handler, and POSTs action
/// payloads back. The portal is a pure renderer — softcode owns the schema, the data, the validation,
/// and the side effects. All routes are relative to the named "api" HttpClient (the <c>/http/...</c>
/// handler prefix). Network/parse failures degrade to <c>null</c> rather than crashing the page.
/// </summary>
public class SchemaAppService(IHttpClientFactory httpClientFactory, ILogger<SchemaAppService> logger)
{
	/// <summary>Loads a Portal Schema Document, or <c>null</c> when the route is missing or invalid.</summary>
	public async Task<PortalSchemaDocument?> GetSchemaAsync(string schemaUrl)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			return await http.GetFromJsonAsync<PortalSchemaDocument>(schemaUrl, SchemaJson.Options);
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to load schema from {Url} (status {Status}).", schemaUrl, ex.StatusCode);
			return null;
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			logger.LogWarning(ex, "Schema response from {Url} was not valid JSON.", schemaUrl);
			return null;
		}
	}

	/// <summary>Loads a data payload (view display / form prefill), or <c>null</c> when unavailable.</summary>
	public async Task<SchemaData?> GetDataAsync(string dataUrl)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			return await http.GetFromJsonAsync<SchemaData>(dataUrl, SchemaJson.Options);
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to load data from {Url} (status {Status}).", dataUrl, ex.StatusCode);
			return null;
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			logger.LogWarning(ex, "Data response from {Url} was not valid JSON.", dataUrl);
			return null;
		}
	}

	/// <summary>
	/// POSTs the collected field values to an action route and returns the structured envelope.
	/// On transport failure, returns an envelope with <c>Ok = false</c> and a <c>_global</c> error so
	/// the renderer can surface it the same way it surfaces softcode-reported errors.
	/// </summary>
	public async Task<SchemaActionResult> SubmitAsync(string route, IReadOnlyDictionary<string, object?> payload)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync(route, payload, SchemaJson.Options);
			var body = await response.Content.ReadAsStringAsync();

			SchemaActionResult? result = null;
			try
			{
				result = JsonSerializer.Deserialize<SchemaActionResult>(body, SchemaJson.Options);
			}
			catch (JsonException ex)
			{
				logger.LogWarning(ex, "Action response from {Route} was not valid JSON.", route);
			}

			if (result is not null)
			{
				return result;
			}

			// Non-JSON or empty body: synthesize a result from the HTTP status.
			return response.IsSuccessStatusCode
				? new SchemaActionResult(true, null, null, null, null, null)
				: Failure($"The action handler returned HTTP {(int)response.StatusCode}.");
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Action POST to {Route} failed.", route);
			return Failure("The action could not be sent. Please try again.");
		}
	}

	private static SchemaActionResult Failure(string globalMessage)
		=> new(false, new Dictionary<string, string> { ["_global"] = globalMessage }, null, null, null, null);
}
