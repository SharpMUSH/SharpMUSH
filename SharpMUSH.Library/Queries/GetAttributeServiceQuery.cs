using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Queries;

/// <summary>
/// Offers a Mediator call into the Attribute Service to get an attribute when dealing with Circular Dependencies.
/// </summary>
/// <param name="executor"></param>
/// <param name="obj"></param>
/// <param name="attribute"></param>
/// <param name="mode"></param>
/// <param name="parent"></param>
public record GetAttributeServiceQuery(
	AnySharpObject executor,
	AnySharpObject obj,
	string attribute,
	IAttributeService.AttributeMode mode,
	bool parent = true) : IQuery<OptionalSharpAttributeOrError>;
