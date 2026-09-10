using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAttributeQueryHandler(IAttributeStore database)
	: IStreamQueryHandler<GetAttributeQuery, SharpAttribute>
{
	public IAsyncEnumerable<SharpAttribute> Handle(GetAttributeQuery request,
		CancellationToken cancellationToken)
		=> database.GetAttributeAsync(request.DBRef, Array.ConvertAll(request.Attribute, x => x.ToUpper()), cancellationToken);
}

public class GetLazyAttributeQueryHandler(IAttributeStore database)
	: IStreamQueryHandler<GetLazyAttributeQuery, LazySharpAttribute>
{
	public IAsyncEnumerable<LazySharpAttribute> Handle(GetLazyAttributeQuery request,
		CancellationToken cancellationToken)
		=> database.GetLazyAttributeAsync(request.DBRef, Array.ConvertAll(request.Attribute, x => x.ToUpper()), cancellationToken);
}

public class GetAttributesQueryHandler(
	IAttributeStore database,
	IObjectStore objects,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration)
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

		// One walk, shared with the lazy handler below: the two queries are one documented
		// contract, and keeping two copies of atr_iter_get_parent is how they came to disagree.
		return AttributeAncestry.MatchesWithParentsAsync(
				request.DBRef,
				async () =>
				{
					var obj = await objects.GetObjectNodeAsync(request.DBRef, cancellationToken);
					return obj.IsNone ? null : obj.Known.Object();
				},
				(int)configuration.CurrentValue.Limit.MaxParents,
				dbref => GetAttributesForDbRef(dbref, request, cancellationToken),
				(dbref, segments) => database.GetAttributeAsync(dbref, segments, cancellationToken),
				static attr => attr.LongName!,
				static attr => attr.IsNoInherit(),
				cancellationToken)
			.Select(match => new AttributeWithSource(match.Attribute, match.Source));
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
}

/// <summary>
/// The lazy twin of <see cref="GetAttributesQueryHandler"/>. Same walk, same
/// <c>CheckParents</c> semantics — <c>GetLazyAttributesQuery</c> inherits the eager query's
/// documentation, so a caller that switches to it purely to avoid materialising a large result
/// must get the same attributes back.
/// </summary>
public class GetLazyAttributesQueryHandler(
	IAttributeStore database,
	IObjectStore objects,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration)
	: IStreamQueryHandler<GetLazyAttributesQuery, LazyAttributeWithSource>
{
	public IAsyncEnumerable<LazyAttributeWithSource> Handle(GetLazyAttributesQuery request,
		CancellationToken cancellationToken)
	{
		if (!request.CheckParents)
		{
			return GetAttributesForDbRef(request.DBRef, request, cancellationToken)
				.Select(attr => new LazyAttributeWithSource(attr, request.DBRef));
		}

		return AttributeAncestry.MatchesWithParentsAsync(
				request.DBRef,
				async () =>
				{
					var obj = await objects.GetObjectNodeAsync(request.DBRef, cancellationToken);
					return obj.IsNone ? null : obj.Known.Object();
				},
				(int)configuration.CurrentValue.Limit.MaxParents,
				dbref => GetAttributesForDbRef(dbref, request, cancellationToken),
				(dbref, segments) => database.GetLazyAttributeAsync(dbref, segments, cancellationToken),
				static attr => attr.LongName,
				static attr => attr.IsNoInherit(),
				cancellationToken)
			.Select(match => new LazyAttributeWithSource(match.Attribute, match.Source));
	}

	private IAsyncEnumerable<LazySharpAttribute> GetAttributesForDbRef(DBRef dbref, GetLazyAttributesQuery request, CancellationToken cancellationToken)
		=> request.Mode switch
		{
			IAttributeService.AttributePatternMode.Exact =>
				database.GetLazyAttributesAsync(dbref, request.Pattern.ToUpper(), cancellationToken),
			IAttributeService.AttributePatternMode.Wildcard =>
				database.GetLazyAttributesAsync(dbref, request.Pattern.ToUpper(), cancellationToken),
			IAttributeService.AttributePatternMode.Regex =>
				database.GetLazyAttributesByRegexAsync(dbref, request.Pattern.ToUpper(), cancellationToken),
			_ => database.GetLazyAttributesAsync(dbref, request.Pattern.ToUpper(), cancellationToken)
		};
}
