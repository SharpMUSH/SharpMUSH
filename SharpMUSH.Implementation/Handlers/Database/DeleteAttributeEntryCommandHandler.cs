using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class DeleteAttributeEntryCommandHandler(ISharpDatabase database) : ICommandHandler<DeleteAttributeEntryCommand, bool>
{
	public async ValueTask<bool> Handle(DeleteAttributeEntryCommand request, CancellationToken cancellationToken)
	{
		return await database.DeleteAttributeEntryAsync(request.Name, cancellationToken);
	}
}
