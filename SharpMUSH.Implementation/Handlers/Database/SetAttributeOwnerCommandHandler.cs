using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetAttributeOwnerCommandHandler(IAttributeStore database) : ICommandHandler<SetAttributeOwnerCommand, bool>
{
	public ValueTask<bool> Handle(SetAttributeOwnerCommand request, CancellationToken cancellationToken)
		=> database.SetAttributeOwnerAsync(request.DBRef, request.Attribute, request.Owner, cancellationToken);
}
