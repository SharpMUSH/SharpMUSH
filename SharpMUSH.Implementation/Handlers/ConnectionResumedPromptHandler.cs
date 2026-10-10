using Mediator;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Takes down a prompt a resumed WebSocket client may still show when no <c>@input</c> session is
/// capturing its lines any more. A reloaded page restores the prompt it last showed, and the clear for
/// a session that ended before the page's <c>lastSeq</c> is not in the replay.
/// </summary>
public class ConnectionResumedPromptHandler(IInputSessionService inputSessions)
	: INotificationHandler<ConnectionResumedNotification>
{
	public ValueTask Handle(ConnectionResumedNotification notification, CancellationToken cancellationToken)
		=> inputSessions.ClearPromptUnlessCapturingAsync(notification.Handle);
}
