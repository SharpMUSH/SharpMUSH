using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class ClearAttributeCommandHandler(IAttributeStore database, IPublisher publisher) : ICommandHandler<ClearAttributeCommand, bool>
{
	public async ValueTask<bool> Handle(ClearAttributeCommand request, CancellationToken cancellationToken)
		=> await publisher.AnnounceAsync(
			await database.ClearAttributeAsync(request.DBRef, request.Attribute, cancellationToken),
			request.DBRef, request.Enactor, cancellationToken);
}
