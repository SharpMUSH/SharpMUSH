using SharpMUSH.Client.Shared.Dtos;

namespace SharpMUSH.Client.Shared.Hubs;

/// <summary>
/// Client-side callback methods that the server can invoke.
/// </summary>
public interface IPortalHubClient
{
    /// <summary>
    /// Receive a new pose in the current scene.
    /// </summary>
    Task OnPoseReceived(PoseDto pose);

    /// <summary>
    /// Presence list changed (someone joined/left/idled).
    /// </summary>
    Task OnPresenceChanged(IReadOnlyList<PresenceDto> presence);

    /// <summary>
    /// New notification for the user.
    /// </summary>
    Task OnNotificationReceived(NotificationDto notification);

    /// <summary>
    /// Another character is typing.
    /// </summary>
    Task OnCharacterTyping(string characterName);

    /// <summary>
    /// Scene ended or was closed.
    /// </summary>
    Task OnSceneEnded(string sceneId);

    /// <summary>
    /// Error occurred (connectivity, server error, etc.).
    /// </summary>
    Task OnError(string message);
}
