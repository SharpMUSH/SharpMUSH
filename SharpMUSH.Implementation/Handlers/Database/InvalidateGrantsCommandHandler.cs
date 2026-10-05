using Mediator;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class InvalidateGrantsCommandHandler : ICommandHandler<InvalidateGrantsCommand, bool>
{
	public ValueTask<bool> Handle(InvalidateGrantsCommand command, CancellationToken cancellationToken) => ValueTask.FromResult(true);
}
