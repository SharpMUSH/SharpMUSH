namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The lifecycle state of the client-side SignalR connection to the game hub.
/// </summary>
public enum HubConnectionState
{
	Disconnected,
	Connecting,
	Connected,
	Reconnecting,
}
