using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetObjectWarningsCommandHandler(ISharpDatabase database) : ICommandHandler<SetObjectWarningsCommand>
{
	public async ValueTask<Unit> Handle(SetObjectWarningsCommand request, CancellationToken cancellationToken)
	{
		// Update the in-memory object
		request.Target.Object().Warnings = request.Warnings;

		// Persist the warnings to the database (best-effort)
		try
		{
			await database.SetObjectWarnings(request.Target, request.Warnings, cancellationToken);
		}
		catch
		{
			// Silently ignore database update errors - the in-memory update already succeeded
			// This maintains backwards compatibility while adding database persistence
		}

		return Unit.Value;
	}
}
