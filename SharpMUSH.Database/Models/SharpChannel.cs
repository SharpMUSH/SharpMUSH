namespace SharpMUSH.Database.Models;

public record SharpChannelQueryResult(
	string Id,
	string Key,
	string Name,
	string Description,
	string[] Privs,
	string JoinLock,
	string SpeakLock,
	string SeeLock,
	string HideLock,
	string ModLock,
	int Buffer
);

public record SharpChannelCreateRequest(
	string Name,
	string[] Privs
);

public record SharpChannelUpdateRequest(
	string Key,
	string? Name,
	string? Description,
	string[]? Privs,
	string? JoinLock,
	string? SpeakLock,
	string? SeeLock,
	string? HideLock,
	string? ModLock,
	int? Buffer
);

public record SharpChannelUserStatusQueryResult(
	bool Gagged,
	bool Mute,
	bool Hide,
	bool Combine,
	string Title
);

public record SharpChannelUserStatusUpdateRequest(
	string Key,
	bool? Gagged,
	bool? Mute,
	bool? Hide,
	bool? Combine,
	string? Title
);
