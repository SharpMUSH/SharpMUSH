using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class ExpandedDataCommandHandler(ISharpDatabase database) : ICommandHandler<SetExpandedDataCommand>, ICommandHandler<SetExpandedServerDataCommand>
{
	public async ValueTask<Unit> Handle(SetExpandedDataCommand command, CancellationToken cancellationToken)
	{
		var dynamicObject = System.Text.Json.JsonSerializer.Deserialize<dynamic>(command.Json);
		await database.SetExpandedObjectData(command.SharpObject.Id!, command.TypeName, dynamicObject);
		return Unit.Value;
	}

	public async ValueTask<Unit> Handle(SetExpandedServerDataCommand command, CancellationToken cancellationToken)
	{
		await database.SetExpandedServerData(command.TypeName, command.Json);
		return Unit.Value;
	}
}
