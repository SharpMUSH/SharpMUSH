using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Handler for locating objects without requiring parser context.
/// Breaks circular dependency in lock evaluation.
/// </summary>
public class LocateObjectQueryHandler(ILocateService locateService) : IQueryHandler<LocateObjectQuery, AnyOptionalSharpObjectOrError>
{
	public async ValueTask<AnyOptionalSharpObjectOrError> Handle(LocateObjectQuery query, CancellationToken cancellationToken)
	{
		// Call LocateService with null parser as substitutions should have been pre-evaluated
		return await locateService.Locate(
			parser: null!,
			looker: query.Looker,
			executor: query.Executor,
			name: query.Name,
			flags: query.Flags);
	}
}
