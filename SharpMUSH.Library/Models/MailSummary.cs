namespace SharpMUSH.Library.Models;

/// <summary>
/// One message of a mailbox listing without its body: what a mail list shows, read without decoding the
/// message text or hydrating the sender.
/// </summary>
/// <param name="Id">The message id, as <see cref="SharpMail.Id"/> carries it.</param>
/// <param name="SenderName">The sender's name; null when the sender no longer exists or the message is anonymous.</param>
/// <param name="Anonymous">Sent anonymously (<see cref="SharpMail.Anonymous"/>).</param>
public sealed record MailSummary(
	string Id,
	DateTimeOffset DateSent,
	bool Read,
	bool Urgent,
	string Folder,
	MString Subject,
	string? SenderName,
	bool Anonymous = false);
