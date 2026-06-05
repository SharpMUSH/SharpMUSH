namespace SharpMUSH.Server.Hubs;

/// <summary>
/// Strongly-typed client interface for SignalR hub method calls from server to client.
/// </summary>
public interface IPortalHubClient
{
	/// <summary>
	/// Called when a pose is received for a scene the client is subscribed to.
	/// </summary>
	Task OnPoseReceived(string sceneId, string characterName, string poseText);

	/// <summary>
	/// Called when character presence changes (joined/left a scene).
	/// </summary>
	Task OnPresenceChanged(string sceneId, string characterName, string action);

	/// <summary>
	/// Called when a wiki page is updated.
	/// </summary>
	Task OnWikiPageUpdated(string pageName, string updatedBy, long timestamp);

	/// <summary>
	/// Called when a mail message is received for the authenticated account.
	/// </summary>
	Task OnMailReceived(string mailId, string fromCharacter, string subject, string timestamp);

	/// <summary>
	/// Called for general portal notifications.
	/// </summary>
	Task OnNotification(string message, string notificationType);
}
