using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class LinkExitCommandHandler(ISharpDatabase database) : ICommandHandler<LinkExitCommand, bool>
{
	public async ValueTask<bool> Handle(LinkExitCommand request, CancellationToken cancellationToken)
		=> await database.LinkExitAsync(request.Exit, request.Location);
}

public class UnlinkExitCommandHandler(ISharpDatabase database) : ICommandHandler<UnlinkExitCommand, bool>
{
	public async ValueTask<bool> Handle(UnlinkExitCommand request, CancellationToken cancellationToken)
		=> await database.UnlinkExitAsync(request.Exit);
}