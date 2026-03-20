using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class WipeAttributeCommandHandler(ISharpDatabase database) : ICommandHandler<WipeAttributeCommand, bool>
{
	public async ValueTask<bool> Handle(WipeAttributeCommand request, CancellationToken cancellationToken)
	{
		return await database.WipeAttributeAsync(request.DBRef, request.Attribute, cancellationToken);
	}
}
