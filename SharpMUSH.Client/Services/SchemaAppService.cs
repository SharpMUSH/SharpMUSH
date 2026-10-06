using System.Net.Http.Json;
using System.Text.Json;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side reader/poster for Dynamic Applications (Area 21). Generalizes <see cref="ProfileService"/>:
/// fetches a Portal Schema Document and its data from the in-game http_handler, and POSTs action
/// payloads back. The portal is a pure renderer — softcode owns the schema, the data, the validation,
/// and the side effects. All routes are relative to the named "api" HttpClient (the <c>/http/...</c>
/// handler prefix). Network/parse failures come back as an <see cref="ApiFailure"/> rather than crashing the page.
/// </summary>
public class SchemaAppService(IHttpClientFactory httpClientFactory, ILogger<SchemaAppService> logger)
{
	/// <summary>Loads a Portal Schema Document, or the <see cref="ApiFailure"/> when the route is missing or invalid.</summary>
	public Task<ApiResult<PortalSchemaDocument>> GetSchemaAsync(string schemaUrl) =>
		LoggedAsync(httpClientFactory.CreateClient("api")
			.GetApiAsync<PortalSchemaDocument>(schemaUrl, "The schema route returned no document.", SchemaJson.Options),
			"schema", schemaUrl);

	/// <summary>Loads a data payload (view display / form prefill), or the <see cref="ApiFailure"/> when unavailable.</summary>
	public Task<ApiResult<SchemaData>> GetDataAsync(string dataUrl) =>
		LoggedAsync(httpClientFactory.CreateClient("api")
			.GetApiAsync<SchemaData>(dataUrl, "The data route returned no payload.", SchemaJson.Options),
			"data", dataUrl);

	/// <summary>Loads a page application's sidebar entries for the viewer, or the <see cref="ApiFailure"/> when unavailable.</summary>
	public Task<ApiResult<AppNav>> GetNavAsync(string navUrl) =>
		LoggedAsync(httpClientFactory.CreateClient("api")
			.GetApiAsync<AppNav>(navUrl, "The sidebar route returned nothing.", SchemaJson.Options),
			"sidebar", navUrl);

	private async Task<ApiResult<T>> LoggedAsync<T>(Task<ApiResult<T>> read, string what, string url)
	{
		var result = await read;
		if (result is ApiFailure failure)
			logger.LogWarning("Failed to load {What} from {Url}: {Reason}", what, url, failure.Message);
		return result;
	}

	/// <summary>
	/// Reads a data payload that arrived by other means than a data route — an OOB package's latest push.
	/// Absent, empty or malformed JSON, or JSON that is not a data payload, is <see cref="NotFound"/>.
	/// </summary>
	public static Found<SchemaData> ParseData(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return new NotFound();
		}

		try
		{
			return JsonSerializer.Deserialize<SchemaData>(json, SchemaJson.Options) is SchemaData data
				? data
				: new NotFound();
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			return new NotFound();
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
