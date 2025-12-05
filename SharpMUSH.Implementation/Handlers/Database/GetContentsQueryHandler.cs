using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetContentsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetContentsQuery, AnySharpContent>
{
	public async IAsyncEnumerable<AnySharpContent> Handle(GetContentsQuery request, CancellationToken cancellationToken)
	{
		switch (request.DBRef)
		{
			case { IsT0: true, AsT0: var dbref }:
			{
				await foreach (var item in (await database.GetContentsAsync(dbref, cancellationToken))
				               .WithCancellation(cancellationToken))
				{
					yield return item;
				}

				break;
			}
			case { IsT1 : true, AsT1: var obj }:
			{
				await foreach (var item in (await database.GetContentsAsync(obj, cancellationToken))
				               .WithCancellation(cancellationToken))
				{
					yield return item;
				}

				break;
			}
			default: throw new ArgumentException(nameof(request.DBRef));
		}
	}
}