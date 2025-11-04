using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetContentsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetContentsQuery, AnySharpContent>
{
	public async IAsyncEnumerable<AnySharpContent> Handle(GetContentsQuery request, CancellationToken cancellationToken)
		=> await request.DBRef.Match(
			async dbRef => await database.GetContentsAsync(dbRef, cancellationToken)
			               ?? AsyncEnumerable.Empty<AnySharpContent>(),
			async obj => await database.GetContentsAsync(obj, cancellationToken)
			             ?? AsyncEnumerable.Empty<AnySharpContent>()
		);
}