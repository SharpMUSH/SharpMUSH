using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// PennMUSH's <c>call_ufun</c>/<c>fetch_ufun_attrib</c> and the <c>#apply</c>/<c>#lambda</c>
/// pseudo-objects: everything that turns an attribute's stored text into a running evaluation.
/// </summary>
/// <remarks>
/// This is not attribute storage, and it reaches back into <see cref="IAttributeService"/> for the
/// one thing it does need — an <see cref="IAttributeService.AttributeMode.Execute"/>-mode read.
/// <para>
/// <paramref name="userFunctions"/> is optional for the same reason the service locator it replaces
/// used <c>GetService</c> rather than <c>GetRequiredService</c>: the builtin-restriction overlay is
/// absent in hosts that register no user-function registry, and a missing overlay means "no
/// restriction", not a failure.
/// </para>
/// </remarks>
internal sealed class AttributeEvaluator(
	IAttributeService attributes,
	IMediator mediator,
	ILocateService locateService,
	IValidateService validateService,
	INotifyService notifyService,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration,
	ILogger logger,
	IUserDefinedFunctionService? userFunctions)
{
	public async ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject obj, string attribute, Dictionary<string, CallState> args,
		bool evalParent = true, bool ignorePermissions = false)
		=> (await EvaluateAttributeFunctionResultAsync(parser, executor, obj, attribute, args, evalParent, ignorePermissions)).Message ?? MarkupText.Empty;

	public async ValueTask<CallState> EvaluateAttributeFunctionResultAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject obj,
		string attribute, Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false)
	{
		EvaluationRestrictions.DemandObjectDataAccess(parser.CurrentState.Restrictions);
		if (!await AttributeService.CheckReadAsync(() => validateService.Valid(IValidateService.ValidationType.AttributeName, MarkupText.Plain(attribute), obj)))
		{
			return new CallState(ErrorMessages.Returns.ObjectAttributeString);
		}

		var realExecutor = executor;

		if (ignorePermissions)
		{
			realExecutor = await mediator.Send(new GetObjectNodeQuery(new DBRef(1)), ExecutionBudget.CurrentToken) is AnySharpObject one
				? one
				: throw new InvalidOperationException("Object #1 does not exist to evaluate an attribute without permission checks.");
		}

		return await attributes.GetAttributeAsync(realExecutor, obj, attribute, IAttributeService.AttributeMode.Execute, evalParent) switch
		{
			SharpAttribute[] chain => await RunAsOwnerAsync(parser,
				new AttributeFunction(obj, chain.Last().LongName.ToUpper(), chain.Last().Value),
				s => s with { Arguments = args, EnvironmentRegisters = args }),
			None => CallState.Empty,
			Error<string> error => new CallState(error.Value)
		};
	}

	public ValueTask<CallState> CallAttributeFunctionAsync(IMUSHCodeParser parser, AttributeFunction function)
		=> RunAsOwnerAsync(parser, function, s => s);

	/// <summary>
	/// PennMUSH's <c>call_ufun</c>: the attribute runs as the object it was read from, with the current
	/// executor as caller.
	/// </summary>
	private async ValueTask<CallState> RunAsOwnerAsync(IMUSHCodeParser parser, AttributeFunction function,
		Func<ParserState, ParserState> withArguments)
	{
		var (owner, attributeName, code) = function;

		// PennMUSH: a HALTED object runs none of its softcode. process_expression returns PE_NOTHING for a
		// halted executor (src/parse.c), so the attribute yields its stored text unevaluated. The HALT flag is
		// set by @halt and by @chown (to break ownership loops). The code runs as its owner, so the owner's
		// flag is the one that matters.
		if (await owner.HasFlag("HALT", ExecutionBudget.CurrentToken))
		{
			return new CallState(code);
		}

		// Use shared tracking collections from parser state.
		// These are guaranteed to be non-null because:
		// - CommandParse creates them for each command evaluation
		// - FunctionParse creates them for standalone parsing
		// - All nested calls propagate them through parser state
		var callDepth = parser.CurrentState.CallDepth!;
		var recursionDepths = parser.CurrentState.FunctionRecursionDepths!;
		var limitExceeded = parser.CurrentState.LimitExceeded!;

		callDepth.Increment();
		if (!recursionDepths.TryGetValue(attributeName, out var depth))
		{
			depth = 0;
		}
		recursionDepths[attributeName] = ++depth;

		if (depth > configuration.CurrentValue.Limit.FunctionRecursionLimit)
		{
			limitExceeded.IsExceeded = true;
			limitExceeded.ErrorMessage ??= ErrorMessages.Returns.Recursion;
			return new CallState(ErrorMessages.Returns.Recursion);
		}

		try
		{
			var result = await parser.With(s =>
					withArguments(s) with
					{
						CurrentEvaluation = new DBAttribute(owner.Object().DBRef, attributeName),
						Function = attributeName,
						Executor = owner.Object().DBRef,
						Caller = s.Executor
					},
				async newParser =>
					await newParser.FunctionParse(code));

			return result ?? CallState.Empty;
		}
		finally
		{
			callDepth.Decrement();
			if (recursionDepths.TryGetValue(attributeName, out var currentDepth) && currentDepth > 0)
			{
				recursionDepths[attributeName] = currentDepth - 1;
			}
		}
	}

	public async ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor,
		MString objAndAttribute, Dictionary<string, CallState> args, bool evalParent = true,
		bool ignorePermissions = false, bool ignoreLambda = false)
		=> (await EvaluateAttributeFunctionResultAsync(parser, executor, objAndAttribute, args,
			evalParent, ignorePermissions, ignoreLambda)).Message ?? MarkupText.Empty;

	public async ValueTask<AttributeFunctionFetch> FetchAttributeFunctionAsync(IMUSHCodeParser parser,
		AnySharpObject executor, string objectAndAttribute)
	{
		if (HelperFunctions.SplitOptionalObjectAndAttr(objectAndAttribute) is not { Object: var objectName, Attribute: var attributeName })
		{
			return new CallState(ErrorMessages.Returns.ObjectAttributeString);
		}

		var located = await locateService.LocateAndNotifyIfInvalid(parser, executor, executor,
			objectName ?? executor.Object().DBRef.ToString(), LocateFlags.All);
		if (located is not AnySharpObject owner)
		{
			return CallState.Empty;
		}

		return await attributes.GetAttributeAsync(executor, owner, attributeName, IAttributeService.AttributeMode.Execute, parent: true) switch
		{
			SharpAttribute[] chain => new AttributeFunction(owner, chain.Last().LongName.ToUpper(), chain.Last().Value),
			None => new CallState(ErrorMessages.Returns.NoSuchAttribute),
			Error<string> error => new CallState(error.Value)
		};
	}

	public async ValueTask<CallState> EvaluateAttributeFunctionResultAsync(IMUSHCodeParser parser, AnySharpObject executor,
		MString objAndAttribute,
		Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false,
		bool ignoreLambda = false)
	{
		EvaluationRestrictions.DemandObjectDataAccess(parser.CurrentState.Restrictions);
		var split = objAndAttribute.Split("/");
		var obj = split.First();
		var attribute = MarkupText.Concat(split.Skip(1));
		var objPlainText = obj.ToPlainText();
		var applyPredicate = objPlainText.StartsWith("#apply", StringComparison.OrdinalIgnoreCase);
		var lambdaPredicate = objPlainText.StartsWith("#lambda", StringComparison.OrdinalIgnoreCase);

		if (!applyPredicate && !lambdaPredicate && attribute.Length == 0)
		{
			return await EvaluateAttributeFunctionResultAsync(parser, executor, executor,
				objPlainText, args, evalParent, ignorePermissions);
		}

		// Skip attribute name validation for lambda/apply: the "attribute" part is
		// executable code, not a database attribute name, and can contain characters
		// (e.g. '[', ']', '\') that are not valid in attribute names.
		if (!applyPredicate && !lambdaPredicate &&
				!await AttributeService.CheckReadAsync(() => validateService.Valid(IValidateService.ValidationType.AttributeName, attribute, new None())))
		{
			return new CallState(ErrorMessages.Returns.ObjectAttributeString);
		}

		var realExecutor = executor;

		if (ignorePermissions)
		{
			realExecutor = await mediator.Send(new GetObjectNodeQuery(new DBRef(1)), ExecutionBudget.CurrentToken) is AnySharpObject one
				? one
				: throw new InvalidOperationException("Object #1 does not exist to evaluate an attribute without permission checks.");
		}

		if (applyPredicate && !ignoreLambda)
		{
			var argN = 1;
			// The optional argument count is embedded in the obj portion after "#apply" (e.g. "#apply2" -> argN=2).
			// The function name is in the attribute portion (e.g. "#apply/strlen" -> funcname="strlen").
			var applyArgCountStr = objPlainText.Remove(0, 6); // part after "#apply"
			if (!string.IsNullOrWhiteSpace(applyArgCountStr) && !int.TryParse(applyArgCountStr, out argN))
			{
				return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "#APPLY"));
			}

			var slimArgs = Enumerable
				.Range(0, argN)
				.Select(i => i.ToString())
				.ToDictionary(k => k, k => args.TryGetValue(k, out var v) ? v : CallState.Empty);

			if (parser.FunctionLibrary.TryGetValue(attribute.ToPlainText().ToLower(), out var applyFunction))
			{
				var builtinRestriction = userFunctions?.GetBuiltinRestriction(attribute.ToPlainText());
				if (builtinRestriction is not null && !await realExecutor.SatisfiesFunctionRestriction(builtinRestriction))
					return new CallState(ErrorMessages.Returns.PermissionDenied);

				if (applyFunction.LibraryInformation.Attribute.Flags.HasFlag(FunctionFlags.StripAnsi))
				{
					slimArgs = slimArgs.ToDictionary(pair => pair.Key,
						pair => pair.Value with { Message = MarkupText.Plain(pair.Value.Message?.ToPlainText() ?? "") });
				}

				var result = await parser.With(
					s => s with
					{
						Arguments = slimArgs,
						EnvironmentRegisters = slimArgs,
						CallDepth = s.CallDepth,
						FunctionRecursionDepths = s.FunctionRecursionDepths,
						TotalInvocations = s.TotalInvocations,
						LimitExceeded = s.LimitExceeded,
						MoveDepth = s.MoveDepth
					},
					async np => await FunctionDispatcher.InvokeAsync(np, applyFunction.LibraryInformation, realExecutor,
						configuration.CurrentValue.Function.FunctionSideEffects, notifyService,
						logger)
				);

				return result;
			}

			// Check if proper function name in the attribute section.
			// Check if enough arguments are being passed to the function based on the number after #apply.
			// This is where we really need a proper attribute library access layer, similar to commands.

			// CallFunction must be Exposed by IMUSHCodeParser.
			// Further work is needed before this can be implemented properly.
		}

		if (lambdaPredicate && !ignoreLambda)
		{
			var result = await parser.With(s => s with
			{
				Arguments = args,
				EnvironmentRegisters = args,
				CallDepth = s.CallDepth,
				FunctionRecursionDepths = s.FunctionRecursionDepths,
				TotalInvocations = s.TotalInvocations,
				LimitExceeded = s.LimitExceeded,
				MoveDepth = s.MoveDepth
			},
				async np => await np.FunctionParse(attribute));
			return result ?? CallState.Empty;
		}

		var maybeObject =
			await locateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objPlainText,
				LocateFlags.All);

		return maybeObject switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject found => await EvaluateAttributeFunctionResultAsync(parser, executor, found, attribute.ToPlainText(),
				args, evalParent, ignorePermissions)
		};
	}
}
