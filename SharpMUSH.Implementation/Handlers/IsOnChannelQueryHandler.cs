using Mediator;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Handler for IsOnChannelQuery - checks if an object is a member of a channel.
/// This is used by channel locks in the Boolean Expression Parser.
/// For players, checks direct membership. For objects, checks owner's membership.
/// </summary>
public class IsOnChannelQueryHandler(IMediator mediator) : IQueryHandler<IsOnChannelQuery, bool>
{
	public async ValueTask<bool> Handle(IsOnChannelQuery request, CancellationToken cancellationToken)
	{
		// Check if the object (or its owner) is on the channel
		// GetOnChannelQuery returns all channels the object is on
		var requestedChannelName = request.ChannelName;

		await foreach (var memberChannel in mediator.CreateStream(new GetOnChannelQuery(request.Object), cancellationToken))
		{
			// Compare channel names (MString comparison)
			if (memberChannel.Name.ToString().Equals(requestedChannelName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		// Object is not on the channel (or channel doesn't exist)
		return false;
	}
}
