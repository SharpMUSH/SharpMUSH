using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// What a compiled lock reaches at evaluation time. The lock service owns the expression parser,
/// and the locate and attribute services reach the lock service through permissions, so none of
/// them can be a constructor dependency of the parser; this seam resolves them on first use.
/// </summary>
public interface ILockEvaluationServices
{
	ValueTask<AnyOptionalSharpObjectOrError> LocateAsync(AnySharpObject looker, AnySharpObject executor, string name, LocateFlags flags);

	ValueTask<OptionalSharpAttributeOrError> GetAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute,
		IAttributeService.AttributeMode mode, bool parent = true);

	/// <summary>
	/// Evaluates <paramref name="attributeName"/> on <paramref name="gated"/> as MUSHcode with
	/// <paramref name="unlocker"/> as the enactor, the way PennMUSH's <c>check_attrib_lock()</c> does.
	/// </summary>
	ValueTask<OneOf<string, LockEvaluationFailure>> EvaluateAttributeAsync(AnySharpObject gated, AnySharpObject unlocker, string attributeName);

	bool EvaluateLock(string lockString, AnySharpObject gated, AnySharpObject unlocker);
}
