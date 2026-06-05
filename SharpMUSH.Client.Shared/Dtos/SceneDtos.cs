namespace SharpMUSH.Client.Shared.Dtos;

/// <summary>
/// Summary of a scene (for lists).
/// </summary>
public record SceneSummaryDto(
    string Id,
    string Title,
    string Room,
    DateTime StartedAt,
    int ParticipantCount
);

/// <summary>
/// Full scene information including current state.
/// </summary>
public record SceneDto(
    string Id,
    string Title,
    string Room,
    DateTime StartedAt,
    string Description,
    IReadOnlyList<string> Participants,
    IReadOnlyList<PoseDto> Poses
);

/// <summary>
/// Request to create a new scene.
/// </summary>
public record SceneCreateRequest(
    string Title,
    string Room,
    string Description
);

/// <summary>
/// A single pose (action) in a scene.
/// </summary>
public record PoseDto(
    string Id,
    string Character,
    string Content,
    DateTime PostedAt
);
