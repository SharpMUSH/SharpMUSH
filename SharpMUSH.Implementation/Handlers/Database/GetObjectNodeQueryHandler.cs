using Mediator;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetObjectNodeQueryHandler(IMediator mediator)
	: IQueryHandler<GetObjectNodeQuery, AnyOptionalSharpObject>
{
	public async ValueTask<AnyOptionalSharpObject> Handle(GetObjectNodeQuery request, CancellationToken cancellationToken)
	{
		// The cached load is keyed by NUMBER only, so a bare "#N" and a full "#N:creation" reference share
		// it and every mutation (which invalidates by number) clears it.
		var node = await mediator.Send(new GetObjectNodeByNumberQuery(request.DBRef.Number), cancellationToken);

		// Objid (recycle) check, applied OUTSIDE the cache so it runs on every request: a full objid must
		// match the live object's creation time, else it resolves to nothing (e.g. a stale, recycled objid).
		if (request.DBRef.CreationMilliseconds is null || node.IsNone)
		{
			return node;
		}

		return node.Object()?.DBRef.CreationMilliseconds == request.DBRef.CreationMilliseconds
			? node
			: new None();
	}
}

public class GetObjectNodeByNumberQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetObjectNodeByNumberQuery, AnyOptionalSharpObject>
{
	public async ValueTask<AnyOptionalSharpObject> Handle(GetObjectNodeByNumberQuery request, CancellationToken cancellationToken)
		=> await database.GetObjectNodeAsync(new DBRef(request.Number), cancellationToken);
}

public class GetBaseObjectNodeQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetBaseObjectNodeQuery, SharpObject?>
{
	public async ValueTask<SharpObject?> Handle(GetBaseObjectNodeQuery request, CancellationToken cancellationToken)
		 => await database.GetBaseObjectNodeAsync(request.DBRef, cancellationToken);
}
