using System.Runtime.CompilerServices;
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
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var result = await database.GetAttributeAsync(request.DBRef, request.Attribute.Select(x => x.ToUpper()).ToArray(), cancellationToken);
		if (result != null)
		{
			await foreach (var item in result.WithCancellation(cancellationToken))
			{
				yield return item;
			}
		}
	}
}

public class GetLazyAttributeQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetLazyAttributeQuery, LazySharpAttribute>
{
	public IAsyncEnumerable<LazySharpAttribute> Handle(GetLazyAttributeQuery request,
		CancellationToken cancellationToken)
		=> database.GetLazyAttributeAsync(request.DBRef, request.Attribute.Select(x => x.ToUpper()).ToArray(), cancellationToken)
		   ?? AsyncEnumerable.Empty<LazySharpAttribute>();
}

public class GetAttributesQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetAttributesQuery, SharpAttribute>
{
	public async IAsyncEnumerable<SharpAttribute> Handle(GetAttributesQuery request,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var result = request.Mode switch
		{
			IAttributeService.AttributePatternMode.Exact =>
				await database.GetAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken),
			IAttributeService.AttributePatternMode.Wildcard =>
				await database.GetAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken),
			IAttributeService.AttributePatternMode.Regex =>
				await database.GetAttributesByRegexAsync(
						request.DBRef,
						request.Pattern.ToUpper(), cancellationToken),
			_ => await database.GetAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken)
		};
		
		if (result != null)
		{
			await foreach (var item in result.WithCancellation(cancellationToken))
			{
				yield return item;
			}
		}
	}
}

public class GetLazyAttributesQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetLazyAttributesQuery, LazySharpAttribute>
{
	public async IAsyncEnumerable<LazySharpAttribute> Handle(GetLazyAttributesQuery request,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var result = request.Mode switch
		{
			IAttributeService.AttributePatternMode.Exact =>
				await database.GetLazyAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken),
			IAttributeService.AttributePatternMode.Wildcard =>
				await database.GetLazyAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken),
			IAttributeService.AttributePatternMode.Regex =>
				await database.GetLazyAttributesByRegexAsync(
						request.DBRef,
						request.Pattern.ToUpper(), cancellationToken),
			_ =>
				await database.GetLazyAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken)
		};
		
		if (result != null)
		{
			await foreach (var item in result.WithCancellation(cancellationToken))
			{
				yield return item;
			}
		}
	}
}