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
	: IStreamQueryHandler<GetAttributesQuery, AttributeWithSource>
{
	public IAsyncEnumerable<AttributeWithSource> Handle(GetAttributesQuery request,
		CancellationToken cancellationToken)
	{
		if (!request.CheckParents)
		{
			return GetAttributesForDbRef(request.DBRef, request, cancellationToken)
				.Select(attr => new AttributeWithSource(attr, request.DBRef));
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

	private async IAsyncEnumerable<AttributeWithSource> GetAttributesWithParentsAsync(
		GetAttributesQuery request,
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		await foreach (var attr in GetAttributesForDbRef(request.DBRef, request, cancellationToken))
		{
			if (seen.Add(attr.LongName!))
				yield return new AttributeWithSource(attr, request.DBRef);
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
				// Penn's atr_iter_get_parent (attrib.c:1500-1622) has an early fast-path
				// (attrib.c:1522-1529): any literal, non-wildcarded pattern is routed straight
				// through atr_get_with_parent -- the same function backing get() -- and never
				// reaches the seen/st_insert iteration loop at all. Every pattern this handler
				// sees under AttributePatternMode.Exact is literal, so the operative reference
				// for those is atr_get_with_parent (attrib.c:1232-1252), identical to the fix
				// in GetAttributeWithInheritanceAsync: a private hit on a nearer ancestor
				// blocks resolution outright and never falls through to a farther ancestor's
				// unflagged copy. Recording membership before the flag check reproduces that
				// outcome for exact-mode lookups.
				//
				// The iteration loop (attrib.c:1580-1622, entered only for a genuine wildcard
				// or regex pattern) has its own st_insert-before-AF_Private ordering, which
				// gives the same shadowing property there too -- but that loop's private test
				// only continues the walk rather than aborting it, so a farther ancestor CAN
				// still surface under a different branch than the one that shadowed it. See
				// the task report for a known, narrow case where that leaves SharpMUSH
				// stricter than live Penn for a genuine wildcard pattern.
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

				// The source object rides along with the match: it is what a downstream read
				// gate has to re-walk the ancestor path against (AttributeWithSource).
				yield return new AttributeWithSource(attr, parentObj.DBRef);
			}

			current = parentObj;
		}
	}
}

public class GetLazyAttributesQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetLazyAttributesQuery, LazyAttributeWithSource>
{
	// This handler does not walk the parent chain at all (it ignores request.CheckParents), so
	// every match is by construction sourced from request.DBRef itself. It still reports the
	// source, so the read gate downstream is written once against provenance rather than
	// assuming it - the assumption is exactly what leaked in the eager path.
	public IAsyncEnumerable<LazyAttributeWithSource> Handle(GetLazyAttributesQuery request,
		CancellationToken cancellationToken)
		=> (request.Mode switch
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
		}).Select(attr => new LazyAttributeWithSource(attr, request.DBRef));
}
