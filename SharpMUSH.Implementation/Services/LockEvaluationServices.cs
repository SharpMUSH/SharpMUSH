using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

public sealed class LockEvaluationServices(
	Lazy<ILocateService> locate,
	Lazy<IAttributeService> attributes,
	Lazy<ILockService> locks,
	Lazy<IMUSHCodeParser> parser,
	ILogger<LockEvaluationServices> logger) : ILockEvaluationServices
{
	/// <remarks>
	/// Lock evaluation runs after every substitution has been pre-evaluated, so the root parser's
	/// state is safe to locate against.
	/// </remarks>
	public ValueTask<AnyOptionalSharpObjectOrError> LocateAsync(AnySharpObject looker, AnySharpObject executor, string name, LocateFlags flags)
		=> locate.Value.Locate(parser.Value, looker, executor, name, flags);

	public ValueTask<OptionalSharpAttributeOrError> GetAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute,
		IAttributeService.AttributeMode mode, bool parent = true)
		=> attributes.Value.GetAttributeAsync(executor, obj, attribute, mode, parent);

	/// <remarks>
	/// An evaluation that throws returns <see cref="LockEvaluationFailure"/>, not a value: a failure
	/// and a non-matching result both deny, but only one of them says the game is broken, and a caller
	/// that wants to treat them differently needs to be able to.
	/// </remarks>
	public async ValueTask<OneOf<string, LockEvaluationFailure>> EvaluateAttributeAsync(AnySharpObject gated, AnySharpObject unlocker, string attributeName)
	{
		// PennMUSH: call_ufun(&ufun, buff, player, player, pe_info, NULL)
		// where player = unlocker, and the attribute is on the gated object.
		try
		{
			var unlockerRef = unlocker.Object().DBRef;

			var evalParser = parser.Value.Push(new ParserState(
				Registers: new([[]]),
				IterationRegisters: [],
				RegexRegisters: [],
				SwitchStack: [],
				ExecutionStack: [],
				EnvironmentRegisters: [],
				CurrentEvaluation: null,
				ParserFunctionDepth: 0,
				Function: null,
				Command: null,
				CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
				Switches: [],
				Arguments: [],
				Executor: unlockerRef,
				Enactor: unlockerRef,
				Caller: unlockerRef,
				Handle: null,
				ParseMode: ParseMode.Default,
				CallDepth: new InvocationCounter(),
				FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				TotalInvocations: new InvocationCounter(),
				LimitExceeded: new LimitExceededFlag()));

			var result = await attributes.Value.EvaluateAttributeFunctionAsync(
				evalParser,
				unlocker,
				gated,
				attributeName,
				new Dictionary<string, CallState>(),
				evalParent: false,
				ignorePermissions: true);

			return result.ToPlainText();
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to evaluate attribute {Attribute} on {Object} for lock evaluation", attributeName, gated);
			return new LockEvaluationFailure(attributeName, ex.Message);
		}
	}

	public bool EvaluateLock(string lockString, AnySharpObject gated, AnySharpObject unlocker)
		=> locks.Value.Evaluate(lockString, gated, unlocker);
}
