namespace SharpMUSH.Client.Shared.Dtos;

/// <summary>
/// Character presence information (who is online, where).
/// </summary>
public record PresenceDto(
    string Character,
    string Location,
    int IdleSeconds
);

/// <summary>
/// Notification for portal users.
/// </summary>
public record NotificationDto(
    string Id,
    string Type,
    string Title,
    string Message,
    DateTime CreatedAt,
    bool IsRead,
    Dictionary<string, string>? Metadata
);
