using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class WipeAttributeCommandHandler(IAttributeStore database, IPublisher publisher) : ICommandHandler<WipeAttributeCommand, bool>
{
	public async ValueTask<bool> Handle(WipeAttributeCommand request, CancellationToken cancellationToken)
		=> await publisher.AnnounceAsync(
			await database.WipeAttributeAsync(request.DBRef, request.Attribute, cancellationToken),
			request.DBRef, request.Enactor, cancellationToken);
}
