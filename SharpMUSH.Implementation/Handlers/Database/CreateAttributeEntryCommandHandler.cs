using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateAttributeEntryCommandHandler(ISharpDatabase database) : ICommandHandler<CreateAttributeEntryCommand, SharpAttributeEntry?>
{
	public async ValueTask<SharpAttributeEntry?> Handle(CreateAttributeEntryCommand request, CancellationToken cancellationToken)
	{
		return await database.CreateOrUpdateAttributeEntryAsync(request.Name, request.DefaultFlags, request.Limit, request.EnumValues, cancellationToken);
	}
}
