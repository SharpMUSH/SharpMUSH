using System.Net.Http.Json;
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
			logger.LogWarning(ex, "Failed to load profile schema.");
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
			logger.LogWarning(ex, "Failed to load profile for {Name}.", name);
			return null;
		}
	}
}
