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
				// Penn's atr_iter_get_parent (attrib.c:1580-1622) calls st_insert (its "seen"
				// set) BEFORE testing AF_Private, so a private attribute on a nearer ancestor
				// shadows a farther ancestor's same-named copy even though the nearer one is
				// never yielded itself. Recording membership first, and bailing on a repeat
				// before any flag check, reproduces that shadowing.
				if (!seen.Add(attr.LongName!))
					continue;

				// no_inherit on ANY level of the branch blocks the whole path when crossing
				// this parent boundary (Penn: AF_Private test in atr_get_with_parent,
				// attrib.c:1232-1252 -- checking attr.Flags alone only covers the leaf). Only
				// pay for the full-path re-resolution when there's a branch to check at all --
				// a flat (no backtick) attribute IS the whole path, so its own flags suffice
				// and the common case costs nothing extra.
				if (attr.LongName!.Contains('`'))
				{
					var segments = attr.LongName.Split('`');
					var path = await database.GetAttributeAsync(parentObj.DBRef, segments, cancellationToken)
						.ToArrayAsync(cancellationToken);
					// Fail closed: if re-resolution doesn't return the full path (a race, or a
					// name-normalisation mismatch), deny rather than yield the attribute.
					if (path.Length != segments.Length || path.Any(a => a.Flags.Any(f => f.Name == "no_inherit")))
						continue;
				}
				else if (attr.Flags.Any(f => f.Name == "no_inherit"))
				{
					continue;
				}

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
