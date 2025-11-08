using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Queries;

/// <summary>
/// Query to evaluate a lock string against a gated object and an unlocker.
/// Used to break circular dependency between BooleanExpressionParser and LockService.
/// </summary>
/// <param name="LockString">The lock expression to evaluate</param>
/// <param name="Gated">The object being locked/gated</param>
/// <param name="Unlocker">The object attempting to pass the lock</param>
public record EvaluateLockQuery(string LockString, AnySharpObject Gated, AnySharpObject Unlocker) : IQuery<bool>;
