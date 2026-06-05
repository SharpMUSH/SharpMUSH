namespace SharpMUSH.Client.Shared.Dtos;

/// <summary>
/// Mail message data.
/// </summary>
public record MailMessageDto(
    string Id,
    string From,
    string To,
    string Subject,
    string Body,
    DateTime SentAt,
    bool IsRead
);

/// <summary>
/// Request to send a mail message.
/// </summary>
public record MailSendRequest(
    string To,
    string Subject,
    string Body
);
