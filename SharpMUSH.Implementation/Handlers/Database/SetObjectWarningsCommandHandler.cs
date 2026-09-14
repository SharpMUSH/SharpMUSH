using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetObjectWarningsCommandHandler(IObjectStore database) : ICommandHandler<SetObjectWarningsCommand>
{
	public async ValueTask<Unit> Handle(SetObjectWarningsCommand request, CancellationToken cancellationToken)
	{
		request.Target.Object().Warnings = request.Warnings;

		await database.SetObjectWarnings(request.Target, request.Warnings, cancellationToken);

		return Unit.Value;
	}
}
