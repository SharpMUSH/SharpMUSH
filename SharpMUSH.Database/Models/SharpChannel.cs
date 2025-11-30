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

public record SharpChannelMemberListQueryResult(
	[property:JsonPropertyName("Id")]string Id,
	[property:JsonPropertyName("Status")]SharpChannelUserStatusQueryResult Status);

public record SharpChannelUserStatusQueryResult(
	bool? Gagged,
	bool? Mute,
	bool? Hide,
	bool? Combine,
	string? Title
);

public record SharpChannelUserStatusUpdateRequest(
	string Key,
	bool? Gagged,
	bool? Mute,
	bool? Hide,
	bool? Combine,
	string? Title
);
