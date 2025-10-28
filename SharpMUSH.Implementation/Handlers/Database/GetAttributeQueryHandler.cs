using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAttributeQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetAttributeQuery, IAsyncEnumerable<SharpAttribute>?>
{
	public async ValueTask<IAsyncEnumerable<SharpAttribute>?> Handle(GetAttributeQuery request,
		CancellationToken cancellationToken)
		=> await database.GetAttributeAsync(request.DBRef, request.Attribute, cancellationToken);
}

public class GetLazyAttributeQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetLazyAttributeQuery, IAsyncEnumerable<LazySharpAttribute>?>
{
	public async ValueTask<IAsyncEnumerable<LazySharpAttribute>?> Handle(GetLazyAttributeQuery request,
		CancellationToken cancellationToken)
		=> await database.GetLazyAttributeAsync(request.DBRef, request.Attribute, cancellationToken);
}

public class GetAttributesQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetAttributesQuery, IAsyncEnumerable<SharpAttribute>?>
{
	public async ValueTask<IAsyncEnumerable<SharpAttribute>?> Handle(GetAttributesQuery request,
		CancellationToken cancellationToken)
		=> request.Mode switch
		{
			IAttributeService.AttributePatternMode.Exact => await database.GetAttributesAsync(request.DBRef, request.Pattern, cancellationToken),
			IAttributeService.AttributePatternMode.Regex => await database.GetAttributesByRegexAsync(request.DBRef,
				request.Pattern, cancellationToken),
			_ => await database.GetAttributesAsync(request.DBRef, request.Pattern, cancellationToken)
		};
}

public class GetLazyAttributesQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetLazyAttributesQuery, IAsyncEnumerable<LazySharpAttribute>?>
{
	public async ValueTask<IAsyncEnumerable<LazySharpAttribute>?> Handle(GetLazyAttributesQuery request,
		CancellationToken cancellationToken)
		=> request.Mode switch
		{
			IAttributeService.AttributePatternMode.Exact => await database.GetLazyAttributesAsync(request.DBRef, request.Pattern, cancellationToken),
			IAttributeService.AttributePatternMode.Regex => await database.GetLazyAttributesByRegexAsync(request.DBRef,
				request.Pattern, cancellationToken),
			_ => await database.GetLazyAttributesAsync(request.DBRef, request.Pattern, cancellationToken)
		};
}