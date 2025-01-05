using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class LinkExitCommandHandler(ISharpDatabase database) : ICommandHandler<LinkExitCommand, bool>
{
	public async ValueTask<bool> Handle(LinkExitCommand request, CancellationToken cancellationToken)
		=> await database.LinkExitAsync(request.Exit, request.Location);
}