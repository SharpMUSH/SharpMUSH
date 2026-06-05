namespace SharpMUSH.Client.Shared.Dtos;

/// <summary>
/// Summary of a wiki page (for lists).
/// </summary>
public record WikiPageSummaryDto(
    string Slug,
    string Title,
    string Category,
    DateTime LastModified,
    string LastModifiedBy
);

/// <summary>
/// Full wiki page content.
/// </summary>
public record WikiPageDto(
    string Slug,
    string Title,
    string Category,
    string Content,
    DateTime LastModified,
    string LastModifiedBy,
    DateTime Created,
    string CreatedBy
);

/// <summary>
/// Request to create or update a wiki page.
/// </summary>
public record WikiEditRequest(
    string Title,
    string Category,
    string Content
);
