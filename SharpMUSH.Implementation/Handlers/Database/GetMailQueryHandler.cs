using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetMailQueryHandler(IMailStore database) : IQueryHandler<GetMailQuery, SharpMail?>
{
	public async ValueTask<SharpMail?> Handle(GetMailQuery query, CancellationToken cancellationToken) =>
		await database.GetIncomingMailAsync(query.Player, query.Folder, query.Mail, cancellationToken);
}

public class GetMailListQueryHandler(IMailStore database)
	: IStreamQueryHandler<GetMailListQuery, SharpMail>
{
	public IAsyncEnumerable<SharpMail>
		Handle(GetMailListQuery query, CancellationToken cancellationToken)
		=> database.GetIncomingMailsAsync(query.Player, query.Folder, cancellationToken);
}

public class GetAllMailListQueryHandler(IMailStore database)
	: IStreamQueryHandler<GetAllMailListQuery, SharpMail>
{
	public IAsyncEnumerable<SharpMail>
		Handle(GetAllMailListQuery query, CancellationToken cancellationToken)
		=> database.GetAllIncomingMailsAsync(query.Player, cancellationToken);
}

public class GetSentMailQueryHandler(IMailStore database) : IQueryHandler<GetSentMailQuery, SharpMail?>
{
	public async ValueTask<SharpMail?> Handle(GetSentMailQuery query, CancellationToken cancellationToken) =>
		await database.GetSentMailAsync(query.Sender, query.Recipient, query.Mail, cancellationToken);
}

public class GetSentMailListQueryHandler(IMailStore database)
	: IStreamQueryHandler<GetSentMailListQuery, SharpMail>
{
	public IAsyncEnumerable<SharpMail>
		Handle(GetSentMailListQuery query, CancellationToken cancellationToken) =>
		database.GetSentMailsAsync(query.Sender, query.Recipient, cancellationToken);
}

public class GetAllSentMailListQueryHandler(IMailStore database)
	: IStreamQueryHandler<GetAllSentMailListQuery, SharpMail>
{
	public IAsyncEnumerable<SharpMail>
		Handle(GetAllSentMailListQuery query, CancellationToken cancellationToken) =>
		database.GetAllSentMailsAsync(query.Sender, cancellationToken);
}

public class GetAllSystemMailQueryHandler(IMailStore database)
	: IStreamQueryHandler<GetAllSystemMailQuery, SharpMail>
{
	public IAsyncEnumerable<SharpMail>
		Handle(GetAllSystemMailQuery query, CancellationToken cancellationToken) =>
		database.GetAllSystemMailAsync(cancellationToken);
}