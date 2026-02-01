using System.Runtime.CompilerServices;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetExitsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetExitsQuery, SharpExit>
{
	public async IAsyncEnumerable<SharpExit> Handle(GetExitsQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var result = await request.DBRef.Match<ValueTask<IAsyncEnumerable<SharpExit>?>>(
			dbref => database.GetExitsAsync(dbref, cancellationToken),
			obj => ValueTask.FromResult<IAsyncEnumerable<SharpExit>?>(database.GetExitsAsync(obj, cancellationToken)));
		
		if (result != null)
		{
			await foreach (var item in result.WithCancellation(cancellationToken))
			{
				yield return item;
			}
		}
	}
}