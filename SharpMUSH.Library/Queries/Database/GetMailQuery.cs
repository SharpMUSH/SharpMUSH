using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetMailListQuery(SharpPlayer Player, string Folder) : IStreamQuery<SharpMail>;

public record GetAllMailListQuery(SharpPlayer Player) : IStreamQuery<SharpMail>;

public record GetMailQuery(SharpPlayer Player, int Mail, string Folder) : IQuery<SharpMail?>;

public record GetSentMailListQuery(SharpObject Sender, SharpPlayer Recipient) : IStreamQuery<SharpMail>;

public record GetAllSentMailListQuery(SharpObject Sender) : IStreamQuery<SharpMail>;

public record GetSentMailQuery(SharpObject Sender, int Mail, SharpPlayer Recipient) : IQuery<SharpMail?>;

public record GetAllSystemMailQuery() : IStreamQuery<SharpMail>;