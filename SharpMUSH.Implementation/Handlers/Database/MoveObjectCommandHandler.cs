using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;

namespace SharpMUSH.Implementation.Handlers.Database;

public class MoveObjectCommandHandler(INavigationStore database, IPublisher publisher) : ICommandHandler<MoveObjectCommand, DBRef>
{
	public async ValueTask<DBRef> Handle(MoveObjectCommand request, CancellationToken cancellationToken)
	{
		await database.MoveObjectAsync(request.Target, request.Destination, cancellationToken);

		await publisher.Publish(new ObjectMovedNotification(
			request.Target,
			request.Destination,
			request.OldContainer,
			request.Enactor,
			request.IsSilent,
			request.Cause),
			cancellationToken);

		return request.Destination.Object().DBRef;
	}
}