using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetObjectWarningsCommandHandler : ICommandHandler<SetObjectWarningsCommand>
{
	public async ValueTask<Unit> Handle(SetObjectWarningsCommand request, CancellationToken cancellationToken)
	{
		// Set the warnings on the object in memory
		// TODO: Implement database persistence when ISharpDatabase.SetObjectWarnings is added
		request.Target.Object().Warnings = request.Warnings;
		
		await ValueTask.CompletedTask;
		return Unit.Value;
	}
}
