namespace SharpMUSH.Library.Models;

/// <summary>One stored message and the object number whose mailbox holds it, as <c>@mail/debug</c> checks them.</summary>
/// <param name="Id">The message id, as <see cref="SharpMail.Id"/> carries it.</param>
/// <param name="Recipient">The recipient's object number.</param>
public sealed record MailRecipient(string Id, int Recipient);
