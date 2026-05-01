using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;

namespace SharpMUSH.Implementation.Handlers.Database;

public class MoveObjectCommandHandler(ISharpDatabase database, IPublisher publisher) : ICommandHandler<MoveObjectCommand, DBRef>
{
	public async ValueTask<DBRef> Handle(MoveObjectCommand request, CancellationToken cancellationToken)
	{
		// Use the OldContainer supplied by the caller when available; otherwise look it up.
		var oldLocation = request.OldContainer ?? (request.Target.Value switch { SharpPlayer p => (await p.Location.WithCancellation(cancellationToken)).Object().DBRef, SharpExit e => (await e.Location.WithCancellation(cancellationToken)).Object().DBRef, SharpThing t => (await t.Location.WithCancellation(cancellationToken)).Object().DBRef, _ => throw new InvalidOperationException() });

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

		return request.Destination.Value switch { SharpPlayer p => p.Object.DBRef, SharpRoom r => r.Object.DBRef, SharpThing t => t.Object.DBRef, _ => throw new InvalidOperationException() };
	}
}