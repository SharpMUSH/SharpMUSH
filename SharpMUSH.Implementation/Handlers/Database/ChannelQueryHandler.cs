using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetChannelQueryHandler(ISharpDatabase database) : IQueryHandler<GetChannelQuery, SharpChannel?>
{
	public async ValueTask<SharpChannel?> Handle(GetChannelQuery request, CancellationToken cancellationToken) 
		=> await database.GetChannelAsync(request.Name, cancellationToken);
}

public class GetChannelListQueryHandler(ISharpDatabase database) : IStreamQueryHandler<GetChannelListQuery, SharpChannel>
{
	public IAsyncEnumerable<SharpChannel> Handle(GetChannelListQuery request, CancellationToken cancellationToken) 
		=> database.GetAllChannelsAsync(cancellationToken);
}

public class GetChannelUsersQueryHandler(ISharpDatabase database) : IStreamQueryHandler<GetOnChannelQuery, SharpChannel>
{
	public IAsyncEnumerable<SharpChannel> Handle(GetOnChannelQuery request, CancellationToken cancellationToken) 
		=> database.GetMemberChannelsAsync(request.Obj, cancellationToken);
}