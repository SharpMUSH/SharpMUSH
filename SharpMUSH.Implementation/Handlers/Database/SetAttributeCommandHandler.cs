using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetAttributeCommandHandler(ISharpDatabase database) : ICommandHandler<SetAttributeCommand, bool>
{
		public async ValueTask<bool> Handle(SetAttributeCommand request, CancellationToken cancellationToken)
		{
				return await database.SetAttributeAsync(request.DBRef, request.Attribute, request.Value, request.Owner);
		}
}