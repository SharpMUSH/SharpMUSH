using Mediator;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Handler for evaluating lock expressions through the mediator pattern.
/// This breaks the circular dependency between BooleanExpressionParser and LockService.
/// </summary>
public class EvaluateLockQueryHandler(ILockService lockService) : IQueryHandler<EvaluateLockQuery, bool>
{
	public ValueTask<bool> Handle(EvaluateLockQuery query, CancellationToken cancellationToken)
	{
		var result = lockService.Evaluate(query.LockString, query.Gated, query.Unlocker);
		return ValueTask.FromResult(result);
	}
}
