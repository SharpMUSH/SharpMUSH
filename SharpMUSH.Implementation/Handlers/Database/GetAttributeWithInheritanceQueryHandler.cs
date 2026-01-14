using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Handler for GetAttributeWithInheritanceQuery that retrieves an attribute
/// with full parent/zone inheritance resolution in a single database call.
/// Returns the complete attribute path (FOO → BAR → BAZ) as a stream of AttributeWithInheritance instances.
/// </summary>
public class GetAttributeWithInheritanceQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetAttributeWithInheritanceQuery, AttributeWithInheritance?>
{
	public IAsyncEnumerable<AttributeWithInheritance?> Handle(
		GetAttributeWithInheritanceQuery request,
		CancellationToken cancellationToken)
	{
		return database.GetAttributeWithInheritanceAsync(
			request.DBRef,
			request.Attribute.Select(x => x.ToUpper()).ToArray(),
			request.CheckParent,
			cancellationToken);
	}
}

/// <summary>
/// Handler for GetLazyAttributeWithInheritanceQuery that retrieves an attribute
/// with full parent/zone inheritance resolution in a single database call (lazy version).
/// Returns the complete attribute path (FOO → BAR → BAZ) as a stream of LazyAttributeWithInheritance instances.
/// </summary>
public class GetLazyAttributeWithInheritanceQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetLazyAttributeWithInheritanceQuery, LazyAttributeWithInheritance?>
{
	public IAsyncEnumerable<LazyAttributeWithInheritance?> Handle(
		GetLazyAttributeWithInheritanceQuery request,
		CancellationToken cancellationToken)
	{
		return database.GetLazyAttributeWithInheritanceAsync(
			request.DBRef,
			request.Attribute.Select(x => x.ToUpper()).ToArray(),
			request.CheckParent,
			cancellationToken);
	}
}
