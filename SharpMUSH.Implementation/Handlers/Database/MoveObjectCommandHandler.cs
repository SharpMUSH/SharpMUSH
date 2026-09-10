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
		var oldLocation = request.OldContainer ?? await request.Target.Match<ValueTask<DBRef>>(
			async player => (await player.Location.WithCancellation(cancellationToken)).Object().DBRef,
			async exit => (await exit.Location.WithCancellation(cancellationToken)).Object().DBRef,
			async thing => (await thing.Location.WithCancellation(cancellationToken)).Object().DBRef);

		await database.MoveObjectAsync(request.Target, request.Destination, cancellationToken);

		await publisher.Publish(new ObjectMovedNotification(
			request.Target,
			request.Destination,
			oldLocation,
			request.Enactor,
			request.IsSilent,
			request.Cause),
			cancellationToken);

		return request.Destination.Match(
			player => player.Object.DBRef,
			room => room.Object.DBRef,
			thing => thing.Object.DBRef);
	}
}