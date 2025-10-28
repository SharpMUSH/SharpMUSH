using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetMailQueryHandler(ISharpDatabase database) : IQueryHandler<GetMailQuery, SharpMail?>
{
	public async ValueTask<SharpMail?> Handle(GetMailQuery query, CancellationToken cancellationToken) =>
		await database.GetIncomingMailAsync(query.Player, query.Folder, query.Mail, cancellationToken);
}

public class GetMailListQueryHandler(ISharpDatabase database) : IQueryHandler<GetMailListQuery, IEnumerable<SharpMail>>
{
	public async ValueTask<IEnumerable<SharpMail>>
		Handle(GetMailListQuery query, CancellationToken cancellationToken) =>
		await database.GetIncomingMailsAsync(query.Player, query.Folder, cancellationToken)
			.ToArrayAsync(cancellationToken: cancellationToken);
}

public class GetAllMailListQueryHandler(ISharpDatabase database) : IQueryHandler<GetAllMailListQuery, IEnumerable<SharpMail>>
{
	public async ValueTask<IEnumerable<SharpMail>>
		Handle(GetAllMailListQuery query, CancellationToken cancellationToken) =>
		await database.GetAllIncomingMailsAsync(query.Player, cancellationToken)
			.ToArrayAsync(cancellationToken: cancellationToken);
}

public class GetSentMailQueryHandler(ISharpDatabase database) : IQueryHandler<GetSentMailQuery, SharpMail?>
{
	public async ValueTask<SharpMail?> Handle(GetSentMailQuery query, CancellationToken cancellationToken) =>
		await database.GetSentMailAsync(query.Sender, query.Recipient, query.Mail, cancellationToken);
}

public class GetSentMailListQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetSentMailListQuery, IEnumerable<SharpMail>>
{
	public async ValueTask<IEnumerable<SharpMail>>
		Handle(GetSentMailListQuery query, CancellationToken cancellationToken) =>
		await database.GetSentMailsAsync(query.Sender, query.Recipient, cancellationToken)
			.ToArrayAsync(cancellationToken: cancellationToken);
}

public class GetAllSentMailListQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetAllSentMailListQuery, IEnumerable<SharpMail>>
{
	public async ValueTask<IEnumerable<SharpMail>>
		Handle(GetAllSentMailListQuery query, CancellationToken cancellationToken) =>
		await database.GetAllSentMailsAsync(query.Sender, cancellationToken)
			.ToArrayAsync(cancellationToken: cancellationToken);
}