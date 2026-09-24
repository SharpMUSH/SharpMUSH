using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class ReleaseTrailingDbrefsCommandHandler(IObjectStore database)
	: ICommandHandler<ReleaseTrailingDbrefsCommand, int>
{
	public async ValueTask<int> Handle(ReleaseTrailingDbrefsCommand request, CancellationToken cancellationToken)
		=> await database.ReleaseTrailingDbrefsAsync(cancellationToken);
}
