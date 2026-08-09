using Mediator;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries;

/// <summary>
/// An eval lock's attribute could not be evaluated at all — the evaluation threw rather than producing
/// a value to compare against the lock's pattern.
/// </summary>
/// <remarks>
/// This exists so a caller cannot mistake "the evaluation blew up" for "the evaluation produced
/// something that did not match". Both deny, so the two are easy to conflate; they are not the same
/// event, and only one of them means the game is misconfigured or broken.
/// </remarks>
/// <param name="AttributeName">The attribute whose evaluation failed.</param>
/// <param name="Reason">The exception message, for a caller that wants to report or log it.</param>
public readonly record struct LockEvaluationFailure(string AttributeName, string Reason);

/// <summary>
/// Query to evaluate an attribute on an object as MUSHcode for lock evaluation.
/// In PennMUSH, evaluation locks (ATTR/pattern) fetch the attribute from the gated object,
/// evaluate it as MUSHcode with the unlocker as the enactor (%#), and compare the result.
/// </summary>
/// <remarks>
/// Returns a union rather than a nullable string. A lock decides who may enter, take, use or modify an
/// object; answering <c>null</c> for a failed evaluation left the caller comparing a null against the
/// lock's pattern and reaching a verdict that had nothing to do with the lock's intent. The union makes
/// the compiler ask every caller what a failure means to it.
/// </remarks>
/// <param name="GatedObject">The object whose attribute is being evaluated</param>
/// <param name="Unlocker">The object attempting to pass the lock (becomes %# during eval)</param>
/// <param name="AttributeName">The attribute name to evaluate</param>
public record EvaluateAttributeForLockQuery(
	AnySharpObject GatedObject,
	AnySharpObject Unlocker,
	string AttributeName) : IQuery<OneOf<string, LockEvaluationFailure>>;
