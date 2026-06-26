using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetObjectWarningsCommandHandler(ISharpDatabase database) : ICommandHandler<SetObjectWarningsCommand>
{
	public async ValueTask<Unit> Handle(SetObjectWarningsCommand request, CancellationToken cancellationToken)
	{
		request.Target.Object().Warnings = request.Warnings;

		try
		{
			await database.SetObjectWarnings(request.Target, request.Warnings, cancellationToken);
		}
		catch
		{
		}

		return Unit.Value;
	}
}
