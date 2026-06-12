using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side character-profile reader. Fetches the schema and a character's public field values
/// from the in-game http_handler's routed softcode (<c>GET /http/profile/schema</c>,
/// <c>GET /http/profile?objid=…</c> — see help sharphttp). Characters are addressed by objid
/// (stable across renames); the portal is a pure renderer — the softcode decides which fields
/// exist and what they contain. Read-only: profile editing is currently out of scope.
/// </summary>
public class ProfileService(IHttpClientFactory httpClientFactory, ILogger<ProfileService> logger)
{
	/// <summary>One field definition from the schema.</summary>
	public record FieldSchema(
		string Key,
		string Label,
		string Type,
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

	/// <summary>A character's profile data. HTTP status carries errors — no JSON envelope.</summary>
	public record ProfileData(
		string? Character,
		string? Objid,
		string? Dbref,
		Dictionary<string, FieldValue>? Fields);

	/// <summary>Loads the field/section schema, or <c>null</c> when no handler route is seeded.</summary>
	public async Task<ProfileSchema?> GetSchemaAsync()
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			return await http.GetFromJsonAsync<ProfileSchema>("http/profile/schema");
		}
		catch (HttpRequestException ex)
		{
			// 404 = no handler/route configured.
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

	/// <summary>Loads a character's public profile data by objid, or <c>null</c> when unavailable.</summary>
	public async Task<ProfileData?> GetProfileAsync(string objid)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			return await http.GetFromJsonAsync<ProfileData>($"http/profile?objid={Uri.EscapeDataString(objid)}");
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to load profile for {Objid} (status {Status}).", objid, ex.StatusCode);
			return null;
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			logger.LogWarning(ex, "Profile response for {Objid} was not valid JSON.", objid);
			return null;
		}
	}
}
