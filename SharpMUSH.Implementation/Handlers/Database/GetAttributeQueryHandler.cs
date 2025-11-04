using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAttributeQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetAttributeQuery, SharpAttribute>
{
	public async IAsyncEnumerable<SharpAttribute> Handle(GetAttributeQuery request,
		CancellationToken cancellationToken)
		=> await database.GetAttributeAsync(request.DBRef, request.Attribute, cancellationToken) 
		   ?? AsyncEnumerable.Empty<SharpAttribute>();
}

public class GetLazyAttributeQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetLazyAttributeQuery, LazySharpAttribute>
{
	public IAsyncEnumerable<LazySharpAttribute> Handle(GetLazyAttributeQuery request,
		CancellationToken cancellationToken)
		=> database.GetLazyAttributeAsync(request.DBRef, request.Attribute, cancellationToken)
		   ?? AsyncEnumerable.Empty<LazySharpAttribute>();
}

public class GetAttributesQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetAttributesQuery, SharpAttribute>
{
	public async IAsyncEnumerable<SharpAttribute> Handle(GetAttributesQuery request,
		CancellationToken cancellationToken)
		=> request.Mode switch
		{
			IAttributeService.AttributePatternMode.Exact =>
				await database.GetAttributesAsync(request.DBRef, request.Pattern, cancellationToken)
				?? AsyncEnumerable.Empty<SharpAttribute>(),
			IAttributeService.AttributePatternMode.Regex =>
				await database.GetAttributesByRegexAsync(
					request.DBRef,
					request.Pattern, cancellationToken)
				?? AsyncEnumerable.Empty<SharpAttribute>(),
			_ => await database.GetAttributesAsync(request.DBRef, request.Pattern, cancellationToken)
			     ?? AsyncEnumerable.Empty<SharpAttribute>()
		};
}

public class GetLazyAttributesQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetLazyAttributesQuery, LazySharpAttribute>
{
	public async IAsyncEnumerable<LazySharpAttribute> Handle(GetLazyAttributesQuery request,
		CancellationToken cancellationToken)
		=> request.Mode switch
		{
			IAttributeService.AttributePatternMode.Exact =>
				await database.GetLazyAttributesAsync(request.DBRef, request.Pattern, cancellationToken)
				?? AsyncEnumerable.Empty<LazySharpAttribute>(),
			IAttributeService.AttributePatternMode.Regex =>
				await database.GetLazyAttributesByRegexAsync(
					request.DBRef,
					request.Pattern, cancellationToken)
				?? AsyncEnumerable.Empty<LazySharpAttribute>(),
			_ =>
				await database.GetLazyAttributesAsync(request.DBRef, request.Pattern, cancellationToken)
				?? AsyncEnumerable.Empty<LazySharpAttribute>()
		};
}