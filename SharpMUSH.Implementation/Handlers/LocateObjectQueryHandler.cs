using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Handler for locating objects without requiring parser context.
/// Breaks circular dependency in lock evaluation.
/// 
/// Note: The parser is passed through to LocateService. Lock evaluation
/// occurs after all substitutions (%#, %!, etc.) have been pre-evaluated,
/// so the parser state at this point is safe to use.
/// </summary>
public class LocateObjectQueryHandler(ILocateService locateService, IMUSHCodeParser parser) : IQueryHandler<LocateObjectQuery, AnyOptionalSharpObjectOrError>
{
	public async ValueTask<AnyOptionalSharpObjectOrError> Handle(LocateObjectQuery query, CancellationToken cancellationToken)
	{
		return await locateService.Locate(
			parser: parser,
			looker: query.Looker,
			executor: query.Executor,
			name: query.Name,
			flags: query.Flags);
	}
}
