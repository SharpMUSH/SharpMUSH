using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetChannelQueryHandler(ISharpDatabase database) : IQueryHandler<GetChannelQuery, SharpChannel?>
{
	public async ValueTask<SharpChannel?> Handle(GetChannelQuery request, CancellationToken cancellationToken)
	{
		return await database.GetChannelAsync(request.Name, cancellationToken);
	}
}

public class GetChannelListQueryHandler(ISharpDatabase database) : IQueryHandler<GetChannelListQuery, IAsyncEnumerable<SharpChannel>>
{
	public async ValueTask<IAsyncEnumerable<SharpChannel>> Handle(GetChannelListQuery request, CancellationToken cancellationToken)
	{
		await ValueTask.CompletedTask;
		return database.GetAllChannelsAsync(cancellationToken);
	}
}

public class GetChannelUsersQueryHandler(ISharpDatabase database) : IQueryHandler<GetOnChannelQuery, IAsyncEnumerable<SharpChannel>>
{
	public async ValueTask<IAsyncEnumerable<SharpChannel>> Handle(GetOnChannelQuery request, CancellationToken cancellationToken)
	{
		await ValueTask.CompletedTask;
		return database.GetMemberChannelsAsync(request.Obj, cancellationToken);
	}
}