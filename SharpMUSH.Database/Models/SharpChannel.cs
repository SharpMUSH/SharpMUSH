using System.Text.Json.Serialization;

namespace SharpMUSH.Database.Models;

public record SharpChannelQueryResult(
	string Id,
	string Key,
	string Name,
	string MarkedUpName,
	string Description,
	string[] Privs,
	string JoinLock,
	string SpeakLock,
	string SeeLock,
	string HideLock,
	string ModLock,
	string Mogrifier,
	int Buffer
);

public record SharpChannelCreateRequest(
	string Name,
	string MarkedUpName,
	string[] Privs
);

/// <summary>
/// One channel member as projected by the batched membership query: the typed vertex, the Objects
/// document with its relations merged in, and the membership edge.
/// </summary>
public record SharpChannelMemberQueryResult(
	[property: JsonPropertyName("Typed")] System.Text.Json.JsonElement Typed,
	[property: JsonPropertyName("Object")] System.Text.Json.JsonElement Object,
	[property: JsonPropertyName("Status")] SharpChannelUserStatusQueryResult Status);

public record SharpChannelMemberListQueryResult(
	[property: JsonPropertyName("Id")] string Id,
	[property: JsonPropertyName("Status")] SharpChannelUserStatusQueryResult Status);

public record SharpChannelUserStatusQueryResult(
	bool? Gagged,
	bool? Mute,
	bool? Hide,
	bool? Combine,
	string? Title
);
