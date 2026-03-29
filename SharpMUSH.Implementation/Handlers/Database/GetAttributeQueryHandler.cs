using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
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
	{
		if (!request.CheckParents)
		{
			return GetAttributesForDbRef(request.DBRef, request, cancellationToken);
		}

		return GetAttributesWithParentsAsync(request, cancellationToken);
	}

	private IAsyncEnumerable<SharpAttribute> GetAttributesForDbRef(DBRef dbref, GetAttributesQuery request, CancellationToken cancellationToken)
		=> request.Mode switch
		{
			IAttributeService.AttributePatternMode.Exact =>
				database.GetAttributesAsync(dbref, request.Pattern.ToUpper(), cancellationToken),
			IAttributeService.AttributePatternMode.Wildcard =>
				database.GetAttributesAsync(dbref, request.Pattern.ToUpper(), cancellationToken),
			IAttributeService.AttributePatternMode.Regex =>
				database.GetAttributesByRegexAsync(dbref, request.Pattern.ToUpper(), cancellationToken),
			_ => database.GetAttributesAsync(dbref, request.Pattern.ToUpper(), cancellationToken)
		};

	private async IAsyncEnumerable<SharpAttribute> GetAttributesWithParentsAsync(
		GetAttributesQuery request,
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		await foreach (var attr in GetAttributesForDbRef(request.DBRef, request, cancellationToken))
		{
			if (seen.Add(attr.LongName!))
				yield return attr;
		}

		var obj = await database.GetObjectNodeAsync(request.DBRef, cancellationToken);
		if (obj.IsNone) yield break;

		var current = obj.Known.Object();
		while (true)
		{
			var parent = await current.Parent.WithCancellation(cancellationToken);
			if (parent.IsNone) break;

			var parentObj = parent.Known.Object();
			await foreach (var attr in GetAttributesForDbRef(parentObj.DBRef, request, cancellationToken))
			{
				if (seen.Add(attr.LongName!))
					yield return attr;
			}

			current = parentObj;
		}
	}
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
