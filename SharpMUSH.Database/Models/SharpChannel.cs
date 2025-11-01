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

public record SharpChannelUserStatusQueryResult(
	string From,
	string To,
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
