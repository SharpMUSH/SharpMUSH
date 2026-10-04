using System.Runtime.CompilerServices;
using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// The objects near one object, for <c>$</c>-command matching: the object itself, then its contents, then the
/// contents of its location (the object itself left out of that second pass). Composed from the cached
/// <see cref="GetObjectNodeQuery"/> and <see cref="GetContentsQuery"/>, the way <c>GetObjectNodeQueryHandler</c>
/// delegates to the number-keyed node cache: the dispatcher asks this once per command, and the executor, its
/// room and both content lists are what every other reader already holds.
/// </summary>
public class GetNearbyObjectsQueryHandler(IMediator mediator)
	: IStreamQueryHandler<GetNearbyObjectsQuery, AnySharpObject>
{
	public IAsyncEnumerable<AnySharpObject> Handle(GetNearbyObjectsQuery request, CancellationToken cancellationToken)
		=> request.DBRef switch
		{
			DBRef dbref => FromDbRefAsync(dbref, cancellationToken),
			AnySharpObject obj => NearbyAsync(obj, cancellationToken)
		};

	private async IAsyncEnumerable<AnySharpObject> FromDbRefAsync(DBRef dbref, [EnumeratorCancellation] CancellationToken ct)
	{
		if (await mediator.Send(new GetObjectNodeQuery(dbref), ct) is not AnySharpObject self)
		{
			yield break;
		}

		await foreach (var item in NearbyAsync(self, ct))
		{
			yield return item;
		}
	}

	private async IAsyncEnumerable<AnySharpObject> NearbyAsync(AnySharpObject obj, [EnumeratorCancellation] CancellationToken ct)
	{
		var self = obj.Object().DBRef;
		var location = await obj.Where();

		yield return obj;

		await foreach (var item in mediator.CreateStream(new GetContentsQuery(self), ct))
		{
			yield return item.WithRoomOption();
		}

		await foreach (var item in mediator.CreateStream(new GetContentsQuery(location.Object().DBRef), ct))
		{
			if (item.Object().DBRef == self)
			{
				continue;
			}

			yield return item.WithRoomOption();
		}
	}
}
