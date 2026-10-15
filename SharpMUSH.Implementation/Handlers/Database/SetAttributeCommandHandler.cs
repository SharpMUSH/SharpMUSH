using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetAttributeCommandHandler(IAttributeStore database, IMediator mediator) : ICommandHandler<SetAttributeCommand, bool>
{
	public async ValueTask<bool> Handle(SetAttributeCommand request, CancellationToken cancellationToken)
	{
		var watched = await mediator.Send(new GetWatchedAttributesQuery(), cancellationToken);
		return await mediator.AnnounceAsync(
			await database.SetAttributeAsync(request.DBRef, request.Attribute, request.Value, request.Owner, watched.Names, cancellationToken),
			watched, request.DBRef, request.Enactor, cancellationToken);
	}
}
