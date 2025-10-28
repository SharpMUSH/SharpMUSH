using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetMailListQuery(SharpPlayer Player, string Folder) : IQuery<IAsyncEnumerable<SharpMail>>;

public record GetAllMailListQuery(SharpPlayer Player) : IQuery<IAsyncEnumerable<SharpMail>>;

public record GetMailQuery(SharpPlayer Player, int Mail, string Folder) : IQuery<SharpMail?>;

public record GetSentMailListQuery(SharpObject Sender, SharpPlayer Recipient) : IQuery<IAsyncEnumerable<SharpMail>>;

public record GetAllSentMailListQuery(SharpObject Sender) : IQuery<IAsyncEnumerable<SharpMail>>;

public record GetSentMailQuery(SharpObject Sender, int Mail, SharpPlayer Recipient) : IQuery<SharpMail?>;