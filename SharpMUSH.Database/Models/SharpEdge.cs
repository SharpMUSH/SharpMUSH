using System.Text.Json.Serialization;

namespace SharpMUSH.Database.Models;

public record SharpEdgeQueryResult(
	[property: JsonPropertyName("_id")] string Id,
	[property: JsonPropertyName("_key")] string Key,
	[property: JsonPropertyName("_from")] string From,
	[property: JsonPropertyName("_to")] string To);

public record SharpEdgeCreateRequest(string From, string To);

public record SharpHookEdgeQueryResult(
	[property: JsonPropertyName("_id")] string Id,
	[property: JsonPropertyName("_key")] string Key,
	[property: JsonPropertyName("_from")] string From,
	[property: JsonPropertyName("_to")] string To,
	string Type);

public record SharpHookEdgeCreateRequest(string From, string To);
