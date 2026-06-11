using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side character-profile reader. Fetches the admin-defined schema and a character's
/// visible field values from the server (<c>GET /api/profile-schema</c>, <c>GET /api/profile/{name}</c>),
/// which proxy the in-game http_handler. The portal is a pure renderer — the handler decides which
/// fields exist and which are visible to the requesting viewer.
/// </summary>
public class ProfileService(IHttpClientFactory httpClientFactory, ILogger<ProfileService> logger)
{
	/// <summary>One field definition from the schema.</summary>
	public record FieldSchema(
		string Key,
		string Label,
		string Type,
		[property: JsonPropertyName("editable_by")] string? EditableBy,
		[property: JsonPropertyName("visible_to")] string? VisibleTo);

	/// <summary>A schema section grouping related fields.</summary>
	public record SectionSchema(
		string Name,
		int Order,
		[property: JsonPropertyName("visible_to")] string? VisibleTo,
		IReadOnlyList<FieldSchema>? Fields);

	/// <summary>The full profile schema.</summary>
	public record ProfileSchema(IReadOnlyList<SectionSchema>? Sections);

	/// <summary>A single field's value plus whether the viewer may see it.</summary>
	public record FieldValue(string? Value, bool Visible);

	/// <summary>A character's profile data as the handler chose to expose it to this viewer.</summary>
	public record ProfileData(
		int Status,
		string? Character,
		string? Dbref,
		Dictionary<string, FieldValue>? Fields,
		string? Error);

	/// <summary>Loads the field/section schema, or <c>null</c> when no handler is configured.</summary>
	public async Task<ProfileSchema?> GetSchemaAsync()
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			return await http.GetFromJsonAsync<ProfileSchema>("api/profile-schema");
		}
		catch (HttpRequestException ex)
		{
			// 404 = no handler configured; 502 = handler returned invalid output (see ProfileController).
			logger.LogWarning(ex, "Failed to load profile schema (status {Status}).", ex.StatusCode);
			return null;
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			// Defensive: a misconfigured handler that still returns a 200 with a non-JSON body
			// must degrade to "no schema" rather than crash the character page.
			logger.LogWarning(ex, "Profile schema response was not valid JSON.");
			return null;
		}
	}

	/// <summary>Loads a character's visible profile data, or <c>null</c> when unavailable.</summary>
	public async Task<ProfileData?> GetProfileAsync(string name)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			return await http.GetFromJsonAsync<ProfileData>($"api/profile/{Uri.EscapeDataString(name)}");
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to load profile for {Name} (status {Status}).", name, ex.StatusCode);
			return null;
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			logger.LogWarning(ex, "Profile response for {Name} was not valid JSON.", name);
			return null;
		}
	}

	/// <summary>The handler's response to an edit: which fields it actually applied.</summary>
	public record UpdateResult(int Status, string? Updated, string? Error);

	/// <summary>
	/// Submits changed fields to the handler (<c>POST /api/profile/{name}</c>). The handler enforces
	/// editability per viewer and returns the space-separated list of fields it applied.
	/// Returns <c>null</c> when the request fails or is rejected (e.g. not authorized).
	/// </summary>
	public async Task<UpdateResult?> UpdateAsync(string name, IReadOnlyDictionary<string, string> fields)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync($"api/profile/{Uri.EscapeDataString(name)}", fields);
			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("Profile update for {Name} returned {Status}.", name, response.StatusCode);
				return null;
			}
			return await response.Content.ReadFromJsonAsync<UpdateResult>();
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to update profile for {Name}.", name);
			return null;
		}
	}
}
