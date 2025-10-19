using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetContentsQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetContentsQuery, IAsyncEnumerable<AnySharpContent>?>
{
	public async ValueTask<IAsyncEnumerable<AnySharpContent>?> Handle(GetContentsQuery request, CancellationToken cancellationToken)
		=> await request.DBRef.Match(
			async dbRef => await database.GetContentsAsync(dbRef),
			async obj => await database.GetContentsAsync(obj)
			);
}
