using Mediator;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Notifications;

/// <summary>
/// Notification published when a connection state changes.
/// </summary>
public record ConnectionStateChangeNotification(
	long Handle,
	DBRef? PlayerRef,
	IConnectionService.ConnectionState OldState,
	IConnectionService.ConnectionState NewState) : INotification;
