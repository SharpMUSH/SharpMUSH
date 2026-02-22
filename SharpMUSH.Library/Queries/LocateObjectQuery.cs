using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Queries;

/// <summary>
/// Query to locate an object by name without requiring parser context.
/// Used in lock evaluation to break circular dependencies.
/// </summary>
/// <param name="Looker">The object doing the looking</param>
/// <param name="Executor">The executor object</param>
/// <param name="Name">The name to search for</param>
/// <param name="Flags">Locate flags to control search behavior</param>
public record LocateObjectQuery(
	AnySharpObject Looker,
	AnySharpObject Executor,
	string Name,
	LocateFlags Flags) : IQuery<AnyOptionalSharpObjectOrError>;
