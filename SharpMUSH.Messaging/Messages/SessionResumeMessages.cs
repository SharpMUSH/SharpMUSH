namespace SharpMUSH.Messaging.Messages;

public record SessionResumeRequestMessage(
	Guid RequestId, long Handle, string SessionId, string IpAddress, string Hostname, bool IsSecure);

public record SessionResumeResponseMessage(
	Guid RequestId, long Handle, string SessionId, bool Accepted);
