using Mediator;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Takes down a prompt a resumed WebSocket client may still show when no <c>@input</c> session is
/// capturing its lines any more. A reloaded page restores the prompt it last showed, and the clear for
/// a session that ended before the page's <c>lastSeq</c> is not in the replay.
/// </summary>
public class ConnectionResumedPromptHandler(
	IInputSessionService inputSessions,
	IConnectionService connections,
	INotifyService notify)
	: INotificationHandler<ConnectionResumedNotification>
{
	public ValueTask Handle(ConnectionResumedNotification notification, CancellationToken cancellationToken)
	{
		if (inputSessions.GetCapturing(notification.Handle) is not null
			|| connections.Get(notification.Handle)?.Metadata.GetValueOrDefault("SessionId") is not { } sessionId)
			return ValueTask.CompletedTask;
		return notify.ClearPromptToSession(notification.Handle, sessionId, null);
	}
}
