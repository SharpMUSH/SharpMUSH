using System.Runtime.CompilerServices;
using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// The objects near one object, for the <c>$</c>-command Nearby scope: the object itself, then its contents,
/// then the contents of its location (the object itself left out of that second pass). Read through the cached
/// <see cref="GetContentsQuery"/>, so the dispatcher, which asks once per unmatched command, shares the content
/// lists every other reader already holds.
/// </summary>
public static class NearbyObjects
{
	public static async IAsyncEnumerable<AnySharpObject> ForAsync(IMediator mediator, AnySharpObject obj,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		var self = obj.Object().DBRef;
		var location = await obj.Where();

		yield return obj;

		await foreach (var item in mediator.CreateStream(new GetContentsQuery(self), ct))
		{
			yield return item.WithRoomOption();
		}

		var neighbours = mediator.CreateStream(new GetContentsQuery(location.Object().DBRef), ct)
			.Where(item => item.Object().DBRef != self);
		await foreach (var item in neighbours)
		{
			yield return item.WithRoomOption();
		}
	}
}
