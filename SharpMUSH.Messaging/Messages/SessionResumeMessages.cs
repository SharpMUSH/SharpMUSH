namespace SharpMUSH.Messaging.Messages;

public record SessionResumeRequestMessage(
	Guid RequestId, long Handle, string SessionId, string IpAddress, string Hostname, bool IsSecure)
{
	/// <summary>The resuming socket owner's <see cref="ConnectionEstablishedMessage.OrderedPrompts"/>.</summary>
	public bool OrderedPrompts { get; init; }
}

public record SessionResumeResponseMessage(
	Guid RequestId, long Handle, string SessionId, bool Accepted, bool Retryable = false);
