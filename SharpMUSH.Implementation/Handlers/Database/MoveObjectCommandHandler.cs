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
		// Use the OldContainer supplied by the caller when available; otherwise look it up.
		var oldLocation = request.OldContainer ?? (await request.Target.Location()).Object.DBRef;

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

		return request.Destination.Object.DBRef;
	}
}