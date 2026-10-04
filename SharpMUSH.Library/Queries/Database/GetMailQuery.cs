using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetMailListQuery(SharpPlayer Player, string Folder) : IStreamQuery<SharpMail>;

public record GetAllMailListQuery(SharpPlayer Player) : IStreamQuery<SharpMail>;

/// <summary>A folder's messages without their bodies, in <see cref="GetMailListQuery"/>'s order.</summary>
public record GetMailSummaryListQuery(SharpPlayer Player, string Folder) : IStreamQuery<MailSummary>;

/// <summary>The non-empty folder names a mailbox holds mail in, read from the folder index.</summary>
public record GetMailFoldersQuery(SharpPlayer Player) : IQuery<string[]>;

public record GetMailQuery(SharpPlayer Player, int Mail, string Folder) : IQuery<SharpMail?>;

public record GetSentMailListQuery(SharpObject Sender, SharpPlayer Recipient) : IStreamQuery<SharpMail>;

public record GetAllSentMailListQuery(SharpObject Sender) : IStreamQuery<SharpMail>;

public record GetSentMailQuery(SharpObject Sender, int Mail, SharpPlayer Recipient) : IQuery<SharpMail?>;

public record GetAllSystemMailQuery() : IStreamQuery<SharpMail>;