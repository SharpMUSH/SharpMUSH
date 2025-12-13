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
/// Note: This handler passes null for the parser parameter because lock evaluation
/// occurs after all substitutions (%#, %!, etc.) have been pre-evaluated.
/// The LocateService will not attempt to use the parser when it is null.
/// </summary>
public class LocateObjectQueryHandler(ILocateService locateService, IMUSHCodeParser parser) : IQueryHandler<LocateObjectQuery, AnyOptionalSharpObjectOrError>
{
	public async ValueTask<AnyOptionalSharpObjectOrError> Handle(LocateObjectQuery query, CancellationToken cancellationToken)
	{
		// Pass null parser - substitutions have been pre-evaluated before lock parsing
		// This is safe because the locate service handles null parser appropriately
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type
		return await locateService.Locate(
			parser: parser,
			looker: query.Looker,
			executor: query.Executor,
			name: query.Name,
			flags: query.Flags);
#pragma warning restore CS8625
	}
}
