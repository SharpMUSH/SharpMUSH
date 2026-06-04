using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries;

/// <summary>
/// Query to evaluate an attribute on an object as MUSHcode for lock evaluation.
/// In PennMUSH, evaluation locks (ATTR/pattern) fetch the attribute from the gated object,
/// evaluate it as MUSHcode with the unlocker as the enactor (%#), and compare the result.
/// </summary>
/// <param name="GatedObject">The object whose attribute is being evaluated</param>
/// <param name="Unlocker">The object attempting to pass the lock (becomes %# during eval)</param>
/// <param name="AttributeName">The attribute name to evaluate</param>
public record EvaluateAttributeForLockQuery(
	AnySharpObject GatedObject,
	AnySharpObject Unlocker,
	string AttributeName) : IQuery<string?>;
