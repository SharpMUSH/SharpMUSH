using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class ClearAttributeCommandHandler(IAttributeStore database, IMediator mediator) : ICommandHandler<ClearAttributeCommand, bool>
{
	public async ValueTask<bool> Handle(ClearAttributeCommand request, CancellationToken cancellationToken)
	{
		var watched = await mediator.Send(new GetWatchedAttributesQuery(), cancellationToken);
		return await mediator.AnnounceAsync(
			await database.ClearAttributeAsync(request.DBRef, request.Attribute, watched.Names, cancellationToken),
			watched, request.DBRef, request.Enactor, cancellationToken);
	}
}
