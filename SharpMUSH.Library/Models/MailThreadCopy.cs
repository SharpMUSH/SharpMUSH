namespace SharpMUSH.Library.Models;

/// <summary>One stored copy of a message in a conversation, as one reader sees it.</summary>
/// <param name="Mail">The copy.</param>
/// <param name="Sent">The reader sent it.</param>
/// <param name="Held">It is in the reader's own mailbox.</param>
public sealed record MailThreadCopy(SharpMail Mail, bool Sent, bool Held);
