namespace SharpMUSH.Client.Shared.Dtos;

/// <summary>
/// Summary of a character (for lists, roster).
/// </summary>
public record CharacterSummaryDto(
    string Id,
    string Name,
    string Title,
    DateTime LastSeen,
    string? CurrentLocation
);

/// <summary>
/// Full character profile information.
/// </summary>
public record CharacterProfileDto(
    string Id,
    string Name,
    string Title,
    string Description,
    string? HomeLocation,
    string? CurrentLocation,
    bool Online,
    DateTime LastSeen,
    DateTime Created,
    Dictionary<string, string> Attributes
);
