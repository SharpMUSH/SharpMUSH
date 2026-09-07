using Mediator;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
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
/// <param name="RemainingConnections">
/// For a QUIT-driven <see cref="ConnectionService.Disconnect"/> only: the player's remaining
/// connection count, computed atomically with the disconnecting handle's removal so two concurrent
/// disconnects of the same player's two handles can't both observe the other as "still connected"
/// and both conclude one connection remains. Null for every other transition (including the LOGOUT
/// path through <see cref="ConnectionService.Unbind"/>, which does not need it - <c>Unbind</c> already
/// nulls its handle's <c>Ref</c> before publishing, so a concurrent <c>Get(playerRef)</c> naturally
/// excludes it without this field).
/// </param>
public record ConnectionStateChangeNotification(
	long Handle,
	DBRef? PlayerRef,
	IConnectionService.ConnectionState OldState,
	IConnectionService.ConnectionState NewState,
	bool FirstLogin = false,
	int? RemainingConnections = null) : INotification;
