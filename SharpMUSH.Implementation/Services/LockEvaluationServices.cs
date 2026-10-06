using SharpMUSH.Library;
using SharpMUSH.Library.Common;
using Microsoft.Extensions.Logging;
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
	Lazy<IPermissionService> permissions,
	Lazy<IConnectionService> connections,
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
	public async ValueTask<LockEvaluation> EvaluateAttributeAsync(AnySharpObject gated, AnySharpObject unlocker, string attributeName)
	{
		// PennMUSH: call_ufun(&ufun, buff, player, player, pe_info, NULL)
		// where player = unlocker, and the attribute is on the gated object.
		try
		{
			var unlockerRef = unlocker.Object().DBRef;
			var arguments = LockEvaluationArguments.CreateArguments();

			var evalParser = parser.Value.Push(ParserState.RootFor(unlockerRef) with
			{
				Arguments = arguments,
				// A limit the lock's evaluation hits halts the evaluation that asked for the lock.
				LimitExceeded = OutputCeiling.Current?.Flag ?? new LimitExceededFlag(),
				Restrictions = EvaluationRestrictions.Current
			});

			var result = await attributes.Value.EvaluateAttributeFunctionAsync(
				evalParser,
				unlocker,
				gated,
				attributeName,
				arguments,
				// check_attrib_lock reads the attribute with fetch_ufun_attrib, an atr_get through the
				// parents and the type ancestor (src/boolexp.c:1983).
				evalParent: true,
				ignorePermissions: true);

			return result.ToPlainText();
		}
		catch (OperationCanceledException) when (ExecutionBudget.CurrentToken.IsCancellationRequested || ExecutionBudget.Current?.IsExceeded == true)
		{
			throw;
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to evaluate attribute {Attribute} on {Object} for lock evaluation", attributeName, gated);
			return new LockEvaluationFailure(attributeName, ex.Message);
		}
	}

	public ValueTask<bool> EvaluateLock(string lockString, AnySharpObject gated, AnySharpObject unlocker)
		=> locks.Value.Evaluate(lockString, gated, unlocker);
	public ValueTask<bool> EvaluateLockType(string lockName, AnySharpObject gated, AnySharpObject unlocker)
		=> locks.Value.EvaluateType(lockName, gated, unlocker);

	public async ValueTask<string> FormatObjectAsync(AnySharpObject viewer, AnySharpObject obj)
	{
		var showReference = await permissions.Value.CanExamine(viewer, obj)
			|| await permissions.Value.CanLinkToAsync(viewer, obj) || await obj.HasFlag("JUMP_OK")
			|| await obj.HasFlag("CHOWN_OK") || await obj.HasFlag("DESTROY_OK");
		return showReference ? await MessageFormatting.FormatObjectWithDbref(obj.Object(), await FlagView.ForAsync(viewer, connections.Value)) : obj.Object().Name;
	}

}
