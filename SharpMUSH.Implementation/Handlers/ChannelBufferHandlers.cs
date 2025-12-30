using Mediator;
using SharpMUSH.Library.Commands;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Handler for adding messages to channel recall buffers
/// </summary>
public class AddChannelMessageCommandHandler(IChannelBufferService bufferService) 
	: ICommandHandler<AddChannelMessageCommand>
{
	public async ValueTask<Unit> Handle(AddChannelMessageCommand command, CancellationToken cancellationToken)
	{
		await bufferService.AddMessageAsync(command.Message);
		return Unit.Value;
	}
}

/// <summary>
/// Handler for retrieving messages from channel recall buffers
/// </summary>
public class GetChannelMessagesQueryHandler(IChannelBufferService bufferService)
	: IStreamQueryHandler<GetChannelMessagesQuery, SharpChannelMessage>
{
	public IAsyncEnumerable<SharpChannelMessage> Handle(GetChannelMessagesQuery query, CancellationToken cancellationToken)
	{
		return bufferService.GetMessagesAsync(query.ChannelId, query.Count);
	}
}
