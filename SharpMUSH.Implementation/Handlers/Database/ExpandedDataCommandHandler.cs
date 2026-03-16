using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using System.Text.Json;

namespace SharpMUSH.Implementation.Handlers.Database;

public class ExpandedDataCommandHandler(ISharpDatabase database) : ICommandHandler<SetExpandedDataCommand>, ICommandHandler<SetExpandedServerDataCommand>
{
	public async ValueTask<Unit> Handle(SetExpandedDataCommand command, CancellationToken cancellationToken)
	{
		var parsedObject = JsonSerializer.Deserialize<object>(command.Json) ?? new object();
		await database.SetExpandedObjectData(command.SharpObject.Id!, command.TypeName, parsedObject, cancellationToken);
		return Unit.Value;
	}

	public async ValueTask<Unit> Handle(SetExpandedServerDataCommand command, CancellationToken cancellationToken)
	{
		await database.SetExpandedServerData(command.TypeName, command.Object, cancellationToken);
		return Unit.Value;
	}
}
