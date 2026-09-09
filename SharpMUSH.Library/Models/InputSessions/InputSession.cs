using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Models.InputSessions;

/// <summary>A generation-bound capture, carrying full identities rather than reusable object numbers.</summary>
public sealed record InputSession(
	Guid Id,
	IConnectionService.ConnectionData Connection,
	string? TransportSessionId,
	DBRef Character,
	DBRef Executor,
	DBRef Owner,
	DBRef CallbackTarget,
	DBRef CallbackOwner,
	string CallbackAttribute,
	DateTimeOffset ExpiresAt);
