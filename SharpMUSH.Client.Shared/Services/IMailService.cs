using SharpMUSH.Client.Shared.Dtos;

namespace SharpMUSH.Client.Shared.Services;

/// <summary>
/// Mail/message operations.
/// </summary>
public interface IMailService
{
    /// <summary>
    /// Get a mail message by ID.
    /// </summary>
    Task<MailMessageDto?> GetMessageAsync(string messageId);

    /// <summary>
    /// List incoming mail with pagination.
    /// </summary>
    Task<OffsetPage<MailMessageDto>> ListInboxAsync(int offset = 0, int limit = 20);

    /// <summary>
    /// List outgoing mail with pagination.
    /// </summary>
    Task<OffsetPage<MailMessageDto>> ListSentAsync(int offset = 0, int limit = 20);

    /// <summary>
    /// Send a mail message.
    /// </summary>
    Task<MailMessageDto> SendAsync(MailSendRequest request);

    /// <summary>
    /// Mark message as read.
    /// </summary>
    Task MarkAsReadAsync(string messageId);

    /// <summary>
    /// Delete a mail message.
    /// </summary>
    Task DeleteAsync(string messageId);
}
