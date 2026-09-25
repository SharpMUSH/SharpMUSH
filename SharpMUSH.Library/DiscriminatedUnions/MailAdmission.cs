namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// The answer to storing a message against a folder limit: the message as stored, or the folder was full and
/// nothing was stored.
/// </summary>
public union MailAdmission(AdmittedMail, MailboxFull);

/// <summary>A stored message: its id, and its 1-based number in its folder as of the write that stored it.</summary>
public readonly record struct AdmittedMail(string Id, int Number);

/// <summary>The folder already held its limit, so the message was refused and nothing was written.</summary>
public readonly record struct MailboxFull;
