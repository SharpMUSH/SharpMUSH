using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetAttributesCommandHandler(IAttributeStore database) : ICommandHandler<SetAttributesCommand, bool>
{
	public async ValueTask<bool> Handle(SetAttributesCommand request, CancellationToken cancellationToken)
		=> await database.SetAttributesAsync(request.DBRef, request.Attributes, cancellationToken);
}
