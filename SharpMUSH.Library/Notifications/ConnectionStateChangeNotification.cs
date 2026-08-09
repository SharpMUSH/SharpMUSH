using Mediator;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Notifications;

/// <summary>
/// Notification published when a connection state changes.
/// </summary>
/// <param name="FirstLogin">
/// True when this transition to <see cref="IConnectionService.ConnectionState.LoggedIn"/> is the
/// character's entry into play immediately after it was created, so it can be greeted as new
/// rather than welcomed "back" to a game it has never seen.
/// </param>
public record ConnectionStateChangeNotification(
	long Handle,
	DBRef? PlayerRef,
	IConnectionService.ConnectionState OldState,
	IConnectionService.ConnectionState NewState,
	bool FirstLogin = false) : INotification;
