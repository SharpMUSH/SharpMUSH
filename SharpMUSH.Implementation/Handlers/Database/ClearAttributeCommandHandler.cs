using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class ClearAttributeCommandHandler(ISharpDatabase database) : ICommandHandler<ClearAttributeCommand, bool>
{
		public async ValueTask<bool> Handle(ClearAttributeCommand request, CancellationToken cancellationToken)
		{
				return await database.ClearAttributeAsync(request.DBRef, request.Attribute);
		}
}