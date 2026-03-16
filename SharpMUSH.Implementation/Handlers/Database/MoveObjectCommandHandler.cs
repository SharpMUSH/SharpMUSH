using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;

namespace SharpMUSH.Implementation.Handlers.Database;

public class MoveObjectCommandHandler(ISharpDatabase database, IPublisher publisher) : ICommandHandler<MoveObjectCommand, DBRef>
{
	public async ValueTask<DBRef> Handle(MoveObjectCommand request, CancellationToken cancellationToken)
	{
		// Capture old location before move for event notification
		var oldLocation = await request.Target.Match<ValueTask<DBRef>>(
			async player => await player.Location.WithCancellation(cancellationToken).ContinueWith(t => t.Result.Object().DBRef, cancellationToken),
			async exit => await exit.Location.WithCancellation(cancellationToken).ContinueWith(t => t.Result.Object().DBRef, cancellationToken),
			async thing => await thing.Location.WithCancellation(cancellationToken).ContinueWith(t => t.Result.Object().DBRef, cancellationToken));

		await database.MoveObjectAsync(request.Target, request.Destination, cancellationToken);

		// Publish notification for event system
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