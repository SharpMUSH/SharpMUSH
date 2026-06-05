using SharpMUSH.Client.Shared.Dtos;

namespace SharpMUSH.Client.Shared.Services;

/// <summary>
/// Scene and roleplay operations (both REST and real-time via hub).
/// </summary>
public interface ISceneService
{
    /// <summary>
    /// Get a scene by ID.
    /// </summary>
    Task<SceneDto?> GetSceneAsync(string sceneId);

    /// <summary>
    /// List active scenes with pagination.
    /// </summary>
    Task<OffsetPage<SceneSummaryDto>> ListScenesAsync(int offset = 0, int limit = 20);

    /// <summary>
    /// Create a new scene.
    /// </summary>
    Task<SceneDto> CreateSceneAsync(SceneCreateRequest request);

    /// <summary>
    /// Close a scene (only by scene owner/admin).
    /// </summary>
    Task CloseSceneAsync(string sceneId);

    /// <summary>
    /// Get recent poses from a scene.
    /// </summary>
    Task<IReadOnlyList<PoseDto>> GetPosesAsync(string sceneId, int limit = 50);

    /// <summary>
    /// Get presence in a scene (real-time via hub).
    /// </summary>
    Task<IReadOnlyList<PresenceDto>> GetPresenceAsync(string sceneId);
}
