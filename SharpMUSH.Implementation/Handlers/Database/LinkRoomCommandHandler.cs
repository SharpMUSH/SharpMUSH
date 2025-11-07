using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class LinkRoomCommandHandler(ISharpDatabase database) : ICommandHandler<LinkRoomCommand, bool>
{
	public async ValueTask<bool> Handle(LinkRoomCommand request, CancellationToken cancellationToken)
		=> await database.LinkRoomAsync(request.Room, request.Location, cancellationToken);
}

public class UnlinkRoomCommandHandler(ISharpDatabase database) : ICommandHandler<UnlinkRoomCommand, bool>
{
	public async ValueTask<bool> Handle(UnlinkRoomCommand request, CancellationToken cancellationToken)
		=> await database.UnlinkRoomAsync(request.Room, cancellationToken);
}
