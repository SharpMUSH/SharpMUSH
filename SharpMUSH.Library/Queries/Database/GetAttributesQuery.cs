using Mediator;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Streams every attribute on <paramref name="DBRef"/> matching <paramref name="Pattern"/>,
/// optionally continuing up the <c>@parent</c> chain.
/// </summary>
/// <remarks>
/// Each match carries the object it was read from (see <see cref="AttributeWithSource"/>). With
/// <paramref name="CheckParents"/> the results are a mix of the object's own attributes and
/// inherited ones, and a consumer that re-walks an attribute's ancestor path - as every
/// PennMUSH read permission check does - has to run that walk against the object the match
/// actually came from, since a parent's branch nodes need not exist on the child at all.
/// </remarks>
public record GetAttributesQuery(
	DBRef DBRef,
	string Pattern,
	bool CheckParents,
	IAttributeService.AttributePatternMode Mode)
	: IStreamQuery<AttributeWithSource>;

/// <inheritdoc cref="GetAttributesQuery"/>
public record GetLazyAttributesQuery(
	DBRef DBRef,
	string Pattern,
	bool CheckParents,
	IAttributeService.AttributePatternMode Mode)
	: IStreamQuery<LazyAttributeWithSource>;