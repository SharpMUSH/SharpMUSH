using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// Everything between recognising <c>name(...)</c> as a call and having its result: resolution,
/// permission and restriction checks, arity, limits, argument evaluation, the function frame,
/// invocation, the output ceiling and telemetry.
/// </summary>
/// <remarks>
/// Stateless: one instance serves every evaluation of a parser. Per-evaluation state — the parse
/// tree's source text, function-recognition suppression — stays on the
/// <see cref="SharpMUSHParserVisitor"/> passed in, which also evaluates the arguments.
/// </remarks>
internal sealed class FunctionInvocationPipeline(EvaluationServices services)
{
	/// <summary>
	/// Parses and executes a function call.
	/// </summary>
	/// <param name="visitor">The visitor walking the call: its parser, options and source text, and the
	/// visitor its arguments are evaluated with.</param>
	/// <param name="name">Function Name</param>
	/// <param name="context">Function Context for Depth</param>
	/// <param name="args">Arguments</param>
	/// <returns>The resulting CallState.</returns>
	public async ValueTask<CallState> InvokeAsync(SharpMUSHParserVisitor visitor, string name,
		IFunctionContext context, IEvaluationStringContext?[] args)
	{
		var parser = visitor.Parser;
		var configuration = visitor.Configuration;
		ExecutionBudget.Current?.ThrowIfExceeded();
		var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
		var success = true;
		// The registered name, never the name as typed: a typed name is free text, and every distinct
		// label value is a series of its own in Prometheus. Null for prose, which is not a call.
		string? measuredName = null;
		var didPushFunction = false;
		LimitExceededFlag? limitExceeded = null;
		var isolated = EvaluationRestrictions.Current is not null || parser.CurrentState.Restrictions is not null;

		try
		{
			if (!parser.FunctionLibrary.TryGetValue(name, out var libraryMatch))
			{
				// Built-ins take precedence; only on a built-in miss do we consult the
				// in-memory global user-defined-function registry (@function). Resolved
				// entries are evaluated ufun-style against <object>/<attribute> with the
				// call args bound to %0.., and are NOT cached in the shared FunctionLibrary
				// (so /enable, /disable, /delete take effect immediately and never leak).
				var userFunction = ResolveUserDefinedFunction(name);
				if (userFunction is null)
				{
					if (EvaluationRestrictions.Current is not null || parser.CurrentState.Restrictions is not null)
					{
						measuredName = TelemetryService.UnknownFunction;
						throw new RestrictedExpressionException();
					}
					if (!SharpMUSHParserVisitor.IsUnknownFunctionAnError(context))
					{
						// Not a function and not required to be one: the text is prose, not a call.
						return await visitor.LiteralFunctionCall(context);
					}

					measuredName = TelemetryService.UnknownFunction;
					success = false;
					return new CallState(EvaluationDiagnostics.UnknownFunction(name, parser.FunctionLibrary), context.Depth());
				}

				libraryMatch = (userFunction.Value, false);
			}

			var definition = libraryMatch.LibraryInformation;
			measuredName = definition.Attribute.Name.ToUpperInvariant();
			EvaluationRestrictions.Demand(definition, parser.CurrentState.Restrictions);
			if ((EvaluationRestrictions.Current is not null || parser.CurrentState.Restrictions is not null)
				&& args.Length > EvaluationRestrictions.MaximumArguments)
				throw new RestrictedExpressionException();
			var attribute = definition.Attribute;

			var currentState = parser.CurrentState;
			var contextDepth = context.Depth();

			var invocationCounter = currentState.TotalInvocations!;
			var callDepth = currentState.CallDepth!;
			limitExceeded = currentState.LimitExceeded!;

			var totalInvocations = invocationCounter.Increment();
			if (totalInvocations > configuration.CurrentValue.Limit.FunctionInvocationLimit)
			{
				limitExceeded.IsExceeded = true;
				limitExceeded.ErrorMessage ??= ErrorMessages.Returns.Invoke;
				return new CallState(ErrorMessages.Returns.Invoke, contextDepth);
			}

			var currentDepth = callDepth.Increment();
			didPushFunction = true;

			// Built-in functions do NOT track recursion - only nesting depth
			// Recursion tracking only applies to user-defined attributes (u(), ufun(), etc.)

			List<CallState> refinedArguments;

			// @function/restrict restrictions. For user-defined functions the restriction string
			// is carried on the synthesized attribute's Restrict (see ResolveUserDefinedFunction);
			// for built-ins it lives in the registry overlay keyed by name.
			var functionRestriction = attribute.Restrict is { Length: > 0 }
				? string.Join(' ', attribute.Restrict)
				: null;
			var builtinRestriction = services.UserFunctions?.GetBuiltinRestriction(name);

			// nobody is FN_DISABLED, which answers e_disabled before any permission check, so the
			// permission gate below does not turn it into e_perm (src/parse.c).
			if (Disables(functionRestriction) || Disables(builtinRestriction))
			{
				success = false;
				return new CallState(ErrorMessages.Returns.FunctionDisabled, contextDepth);
			}

			isolated |= visitor.BeginsRestrictedEvaluation(context);
			AnySharpObject? executor = null;
			string? permissionError;
			if (isolated)
			{
				permissionError = FunctionDispatcher.CheckPermissionWithoutObjectData(attribute);
			}
			else
			{
				if (await currentState.ExecutorObject(services.Mediator) is not AnySharpObject knownExecutor)
				{
					success = false;
					return CallState.Empty;
				}
				executor = knownExecutor;
				permissionError = await FunctionDispatcher.CheckPermissionAsync(attribute, executor);
			}
			if (permissionError is not null)
			{
				success = false;
				return new CallState(permissionError, contextDepth);
			}

			// Either restriction failing means the caller lacks permission and gets the standard error
			// instead of the result. The function's own restriction (attribute.Restrict) was already
			// answered by the permission check above — CheckPermissionAsync tests it, and the isolated
			// check refuses any restricted function — so only the built-in overlay is left to ask.
			if (builtinRestriction is not null && (isolated || !await executor!.SatisfiesFunctionRestriction(builtinRestriction)))
			{
				success = false;
				return new CallState(ErrorMessages.Returns.PermissionDenied, contextDepth);
			}

			// PennMUSH compat: if minargs=0 and we got 1 empty arg from func(), treat as 0 args
			if (attribute.MinArgs == 0 && args is [null])
			{
				args = [];
			}

			// The only arity check a parsed call gets: InvokeValidatedAsync below does not repeat it.
			if (FunctionDispatcher.ValidateArgumentCount(attribute, name, args.Length) is { } arityError)
			{
				success = false;
				return new CallState(arityError, contextDepth);
			}

			// Note: PennMUSH does NOT limit built-in function nesting depth.
			// Only user-defined function recursion is limited (see FunctionRecursionLimit
			// in AttributeService.EvaluateAttributeFunctionAsync).
			// The CallLimit below provides a safety net against infinite built-in nesting.
			if (currentDepth > configuration.CurrentValue.Limit.CallLimit)
			{
				limitExceeded.IsExceeded = true;
				limitExceeded.ErrorMessage ??= ErrorMessages.Returns.Call;
				return new CallState(ErrorMessages.Returns.Call, contextDepth);
			}

			// Built-in functions do NOT check recursion limit
			// Only user-defined attributes check recursion (see AttributeService.EvaluateAttributeFunctionAsync)

			var stripAnsi = attribute.Flags.HasFlag(FunctionFlags.StripAnsi);
			// Start at the already recognized wrapper boundary, including fn chains
			// that serialize before the operation allowlist becomes ambient.
			using var retainedArguments = RestrictedTextRetention.Enter(parser.CurrentState, isolated);

			if (attribute.Flags.HasFlag(FunctionFlags.Literal))
			{
				// FunctionFlags.Literal: treat the entire content between parens as a single
				// raw unevaluated string. Do NOT split on commas, do NOT evaluate substitutions.
				// This is how PennMUSH's lit() works — lit(a,b,%q0) returns "a,b,%q0" verbatim.
				// Slice it out of the source rather than rebuilding it from GetText(), which
				// concatenates token text and so returns the content stripped of its markup.
				refinedArguments = [new CallState(visitor.LiteralArgumentText(context), contextDepth)];
			}
			else if (!attribute.Flags.HasFlag(FunctionFlags.NoParse))
			{
				// Arguments evaluate left to right, one at a time. A plain loop rather than async LINQ:
				// the enumerable adapters and a state machine per argument were a twelfth of the bytes
				// a nested call allocated.
				refinedArguments = new List<CallState>(Math.Max(args.Length, 1));
				foreach (var x in args)
				{
					if (x is null)
					{
						refinedArguments.Add(CallState.Empty);
						continue;
					}

					var evaluated = await visitor.VisitChildren(x) ?? CallState.Empty;
					var msg = evaluated.Message ?? MarkupText.Empty;
					retainedArguments?.Add(msg.Length);
					if (stripAnsi) msg = MarkupText.Plain(msg.ToPlainText());
					refinedArguments.Add(new CallState(msg, x.Depth()) { HadErrors = evaluated.HadErrors });
				}

				if (refinedArguments.Count == 0)
				{
					refinedArguments.Add(new CallState(MarkupText.Empty, context.Depth()));
				}
			}
			else
			{
				// Store NoParse arguments as unevaluated text with deferred evaluation.
				refinedArguments = new List<CallState>(Math.Max(args.Length, 1));
				foreach (var x in args)
				{
					if (x is null)
					{
						refinedArguments.Add(CallState.Empty);
						continue;
					}

					var text = visitor.GetContextText(x);
					var evalText = stripAnsi ? MarkupText.Plain(text.ToPlainText()) : text;
					var evaluate = SharpMUSHParserVisitor.CreateDeferredEvaluation(x, visitor, stripAnsi);
					refinedArguments.Add(new CallState(evalText, x.Depth(), null, async () => (await evaluate())?.Message)
					{ ParsedResult = evaluate });
				}

				if (refinedArguments.Count == 0)
				{
					refinedArguments.Add(new CallState(MarkupText.Empty, context.Depth()));
				}
			}

			// If a limit was exceeded during argument evaluation, return immediately with the error
			// of whichever limit actually tripped (invocation, recursion/call, or output size) —
			// not a blanket invocation-limit message, which would mislabel e.g. an oversized nested
			// result as an invocation-limit error.
			if (limitExceeded.IsExceeded)
			{
				return new CallState(limitExceeded.ErrorMessage ?? ErrorMessages.Returns.Invoke, contextDepth);
			}

			var newParser = parser.Push(currentState.ForFunction(name, SharpMUSHParserVisitor.NumberedArguments(refinedArguments)));

			var result = await FunctionDispatcher.InvokeValidatedAsync(newParser,
				definition, executor,
				configuration.CurrentValue.Function.FunctionSideEffects, services.NotifyService, visitor.Logger,
				argumentCount: args.Length);

			// Output ceiling: stop a single function that generates an enormous string from
			// propagating it (and halt the rest of the evaluation, as the other limits do). Checked
			// at the return so it covers every function without each having to guard itself.
			if (result.Message is not null && FunctionLimits.ExceedsOutput(currentState, result.Message.Length))
			{
				limitExceeded.IsExceeded = true;
				limitExceeded.ErrorMessage ??= ErrorMessages.Returns.OutputTooLarge;
				return new CallState(ErrorMessages.Returns.OutputTooLarge, contextDepth);
			}

			// A function that evaluates its own arguments (cand, iter, ...) may have run into a limit
			// and still returned a small value of its own; the limit halts the evaluation regardless.
			if (limitExceeded.IsExceeded)
			{
				return new CallState(limitExceeded.ErrorMessage ?? ErrorMessages.Returns.Invoke, contextDepth);
			}

			return result with { Depth = contextDepth };
		}
		catch (RestrictedExpressionException) { success = false; throw; }
		catch (OperationCanceledException)
		{
			success = false;
			throw;
		}
		catch (Exception) when (isolated || EvaluationRestrictions.Current is not null || parser.CurrentState.Restrictions is not null)
		{
			success = false;
			throw new RestrictedExpressionException();
		}
		catch (Exception ex)
		{
			visitor.Logger.LogError(ex, "CallFunction");
			success = false;

			// KnownExecutorObject throws when there is no executor — which is exactly the state at the
			// connect screen. Using it here meant the error handler replaced the real exception with an
			// ArgumentNullException of its own, hiding the actual failure. Resolve optionally instead.
			if (await parser.CurrentState.ExecutorObject(services.Mediator) is AnySharpObject executor && executor.IsGod())
			{
				await services.NotifyService.Notify(executor,
					string.Format(ErrorMessages.Returns.InternalErrorFormat, ex));
			}

			return CallState.Empty with { HadErrors = true };
		}
		finally
		{
			// Only decrement counters if we successfully executed the function
			// If a limit was exceeded, we returned early and should NOT decrement
			// (otherwise the counter resets and the limit never works)
			if (didPushFunction && (limitExceeded == null || !limitExceeded.IsExceeded))
			{
				var currentCallDepth = parser.CurrentState.CallDepth;

				currentCallDepth?.Decrement();

				// NOTE: Built-in functions do NOT track recursion
				// Only user-defined attributes track recursion (see AttributeService.EvaluateAttributeFunctionAsync)
			}

			var elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
			// A limit hit (invocation, recursion/call, or output size) aborts the invocation, so it is
			// not a successful call even though it returned a value rather than throwing.
			var limitHit = limitExceeded is { IsExceeded: true };
			if (measuredName is not null)
				services.Telemetry?.RecordFunctionInvocation(measuredName, elapsedMs, success && !limitHit);
		}
	}

