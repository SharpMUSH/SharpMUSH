using SharpMUSH.Client.Shared.Dtos;

namespace SharpMUSH.Client.Shared.Hubs;

/// <summary>
/// Server-side hub methods that clients can call.
/// </summary>
public interface IPortalHubServer
{
    /// <summary>
    /// Client joins a scene.
    /// </summary>
    Task JoinScene(string sceneId, string characterName);

    /// <summary>
    /// Client leaves a scene.
    /// </summary>
    Task LeaveScene(string sceneId);

    /// <summary>
    /// Client posts a pose in a scene.
    /// </summary>
    Task PostPose(string sceneId, string content);

    /// <summary>
    /// Get presence list for a scene.
    /// </summary>
    Task<IReadOnlyList<PresenceDto>> GetPresence(string sceneId);

    /// <summary>
    /// Typing indicator.
    /// </summary>
    Task IndicateTyping(string sceneId);
}
