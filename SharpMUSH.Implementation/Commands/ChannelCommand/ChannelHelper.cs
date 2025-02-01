using Mediator;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public class ChannelOrError : OneOfBase<SharpChannel, Error<CallState>>
{
	public ChannelOrError(SharpChannel channel) : base(channel)
	{
	}

	public ChannelOrError(Error<CallState> error) : base(error)
	{
	}

	public bool IsError => IsT1;
	public SharpChannel AsChannel => AsT0;
	public Error<CallState> AsError => AsT1;
}

public static class ChannelHelper
{
	public static async ValueTask<ChannelOrError> GetChannelOrError(
		IMUSHCodeParser parser,
		MString channelName,
		bool notify)
	{
		var channel = await parser.Mediator.Send(new GetChannelQuery(channelName.ToPlainText()));

		switch (channel, notify)
		{
			case (null, true):
			{
				await parser.NotifyService.Notify((await parser.CurrentState.ExecutorObject(parser.Mediator)).Known(),
					"Channel not found.");
				return new ChannelOrError(new Error<CallState>(new CallState("#-1 Channel not found.")));
			}
			case (null, false):
			{
				return new ChannelOrError(new Error<CallState>(new CallState("#-1 Channel not found.")));
			}
			case ({} foundChannel, _):
			{
				return new ChannelOrError(foundChannel);
			}
		}
	}
}