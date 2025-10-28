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

public class GetMailListQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetMailListQuery, IAsyncEnumerable<SharpMail>>
{
	public async ValueTask<IAsyncEnumerable<SharpMail>>
		Handle(GetMailListQuery query, CancellationToken cancellationToken)
	{
		await ValueTask.CompletedTask;
		return database.GetIncomingMailsAsync(query.Player, query.Folder, cancellationToken);
	}
}

public class GetAllMailListQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetAllMailListQuery, IAsyncEnumerable<SharpMail>>
{
	public async ValueTask<IAsyncEnumerable<SharpMail>>
		Handle(GetAllMailListQuery query, CancellationToken cancellationToken)
	{
		await ValueTask.CompletedTask;
		return database.GetAllIncomingMailsAsync(query.Player, cancellationToken);
	}
}

public class GetSentMailQueryHandler(ISharpDatabase database) : IQueryHandler<GetSentMailQuery, SharpMail?>
{
	public async ValueTask<SharpMail?> Handle(GetSentMailQuery query, CancellationToken cancellationToken) =>
		await database.GetSentMailAsync(query.Sender, query.Recipient, query.Mail, cancellationToken);
}

public class GetSentMailListQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetSentMailListQuery, IAsyncEnumerable<SharpMail>>
{
	public async ValueTask<IAsyncEnumerable<SharpMail>>
		Handle(GetSentMailListQuery query, CancellationToken cancellationToken)
	{
		await ValueTask.CompletedTask;
		return database.GetSentMailsAsync(query.Sender, query.Recipient, cancellationToken);
	}
}

public class GetAllSentMailListQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetAllSentMailListQuery, IAsyncEnumerable<SharpMail>>
{
	public async ValueTask<IAsyncEnumerable<SharpMail>>
		Handle(GetAllSentMailListQuery query, CancellationToken cancellationToken)
	{
		await ValueTask.CompletedTask;
		return database.GetAllSentMailsAsync(query.Sender, cancellationToken);
	}
}