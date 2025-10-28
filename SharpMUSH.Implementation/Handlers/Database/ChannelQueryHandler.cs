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

public class GetChannelListQueryHandler(ISharpDatabase database) : IQueryHandler<GetChannelListQuery, IEnumerable<SharpChannel>>
{
	public async ValueTask<IEnumerable<SharpChannel>> Handle(GetChannelListQuery request, CancellationToken cancellationToken)
	{
		return await database.GetAllChannelsAsync(cancellationToken);
	}
}

public class GetChannelUsersQueryHandler(ISharpDatabase database) : IQueryHandler<GetOnChannelQuery, IEnumerable<SharpChannel>>
{
	public async ValueTask<IEnumerable<SharpChannel>> Handle(GetOnChannelQuery request, CancellationToken cancellationToken)
	{
		return await database.GetMemberChannelsAsync(request.Obj, cancellationToken);
	}
}