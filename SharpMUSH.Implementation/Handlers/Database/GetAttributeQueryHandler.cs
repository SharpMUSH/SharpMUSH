using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAttributeQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetAttributeQuery, SharpAttribute>
{
	public IAsyncEnumerable<SharpAttribute> Handle(GetAttributeQuery request,
		CancellationToken cancellationToken)
		=> database.GetAttributeAsync(request.DBRef, request.Attribute.Select(x => x.ToUpper()).ToArray(), cancellationToken);
}

public class GetLazyAttributeQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetLazyAttributeQuery, LazySharpAttribute>
{
	public IAsyncEnumerable<LazySharpAttribute> Handle(GetLazyAttributeQuery request,
		CancellationToken cancellationToken)
		=> database.GetLazyAttributeAsync(request.DBRef, request.Attribute.Select(x => x.ToUpper()).ToArray(), cancellationToken);
}

public class GetAttributesQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetAttributesQuery, SharpAttribute>
{
	public IAsyncEnumerable<SharpAttribute> Handle(GetAttributesQuery request,
		CancellationToken cancellationToken)
		=> request.Mode switch
		{
			IAttributeService.AttributePatternMode.Exact =>
				database.GetAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken),
			IAttributeService.AttributePatternMode.Wildcard =>
				database.GetAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken),
			IAttributeService.AttributePatternMode.Regex =>
				database.GetAttributesByRegexAsync(
						request.DBRef,
						request.Pattern.ToUpper(), cancellationToken),
			_ => database.GetAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken)
		};
}

public class GetLazyAttributesQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetLazyAttributesQuery, LazySharpAttribute>
{
	public IAsyncEnumerable<LazySharpAttribute> Handle(GetLazyAttributesQuery request,
		CancellationToken cancellationToken)
		=> request.Mode switch
		{
			IAttributeService.AttributePatternMode.Exact =>
				database.GetLazyAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken),
			IAttributeService.AttributePatternMode.Wildcard =>
				database.GetLazyAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken),
			IAttributeService.AttributePatternMode.Regex =>
				database.GetLazyAttributesByRegexAsync(
						request.DBRef,
						request.Pattern.ToUpper(), cancellationToken),
			_ =>
				database.GetLazyAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken)
		};
}