	/// <summary>Whether a function restriction says <c>nobody</c>, PennMUSH's <c>FN_DISABLED</c> (<c>src/function.c</c>).</summary>
	private static bool Disables(string? restriction)
		=> restriction?.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries)
			.Any(word => word.Equals("nobody", StringComparison.OrdinalIgnoreCase)) ?? false;

	/// <summary>
	/// Resolves a global user-defined function (registered via <c>@function</c>) by name, returning
	/// a transient <see cref="FunctionDefinition"/> that the normal function-call pipeline executes.
	///
	/// <para>The synthesized definition carries the registry's min/max arg bounds (so the standard
	/// argument-count validation in <see cref="InvokeAsync"/> applies) and a delegate that evaluates
	/// the stored <c>&lt;object&gt;/&lt;attribute&gt;</c> as softcode with the call args bound to
	/// <c>%0..</c> — exactly like <c>ufun</c>. The definition is never cached in the shared library.</para>
	/// </summary>
	private FunctionDefinition? ResolveUserDefinedFunction(string name)
	{
		var entry = services.UserFunctions?.Resolve(name);
		if (entry is null)
		{
			return null;
		}

		var attribute = new SharpFunctionAttribute
		{
			Name = name,
			MinArgs = entry.MinArgs,
			MaxArgs = entry.MaxArgs,
			Flags = FunctionFlags.Regular,
			// Carry any @function/restrict restriction onto the synthesized attribute so the
			// permission check in InvokeAsync enforces it before evaluating the attribute.
			Restrict = string.IsNullOrWhiteSpace(entry.Restriction) ? [] : [entry.Restriction]
		};

		var target = entry.Object;
		var attributeName = entry.Attribute;

		return new FunctionDefinition(attribute, async invokedParser =>
		{
			if (await services.Mediator.Send(new GetObjectNodeQuery(target)) is not AnySharpObject targetObject)
			{
				return new CallState(string.Format(ErrorMessages.Returns.NoSuchFunction, name.ToUpperInvariant()));
			}

			// @function never checked the attribute was there (function.c:1687-1692); the call does, with
			// atr_get, so a parent's or the ancestor's copy serves (parse.c:3048-3059).
			if (await services.AttributeService.GetAttributeAsync(targetObject, targetObject, attributeName,
					IAttributeService.AttributeMode.Read, parent: true) is None)
			{
				return new CallState(string.Format(ErrorMessages.Returns.UserFunctionMissingAttribute,
					name.ToUpperInvariant(), $"#{targetObject.Object().DBRef.Number}", attributeName));
			}

			// The arguments pushed for this call become %0, %1, … inside the attribute.
			var args = invokedParser.CurrentState.Arguments
				.Select((kvp, i) => new KeyValuePair<string, CallState>(i.ToString(), kvp.Value))
				.ToDictionary();

			// A global @function runs *as the backing object, with its powers* (PennMUSH semantics):
			// the attribute is read and evaluated with the function object's permissions, not the
			// caller's, so a player who lacks read access to the object can still call the function.
			// The caller-level permission gate (@function/restrict) was already enforced in InvokeAsync.
			var result = await services.AttributeService.EvaluateAttributeFunctionResultAsync(
				invokedParser,
				targetObject,
				targetObject,
				attributeName,
				args,
				evalParent: true);

			return result;
		});
	}
}
