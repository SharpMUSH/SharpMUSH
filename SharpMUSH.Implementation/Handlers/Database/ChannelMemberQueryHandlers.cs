using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetChannelMemberStatusQueryHandler(IChannelStore database)
	: IQueryHandler<GetChannelMemberStatusQuery, Found<SharpChannelStatus>>
{
	public async ValueTask<Found<SharpChannelStatus>> Handle(GetChannelMemberStatusQuery request, CancellationToken cancellationToken)
		=> await database.GetChannelMemberStatusAsync(request.Channel, request.Member, cancellationToken);
}

public class GetChannelMemberCountQueryHandler(IChannelStore database) : IQueryHandler<GetChannelMemberCountQuery, int>
{
	public async ValueTask<int> Handle(GetChannelMemberCountQuery request, CancellationToken cancellationToken)
		=> await database.GetChannelMemberCountAsync(request.Channel, cancellationToken);
}
