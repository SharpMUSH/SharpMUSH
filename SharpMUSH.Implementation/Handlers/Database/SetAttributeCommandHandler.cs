using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetAttributeCommandHandler(IAttributeStore database, IPublisher publisher) : ICommandHandler<SetAttributeCommand, bool>
{
	public async ValueTask<bool> Handle(SetAttributeCommand request, CancellationToken cancellationToken)
		=> await publisher.AnnounceAsync(
			await database.SetAttributeAsync(request.DBRef, request.Attribute, request.Value, request.Owner, cancellationToken),
			request.DBRef, request.Enactor, cancellationToken);
}
