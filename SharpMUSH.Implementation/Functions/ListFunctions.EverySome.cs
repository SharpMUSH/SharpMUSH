using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "every", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list", "delimiter", "register"])]
	public static async ValueTask<CallState> Every(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await EverySomeInternal(parser, isEvery: true);

	[SharpFunction(Name = "some", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list", "delimiter", "register"])]
	public static async ValueTask<CallState> Some(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await EverySomeInternal(parser, isEvery: false);

	/// <summary>
	/// Shared engine for every()/some(): evaluates a predicate attribute (or #lambda) per element,
	/// with boolean truth per Truthy() (the filterbool() rule). Returns 1/0. When a register name is
	/// given, the NON-matching elements are stored in that q-register (delimiter-joined), and
	/// short-circuiting is disabled so all failures are collected.
	/// </summary>
	private static async ValueTask<CallState> EverySomeInternal(IMUSHCodeParser parser, bool isEvery)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var rawAttrArg = parser.CurrentState.Arguments["0"].Message!;
		var rawAttrStr = MModule.plainText(rawAttrArg)!;

		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(" "));
		var list = MModule.splitList(delim, parser.CurrentState.Arguments["1"].Message!);

		string? registerName = null;
		if (parser.CurrentState.ArgumentsOrdered.TryGetValue("3", out var registerArg) && registerArg.Message is not null)
		{
			var candidate = MModule.plainText(registerArg.Message);
			if (!string.IsNullOrWhiteSpace(candidate))
			{
				registerName = candidate;
			}
		}

		// A blank list has no elements: every() is vacuously true, some() finds nothing.
		if (list.Length == 0 || (list.Length == 1 && string.IsNullOrEmpty(MModule.plainText(list[0]))))
		{
			if (registerName is not null && !parser.CurrentState.AddRegister(registerName.ToUpper(), MModule.empty()))
			{
				return new CallState(ErrorMessages.Returns.BadRegName);
			}

			return new CallState(isEvery ? "1" : "0");
		}

		var failures = new List<MString>();
		var sawPass = false;
		var sawFail = false;

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			// Evaluate the lambda per item (not via the batch helper) so short-circuiting is honored:
			// every() stops on the first failure and some() on the first success when no register is
			// given. This also keeps predicate side-effects (e.g. setq) from firing past that point,
			// matching the non-lambda branch below.
			foreach (var item in list)
			{
				var result = await AttributeService!.EvaluateAttributeFunctionAsync(
					parser, executor, rawAttrArg,
					new Dictionary<string, CallState> { { "0", new CallState(item) } });

				if (result.Truthy())
				{
					sawPass = true;

					// some() without a register can stop at the first success.
					if (!isEvery && registerName is null)
					{
						break;
					}
				}
				else
				{
					sawFail = true;
					failures.Add(item);

					// every() without a register can stop at the first failure.
					if (isEvery && registerName is null)
					{
						break;
					}
				}
			}
		}
		else
		{
			var objAttr = HelperFunctions.SplitOptionalObjectAndAttr(rawAttrStr);
			if (objAttr is { IsT1: true, AsT1: false })
			{
				return new CallState(ErrorMessages.Returns.ObjectAttributeString);
			}

			var (dbref, attrName) = objAttr.AsT0;
			dbref ??= executor.ToString();

			var locate = await LocateService!.LocateAndNotifyIfInvalid(
				parser, executor, executor, dbref, LocateFlags.All);
			if (!locate.IsValid())
			{
				return CallState.Empty;
			}

			var located = locate.WithoutError().WithoutNone();

			var maybeAttr = await AttributeService!.GetAttributeAsync(
				executor, located, attrName, mode: IAttributeService.AttributeMode.Execute, parent: true);
			if (maybeAttr.IsNone)
			{
				return new CallState(ErrorMessages.Returns.NoSuchAttribute);
			}

			if (maybeAttr.IsError)
			{
				return new CallState(maybeAttr.AsError.Value);
			}

			var attrValue = maybeAttr.AsAttribute.Last().Value;

			foreach (var item in list)
			{
				var newParser = parser.Push(parser.CurrentState with
				{
					Arguments = new Dictionary<string, CallState> { { "0", new CallState(item) } },
					EnvironmentRegisters = new Dictionary<string, CallState> { ["0"] = new CallState(item) }
				});

				if ((await newParser.FunctionParse(attrValue))!.Message!.Truthy())
				{
					sawPass = true;

					// some() without a register can stop at the first success.
					if (!isEvery && registerName is null)
					{
						break;
					}
				}
				else
				{
					sawFail = true;
					failures.Add(item);

					// every() without a register can stop at the first failure.
					if (isEvery && registerName is null)
					{
						break;
					}
				}
			}
		}

		if (registerName is not null && !parser.CurrentState.AddRegister(
			registerName.ToUpper(), MModule.multipleWithDelimiter(delim, failures)))
		{
			return new CallState(ErrorMessages.Returns.BadRegName);
		}

		return new CallState(isEvery
			? sawFail ? "0" : "1"
			: sawPass ? "1" : "0");
	}
}
