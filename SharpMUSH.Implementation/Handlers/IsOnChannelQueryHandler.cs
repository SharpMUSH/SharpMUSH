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
		// Get the channel by name
		var channel = await mediator.Send(new GetChannelQuery(request.ChannelName), cancellationToken);
		
		if (channel == null)
		{
			// Channel doesn't exist
			return false;
		}
		
		// Check if the object (or its owner) is on the channel
		// GetOnChannelQuery returns all channels the object is on
		await foreach (var memberChannel in mediator.CreateStream(new GetOnChannelQuery(request.Object), cancellationToken))
		{
			// If we find a channel with matching name, the object is on it
			if (memberChannel.Name.ToString().Equals(request.ChannelName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		
		return false;
	}
}
