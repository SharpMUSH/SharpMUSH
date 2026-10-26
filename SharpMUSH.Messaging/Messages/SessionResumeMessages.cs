namespace SharpMUSH.Messaging.Messages;

public record SessionResumeRequestMessage(
	Guid RequestId, long Handle, string SessionId, string IpAddress, string Hostname, bool IsSecure)
{
	/// <summary>The resuming socket owner's <see cref="ConnectionEstablishedMessage.OrderedPrompts"/>.</summary>
	public bool OrderedPrompts { get; init; }
}

public record SessionResumeResponseMessage(
	Guid RequestId, long Handle, string SessionId, bool Accepted, bool Retryable = false);

/// <summary>
/// Sent by the connection server once a socket is rebound to <paramref name="SessionId"/> and its
/// missed output replayed, so the engine re-sends the session's current state: a reloaded page holds
/// none of what was pushed before its <c>lastSeq</c>.
/// </summary>
public record SessionResumedMessage(long Handle, string SessionId);

/// <summary>
/// Sent by the connection server when a socket drops and its session is held for the client to resume,
/// so the engine stops counting the player as online until <see cref="SessionResumedMessage"/> or
/// <paramref name="HeldUntil"/>. The two travel on different subjects, so a resume can arrive first;
/// the deadline bounds how long such a session is wrongly left out.
/// </summary>
public record SessionDetachedMessage(long Handle, string SessionId, DateTimeOffset HeldUntil);
