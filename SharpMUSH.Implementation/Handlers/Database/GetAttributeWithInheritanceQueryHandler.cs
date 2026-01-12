using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Handler for GetAttributeWithInheritanceQuery that retrieves an attribute
/// with full parent/zone inheritance resolution in a single database call.
/// </summary>
public class GetAttributeWithInheritanceQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetAttributeWithInheritanceQuery, AttributeWithInheritance?>
{
	public async ValueTask<AttributeWithInheritance?> Handle(
		GetAttributeWithInheritanceQuery request,
		CancellationToken cancellationToken)
	{
		return await database.GetAttributeWithInheritanceAsync(
			request.DBRef,
			request.Attribute.Select(x => x.ToUpper()).ToArray(),
			request.CheckParent,
			cancellationToken);
	}
}

/// <summary>
/// Handler for GetLazyAttributeWithInheritanceQuery that retrieves an attribute
/// with full parent/zone inheritance resolution in a single database call (lazy version).
/// </summary>
public class GetLazyAttributeWithInheritanceQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetLazyAttributeWithInheritanceQuery, LazyAttributeWithInheritance?>
{
	public async ValueTask<LazyAttributeWithInheritance?> Handle(
		GetLazyAttributeWithInheritanceQuery request,
		CancellationToken cancellationToken)
	{
		return await database.GetLazyAttributeWithInheritanceAsync(
			request.DBRef,
			request.Attribute.Select(x => x.ToUpper()).ToArray(),
			request.CheckParent,
			cancellationToken);
	}
}
