using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Handler for GetAttributeWithInheritanceQuery: PennMUSH's <c>atr_get_with_parent</c> in a single
/// database call. Returns the complete attribute path (FOO → BAR → BAZ).
/// </summary>
public class GetAttributeWithInheritanceQueryHandler(IAttributeStore database)
	: IStreamQueryHandler<GetAttributeWithInheritanceQuery, AttributeWithInheritance>
{
	public IAsyncEnumerable<AttributeWithInheritance> Handle(
		GetAttributeWithInheritanceQuery request,
		CancellationToken cancellationToken)
	{
		return database.GetAttributeWithInheritanceAsync(
			request.DBRef,
			Array.ConvertAll(request.Attribute, x => x.ToUpper()),
			request.CheckParent,
			request.Walk,
			cancellationToken);
	}
}

/// <summary>
/// Handler for GetLazyAttributeWithInheritanceQuery: the same walk as
/// <see cref="GetAttributeWithInheritanceQueryHandler"/>, with values loaded on demand.
/// </summary>
public class GetLazyAttributeWithInheritanceQueryHandler(IAttributeStore database)
	: IStreamQueryHandler<GetLazyAttributeWithInheritanceQuery, LazyAttributeWithInheritance>
{
	public IAsyncEnumerable<LazyAttributeWithInheritance> Handle(
		GetLazyAttributeWithInheritanceQuery request,
		CancellationToken cancellationToken)
	{
		return database.GetLazyAttributeWithInheritanceAsync(
			request.DBRef,
			Array.ConvertAll(request.Attribute, x => x.ToUpper()),
			request.CheckParent,
			request.Walk,
			cancellationToken);
	}
}
