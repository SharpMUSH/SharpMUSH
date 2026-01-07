using System.Text.RegularExpressions;
using System.Linq;
using OneOf;
using OneOf.Types;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "aposs", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> AbsolutePossessivePronoun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, arg0,
			LocateFlags.All,
			async onObject => await AttributeHelpers.GetPronoun(AttributeService!, Mediator!, parser, onObject,
				Configuration!.CurrentValue.Attribute.GenderAttribute,
				Configuration.CurrentValue.Attribute.AbsolutePossessivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "his",
					"F" or "Female" => "hers",
					_ => "theirs"
				}));
	}

	[SharpFunction(Name = "attrib_set", MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["object/attribute"])]
	public static async ValueTask<CallState> AttributeSet(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var args = parser.CurrentState.Arguments;
		var split = HelperFunctions.SplitObjectAndAttr(MModule.plainText(args["0"].Message!));
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var executor = (await parser.CurrentState.ExecutorObject(Mediator!)).WithoutNone();

		if (!split.TryPickT0(out var details, out _))
		{
			return new CallState("#-1 BAD ARGUMENT FORMAT TO ATTRIB_SET");
		}

		var (dbref, attribute) = details;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, enactor, executor, dbref, LocateFlags.All, async realLocated =>
			{
				var contents = args.TryGetValue("1", out var tmpContents)
					? tmpContents.Message!
					: MModule.empty();

				var setResult = await AttributeService!.SetAttributeAsync(executor, realLocated, attribute, contents);

				await NotifyService!.Notify(enactor,
					setResult.Match(
						_ => $"{realLocated.Object().Name}/{args["0"].Message} - Set.",
						failure => failure.Value)
				);

				return new CallState(setResult.Match(
					_ => string.Empty, // $"{realLocated.Object().Name}/{args["0"].Message}",
					failure => failure.Value));
			});
	}

	[SharpFunction(Name = "attrib_set#", MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["object/attribute"])]
	public static async ValueTask<CallState> AttributeSetSharp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var args = parser.CurrentState.Arguments;
		var split = HelperFunctions.SplitObjectAndAttr(MModule.plainText(args["0"].Message!));
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (!split.TryPickT0(out var details, out _))
		{
			return new CallState("#-1 BAD ARGUMENT FORMAT TO ATTRIB_SET");
		}

		var (dbref, attribute) = details;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, enactor, executor, dbref, LocateFlags.All, async realLocated =>
			{
				var contents = args.TryGetValue("1", out var tmpContents)
					? tmpContents.Message!
					: MModule.empty();

				var setResult = await AttributeService!.SetAttributeAsync(executor, realLocated, attribute, contents);

				await NotifyService!.Notify(enactor,
					setResult.Match(
						_ => $"{realLocated.Object().Name}/{args["0"].Message} - Set.",
						failure => failure.Value)
				);

				return new CallState(setResult.Match(
					_ => $"{realLocated.Object().Name}/{args["0"].Message}",
					failure => failure.Value));
			});
	}

	[SharpFunction(Name = "default", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["object/attribute", "default"])]
	public static async ValueTask<CallState> Default(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var defaultArg = parser.CurrentState.ArgumentsOrdered.Last().Value;
		var objAndAttrsToCheck = parser.CurrentState.ArgumentsOrdered.SkipLast(1).Select(x => x.Value);

		foreach (var objAndAttr in objAndAttrsToCheck)
		{
			var parsedMessage = await objAndAttr.ParsedMessage();
			var dbrefAndAttr = HelperFunctions.SplitObjectAndAttr(parsedMessage!.ToPlainText());
			var (dbref, attribute) = dbrefAndAttr.AsT0;

			if (dbrefAndAttr is { IsT1: true })
			{
				return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
			}

			var maybeFound = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
				parser,
				executor,
				executor,
				dbref,
				LocateFlags.All);

			if (!maybeFound.IsAnySharpObject)
			{
				return maybeFound.AsError;
			}

			var found = maybeFound.AsSharpObject;

			var maybeAttr = await AttributeService!.GetAttributeAsync(
				executor,
				found,
				attribute,
				mode: IAttributeService.AttributeMode.Execute,
				parent: false);

			if (!maybeAttr.IsAttribute)
			{
				continue;
			}

			return maybeAttr.AsCallState;
		}

		return await defaultArg.ParsedMessage();
	}

	/// <summary>
	/// Returns the first non-empty evaluated attribute value, or evaluates and returns the default value.
	/// Similar to default() but evaluates all arguments. Checks attributes in order until one has content.
	/// </summary>
	[SharpFunction(Name = "edefault", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["object/attribute", "default"])]
	public static async ValueTask<CallState> EvaluateDefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var defaultArg = parser.CurrentState.ArgumentsOrdered.Last().Value;
		var objAndAttrsToCheck = parser.CurrentState.ArgumentsOrdered.SkipLast(1).Select(x => x.Value);

		foreach (var objAndAttr in objAndAttrsToCheck)
		{
			var parsedMessage = await objAndAttr.ParsedMessage();
			var dbrefAndAttr = HelperFunctions.SplitObjectAndAttr(parsedMessage!.ToPlainText());
			var (dbref, attribute) = dbrefAndAttr.AsT0;

			if (dbrefAndAttr is { IsT1: true })
			{
				return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
			}

			var maybeFound = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
				parser,
				executor,
				executor,
				dbref,
				LocateFlags.All);

			if (!maybeFound.IsAnySharpObject)
			{
				return maybeFound.AsError;
			}

			var found = maybeFound.AsSharpObject;

			var maybeAttr = await AttributeService!.GetAttributeAsync(
				executor,
				found,
				attribute,
				mode: IAttributeService.AttributeMode.Execute,
				parent: false);

			if (!maybeAttr.IsAttribute)
			{
				continue;
			}

			return await parser.With(s => s with
				{
					Enactor = parser.CurrentState.Executor
				},
				async newParser => await AttributeService.EvaluateAttributeFunctionAsync(
					newParser,
					executor,
					found,
					attribute,
					parser.CurrentState.EnvironmentRegisters));
		}

		return await defaultArg.ParsedMessage();
	}

	[SharpFunction(Name = "eval", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["object", "attribute"])]
	public static async ValueTask<CallState> Eval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbref = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var attribute = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, dbref,
			LocateFlags.All,
			async actualObject => await parser.With(s => s with
					{
						Enactor = parser.CurrentState.Executor
					},
					async newParser => await AttributeService!.EvaluateAttributeFunctionAsync(
						newParser,
						executor,
						actualObject,
						attribute,
						parser.CurrentState.EnvironmentRegisters)));
	}

	[SharpFunction(Name = "flags", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Flags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (parser.CurrentState.Arguments.Count == 0)
		{
			var flags = Mediator!.CreateStream(new GetAllObjectFlagsQuery());
			return string.Join("", flags.Select(x => x.Symbol));
		}

		var dbrefAndAttr =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true }) // IsNone
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (obj, attributePattern) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
				if (attributePattern is null)
				{
					var flags = found.Object().Flags.Value;
					return string.Join("", await flags.Select(x => x.Symbol).ToArrayAsync());
				}

				var attr = await AttributeService!.LazilyGetAttributeAsync(
					executor, found, attributePattern, IAttributeService.AttributeMode.Read, false);

				return attr.Match(
					attribute => string.Join("", attribute.Last().Flags.Select(x => x.Symbol)),
					_ => Errors.ErrorNoSuchAttribute,
					error => error.Value);
			});
	}

	[SharpFunction(Name = "get", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object/attribute"])]
	public static async ValueTask<CallState> Get(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = (await parser.CurrentState.ExecutorObject(Mediator!)).WithoutNone();
		var dbrefAndAttr =
			HelperFunctions.SplitObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));

		if (dbrefAndAttr is { IsT1: true })
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (dbref, attribute) = dbrefAndAttr.AsT0;

		return await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			dbref,
			async x =>
			{
				var maybeAttr = await AttributeService!.GetAttributeAsync(
					executor,
					x,
					attribute,
					mode: IAttributeService.AttributeMode.Read,
					parent: false);

				return maybeAttr switch
				{
					{ IsError: true } or { IsNone: true } => maybeAttr.AsCallStateError,
					_ => new CallState(maybeAttr.AsAttribute.Last().Value)
				};
			});
	}

	[SharpFunction(Name = "get_eval", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["object/attribute"])]
	public static async ValueTask<CallState> GetEval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));

		if (dbrefAndAttr.IsT1) // IsNone
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(GetEval).ToUpper()));
		}

		var (dbref, attribute) = dbrefAndAttr.AsT0;

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, dbref,
			LocateFlags.All,
			async actualObject =>
			{
				return await parser.With(s => s with
					{
						Enactor = parser.CurrentState.Executor
					},
					async newParser => await AttributeService!.EvaluateAttributeFunctionAsync(
						newParser,
						executor,
						actualObject,
						attribute,
						parser.CurrentState.EnvironmentRegisters));
			});
	}

	[SharpFunction(Name = "grep", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern"])]
	public static ValueTask<CallState> Grep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return GrepInternal(parser, false, false);
	}

	[SharpFunction(Name = "pgrep", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern", "flags"])]
	public static ValueTask<CallState> ParentGrep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return GrepInternal(parser, false, true);
	}

	[SharpFunction(Name = "grepi", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern"])]
	public static ValueTask<CallState> GrepCaseInsensitive(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return GrepInternal(parser, true, false);
	}

	/// <summary>
	/// Internal helper for grep, grepi, pgrep.
	/// </summary>
	private static async ValueTask<CallState> GrepInternal(IMUSHCodeParser parser, bool caseInsensitive, bool checkParents)
	{
		var args = parser.CurrentState.Arguments;
		var objectStr = args["0"].Message!.ToPlainText();
		var attrsPattern = args["1"].Message!.ToPlainText();
		var substring = args["2"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, objectStr!, LocateFlags.All,
			async found =>
			{
				// Get all attributes matching the attribute pattern (wildcard)
				var attributes = await AttributeService!.GetAttributePatternAsync(executor, found,
					attrsPattern ?? "*", checkParents,
					IAttributeService.AttributePatternMode.Wildcard);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				var matchingAttrs = new List<string>();
				var comparison = caseInsensitive 
					? StringComparison.OrdinalIgnoreCase 
					: StringComparison.Ordinal;

				foreach (var attr in attributes.AsAttributes)
				{
					var value = attr.Value.ToPlainText();
					if (value != null && value.Contains(substring!, comparison))
					{
						matchingAttrs.Add(attr.LongName!);
					}
				}

				return string.Join(" ", matchingAttrs);
			});
	}

	[SharpFunction(Name = "hasattr", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "attribute"])]
	public static async ValueTask<CallState> HasAttribute(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.ArgumentsOrdered["0"].Message!.ToPlainText()!;
		var attribute = parser.CurrentState.ArgumentsOrdered["1"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			obj,
			LocateFlags.All,
			async found =>
			{
				var maybeAttr = await AttributeService!.GetAttributeAsync(
					await parser.CurrentState.KnownExecutorObject(Mediator!),
					found,
					attribute,
					mode: IAttributeService.AttributeMode.Read,
					parent: false);

				return new CallState(maybeAttr.IsAttribute ? "1" : "0");
			});
	}

	[SharpFunction(Name = "hasattrp", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "attribute"])]
	public static async ValueTask<CallState> HasAttributeParent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.ArgumentsOrdered["0"].Message!.ToPlainText()!;
		var attribute = parser.CurrentState.ArgumentsOrdered["1"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			obj,
			LocateFlags.All,
			async found =>
			{
				var maybeAttr = await AttributeService!.GetAttributeAsync(
					await parser.CurrentState.KnownExecutorObject(Mediator!),
					found,
					attribute,
					mode: IAttributeService.AttributeMode.Read,
					parent: true);

				return new CallState(maybeAttr.IsAttribute ? "1" : "0");
			});
	}

	[SharpFunction(Name = "hasattrpval", MinArgs = 2, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "pattern"])]
	public static async ValueTask<CallState> HasAttributeParentValue(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.ArgumentsOrdered["0"].Message!.ToPlainText()!;
		var attribute = parser.CurrentState.ArgumentsOrdered["1"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			obj,
			LocateFlags.All,
			async found =>
			{
				var maybeAttr = await AttributeService!.GetAttributeAsync(
					await parser.CurrentState.KnownExecutorObject(Mediator!),
					found,
					attribute,
					mode: IAttributeService.AttributeMode.Read,
					parent: true);

				// Check if attribute exists and has non-whitespace content
				// Returns "1" or "0" based on tiny_booleans config setting
				var hasValue = maybeAttr.IsAttribute && !string.IsNullOrWhiteSpace(maybeAttr.AsAttribute.Last().Value.ToPlainText());
				return new CallState(
					Configuration!.CurrentValue.Compatibility.TinyBooleans
						? (hasValue ? "1" : "0")
						: (hasValue ? "1" : "0"));
			});
	}

	[SharpFunction(Name = "hasattrval", MinArgs = 2, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "attribute"])]
	public static async ValueTask<CallState> HasAttributeValue(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var obj = parser.CurrentState.ArgumentsOrdered["0"].Message!.ToPlainText()!;
		var attribute = parser.CurrentState.ArgumentsOrdered["1"].Message!.ToPlainText()!;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			obj,
			LocateFlags.All,
			async found =>
			{
				var maybeAttr = await AttributeService!.GetAttributeAsync(
					await parser.CurrentState.KnownExecutorObject(Mediator!),
					found,
					attribute,
					mode: IAttributeService.AttributeMode.Read,
					parent: false);

				// Check if attribute exists and has non-whitespace content (no inheritance)
				// Returns "1" or "0" based on tiny_booleans config setting
				var hasValue = maybeAttr.IsAttribute && !string.IsNullOrWhiteSpace(maybeAttr.AsAttribute.Last().Value.ToPlainText());
				return new CallState(
					Configuration!.CurrentValue.Compatibility.TinyBooleans
						? (hasValue ? "1" : "0")
						: (hasValue ? "1" : "0"));
			});
	}

	[SharpFunction(Name = "hasflag", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flag"])]
	public static async ValueTask<CallState> HasFlag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objAndAttr = parser.CurrentState.Arguments["0"].Message!.ToString();
		var flagNameOrSymbol = parser.CurrentState.Arguments["1"].Message!.ToString();
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(objAndAttr);

		if (!split.TryPickT0(out var details, out _))
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(HasFlag)));
		}

		var (db, attr) = details;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, db, LocateFlags.All, async realLocated =>
			{
				return split.AsT0 switch
				{
					(_, null) => await HasObjectFlag(realLocated),
					_ => await HasAttributeFlag(realLocated)
				};
			});

		async ValueTask<CallState> HasObjectFlag(AnySharpObject realLocated)
		{
			return await realLocated.Object().Flags.Value.AnyAsync(f =>
				string.Equals(f.Name, flagNameOrSymbol, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(f.Symbol.ToString(), flagNameOrSymbol, StringComparison.OrdinalIgnoreCase));
		}

		async ValueTask<CallState> HasAttributeFlag(AnySharpObject realLocated)
		{
			var maybeAttr = await AttributeService!.GetAttributeAsync(
				executor,
				realLocated,
				attr!,
				IAttributeService.AttributeMode.Read,
				false);

			if (!maybeAttr.IsAttribute) return "0";

			return maybeAttr.AsAttribute.Last().Flags.Any(f =>
				string.Equals(f.Name, flagNameOrSymbol, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(f.Symbol.ToString(), flagNameOrSymbol, StringComparison.OrdinalIgnoreCase));
		}
	}

	[SharpFunction(Name = "lattr", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "pattern"])]
	public static async ValueTask<CallState> ListAttributes(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true }) // IsNone
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (obj, attributePattern) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, obj, LocateFlags.All,
			async found =>
			{
				var attributes = await AttributeService!.GetAttributePatternAsync(executor, found,
					attributePattern ?? "*", false,
					IAttributeService.AttributePatternMode.Wildcard);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				return string.Join(" ", attributes.AsAttributes.Select(x => x.LongName));
			});
	}

	[SharpFunction(Name = "lattrp", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "pattern"])]
	public static async ValueTask<CallState> ListAttributesParent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true }) // IsNone
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (obj, attributePattern) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, obj, LocateFlags.All,
			async found =>
			{
				var attributes = await AttributeService!.GetAttributePatternAsync(executor, found,
					attributePattern ?? "*", true,
					IAttributeService.AttributePatternMode.Wildcard);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				return string.Join(" ", attributes.AsAttributes.Select(x => x.LongName));
			});
	}

	[SharpFunction(Name = "lflags", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (parser.CurrentState.Arguments.Count == 0)
		{
			// List all flags known to the server
			var flags = Mediator!.CreateStream(new GetAllObjectFlagsQuery());
			return string.Join(" ", flags.Select(x => x.Name));
		}

		// List flags on an object or the object attribute
		var dbrefAndAttr =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true }) // IsNone
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (obj, attributePattern) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
				// Object Flags
				if (attributePattern is null)
				{
					var flags = found.Object().Flags.Value;
					return string.Join(" ", await flags.Select(x => x.Name).ToArrayAsync());
				}

				// Attribute Flags
				var attr = await AttributeService!.LazilyGetAttributeAsync(
					executor, found, attributePattern, IAttributeService.AttributeMode.Read, false);

				return attr.Match(
					attribute => string.Join(" ", attribute.Last().Flags.Select(x => x.Name)),
					_ => Errors.ErrorNoSuchAttribute,
					error => error.Value);
			});
	}

	[SharpFunction(Name = "nattr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> NumberAttributes(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true }) // IsNone
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (obj, attributePattern) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, obj,
			LocateFlags.All,
			async found =>
			{
				var attributes = await AttributeService!.GetAttributePatternAsync(executor, found,
					attributePattern ?? "*", false,
					IAttributeService.AttributePatternMode.Wildcard);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				return attributes.AsAttributes.Length;
			});
	}

	[SharpFunction(Name = "nattrp", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "pattern"])]
	public static async ValueTask<CallState> NumberAttributesParent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true }) // IsNone
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (obj, attributePattern) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, obj,
			LocateFlags.All,
			async found =>
			{
				var attributes = await AttributeService!.GetAttributePatternAsync(executor, found,
					attributePattern ?? "*", true,
					IAttributeService.AttributePatternMode.Wildcard);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				return attributes.AsAttributes.Length;
			});
	}

	[SharpFunction(Name = "obj", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object/attribute"])]
	public static async ValueTask<CallState> ObjectivePronoun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, arg0, LocateFlags.All,
			async onObject => await AttributeHelpers.GetPronoun(AttributeService!, Mediator!, parser, onObject,
				Configuration!.CurrentValue.Attribute.GenderAttribute,
				Configuration.CurrentValue.Attribute.ObjectivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "him",
					"F" or "Female" => "her",
					_ => "them"
				}));
	}

	[SharpFunction(Name = "objeval", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.NoParse, ParameterNames = ["object", "expression"])]
	public static async ValueTask<CallState> ObjectEvaluation(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var sideFxEnabled = Configuration!.CurrentValue.Function.FunctionSideEffects;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"];

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, arg0, LocateFlags.All,
			async found =>
			{
				var sideFxRequirement = sideFxEnabled
				                        && await PermissionService!.Controls(executor, found);
				var noSideFxRequirement = !sideFxEnabled
				                          && (await PermissionService!.Controls(executor, found) || await executor.IsSee_All());

				if (sideFxRequirement || noSideFxRequirement)
				{
					return (await parser.With(state =>
							state with
							{
								Executor = found.Object().DBRef
							},
						async newParser => await newParser.FunctionParse(arg1.Message!)))!;
				}

				return Errors.ErrorPerm;
			});
	}

	[SharpFunction(Name = "objid", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ObjectId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, arg0, LocateFlags.All,
			found => ValueTask.FromResult<CallState>(found.Object().DBRef));
	}

	[SharpFunction(Name = "objmem", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static ValueTask<CallState> ObjectMemory(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>("0");

	[SharpFunction(Name = "owner", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Owner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndMaybeArg =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = (await parser.CurrentState.ExecutorObject(Mediator!)).WithoutNone();

		if (dbrefAndMaybeArg is { IsT1: true, AsT1: false })
		{
			return new CallState(Errors.ErrorCantSeeThat);
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			dbrefAndMaybeArg.AsT0.db,
			LocateFlags.All,
			async actualObject =>
			{
				if (dbrefAndMaybeArg.AsT0.Attribute is null)
				{
					var objOwner = await actualObject.Object().Owner.WithCancellation(CancellationToken.None);
					return new CallState(objOwner.Object.DBRef
						.ToString());
				}

				var attribute = dbrefAndMaybeArg.AsT0.Attribute!;

				var attributeObject = await AttributeService!.GetAttributeAsync(executor, executor, attribute,
					IAttributeService.AttributeMode.Read, false);

				return attributeObject switch
				{
					{ IsNone: true } => new CallState(Errors.ErrorNoSuchAttribute),
					{ IsError: true } => new CallState(attributeObject.AsError.Value),
					{ AsAttribute: var attr } => (await attr.Last().Owner.WithCancellation(CancellationToken.None))!
						.Object
						.DBRef.ToString()
				};
			}
		);
	}

	[SharpFunction(Name = "poss", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> PossessivePronoun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, arg0, LocateFlags.All,
			async onObject => await AttributeHelpers.GetPronoun(AttributeService!, Mediator!, parser, onObject,
				Configuration!.CurrentValue.Attribute.GenderAttribute,
				Configuration.CurrentValue.Attribute.PossessivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "his",
					"F" or "Female" => "her",
					_ => "their"
				}));
	}

	[SharpFunction(Name = "regedit", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["string", "pattern", "replacement"])]
	public static async ValueTask<CallState> RegularExpressionEdit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return await RegEditInternal(parser, false, false);
	}

	[SharpFunction(Name = "regeditall", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["string", "pattern", "replacement"])]
	public static async ValueTask<CallState> RegularExpressionEditAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return await RegEditInternal(parser, false, true);
	}

	[SharpFunction(Name = "regeditalli", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["object", "pattern", "string", "replacement"])]
	public static async ValueTask<CallState> RegularExpressionAllCaseInsensitive(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		return await RegEditInternal(parser, true, true);
	}

	[SharpFunction(Name = "regediti", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["object", "pattern", "string", "replacement"])]
	public static async ValueTask<CallState> RegularExpressionEditCaseInsensitive(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		return await RegEditInternal(parser, true, false);
	}

	[SharpFunction(Name = "regrep", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["object", "attribute", "pattern"])]
	public static ValueTask<CallState> RegularExpressionGrep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegGrepInternal(parser, false);
	}

	[SharpFunction(Name = "regrepi", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["object", "attribute", "pattern"])]
	public static ValueTask<CallState> RegularExpressionGrepCaseInsensitive(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		return RegGrepInternal(parser, true);
	}

	/// <summary>
	/// Internal helper for regedit, regediti, regeditall, regeditalli.
	/// </summary>
	private static async ValueTask<CallState> RegEditInternal(IMUSHCodeParser parser, bool caseInsensitive, bool all)
	{
		// Get the string to edit - keep as MString
		var stringArg = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var mstr = stringArg!;
		var str = mstr.ToPlainText(); // For regex matching only
		
		// Get pattern/replacement pairs (remaining args after the first)
		var args = parser.CurrentState.ArgumentsOrdered.Skip(1).ToList();
		
		var options = RegexOptions.None;
		if (caseInsensitive)
		{
			options |= RegexOptions.IgnoreCase;
		}
		
		// Process pattern/replacement pairs (every 2 elements)
		for (int i = 0; i < args.Count - 1; i += 2)
		{
			var patternKv = args[i];
			var replaceKv = args[i + 1];
			
			var pattern = await patternKv.Value.ParsedMessage();
			var patternStr = pattern!.ToPlainText();
			var replaceTemplate = replaceKv.Value.Message!.ToPlainText();
			
			try
			{
				var regex = new Regex(patternStr, options);
				
				if (all)
				{
					// Replace all matches manually, working backwards to maintain indices
					var matches = regex.Matches(str!).Cast<Match>().Reverse().ToList();
					foreach (var match in matches)
					{
						var replacement = await EvaluateReplacement(parser, regex, match, replaceTemplate);
						// Use MModule.substring and MModule.concat to preserve markup
						var before = MModule.substring(0, match.Index, mstr);
						var after = MModule.substring(match.Index + match.Length, mstr.Length - match.Index - match.Length, mstr);
						mstr = MModule.concat(MModule.concat(before, MModule.single(replacement)), after);
						str = mstr.ToPlainText(); // Update plain text for next iteration
					}
				}
				else
				{
					// Replace only the first match
					var match = regex.Match(str!);
					if (match.Success)
					{
						var replacement = await EvaluateReplacement(parser, regex, match, replaceTemplate);
						// Use MModule.substring and MModule.concat to preserve markup
						var before = MModule.substring(0, match.Index, mstr);
						var after = MModule.substring(match.Index + match.Length, mstr.Length - match.Index - match.Length, mstr);
						mstr = MModule.concat(MModule.concat(before, MModule.single(replacement)), after);
						str = mstr.ToPlainText(); // Update plain text for next iteration
					}
				}
			}
			catch (ArgumentException)
			{
				return new CallState("#-1 REGEXP ERROR: Invalid regular expression");
			}
		}
		
		return new CallState(mstr);
	}

	/// <summary>
	/// Helper to evaluate a replacement template with captured groups.
	/// </summary>
	private static async ValueTask<string> EvaluateReplacement(IMUSHCodeParser parser, Regex regex, Match match, string template)
	{
		var replacement = template;
		
		// Replace $0, $1, etc. with captured groups
		for (int j = 0; j < match.Groups.Count; j++)
		{
			replacement = replacement.Replace($"${j}", match.Groups[j].Value);
		}
		
		// Replace named captures
		foreach (var groupName in regex.GetGroupNames().Where(groupName => !int.TryParse(groupName, out _)))
		{
			var group = match.Groups[groupName];
			if (group.Success)
			{
				replacement = replacement.Replace($"$<{groupName}>", group.Value);
			}
		}
		
		// Evaluate the replacement
		var evaluatedReplacement = await parser.FunctionParse(MModule.single(replacement));
		return evaluatedReplacement?.Message?.ToPlainText() ?? replacement;
	}

	/// <summary>
	/// Internal helper for regrep, regrepi - searches attributes for pattern matches.
	/// </summary>
	private static async ValueTask<CallState> RegGrepInternal(IMUSHCodeParser parser, bool caseInsensitive)
	{
		var args = parser.CurrentState.Arguments;
		var objectStr = args["0"].Message!.ToPlainText();
		var attrsPattern = args["1"].Message!.ToPlainText();
		var regexpPattern = args["2"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		try
		{
			var options = RegexOptions.None;
			if (caseInsensitive)
			{
				options |= RegexOptions.IgnoreCase;
			}
			
			var regex = new Regex(regexpPattern, options);
			
			// 1. Parse the object reference
			return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, executor, executor, objectStr, LocateFlags.All,
				async found =>
				{
					// 2. Get all attributes matching the attrsPattern (using wildcard pattern)
					var attributes = await AttributeService!.GetAttributePatternAsync(
						executor, 
						found,
						attrsPattern, 
						false,
						IAttributeService.AttributePatternMode.Wildcard);
					
					if (!attributes.IsAttribute)
					{
						return CallState.Empty;
					}
					
					var matchingAttributes = new List<string>();
					
					// 3. Filter attributes whose values match the regexpPattern
					foreach (var attr in attributes.AsAttributes)
					{
						var attrValue = attr.Value.ToPlainText();
						if (!string.IsNullOrEmpty(attrValue) && regex.IsMatch(attrValue))
						{
							matchingAttributes.Add(attr.Name);
						}
					}
					
					// 4. Return the list of matching attribute names (space-separated)
					return new CallState(string.Join(" ", matchingAttributes));
				});
		}
		catch (ArgumentException)
		{
			return new CallState("#-1 REGEXP ERROR: Invalid regular expression");
		}
	}

	[SharpFunction(Name = "reglattr", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern"])]
	public static async ValueTask<CallState> RegularExpressionListAttribute(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true }) // IsNone
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "reglattr".ToUpper()));
		}

		var (obj, attributePattern) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, obj, LocateFlags.All,
			async found =>
			{
				var attributes = await AttributeService!.GetAttributePatternAsync(executor, found,
					attributePattern ?? ".*", false,
					IAttributeService.AttributePatternMode.Regex);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				var separator = parser.CurrentState.Arguments.TryGetValue("1", out var sepArg)
					? sepArg.Message!.ToPlainText()
					: " ";

				return string.Join(separator, attributes.AsAttributes.Select(x => x.LongName));
			});
	}

	[SharpFunction(Name = "reglattrp", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern"])]
	public static async ValueTask<CallState> RegularExpressionListAttributeParent(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true }) // IsNone
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (obj, attributePattern) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, obj, LocateFlags.All,
			async found =>
			{
				var attributes = await AttributeService!.GetAttributePatternAsync(executor, found,
					attributePattern ?? ".*", true,
					IAttributeService.AttributePatternMode.Regex);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				var separator = parser.CurrentState.Arguments.TryGetValue("1", out var sepArg)
					? sepArg.Message!.ToPlainText()
					: " ";

				return string.Join(separator, attributes.AsAttributes.Select(x => x.LongName));
			});
	}

	[SharpFunction(Name = "regnattr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern"])]
	public static async ValueTask<CallState> RegularExpressionNumberAttributes(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true }) // IsNone
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (obj, attributePattern) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, obj,
			LocateFlags.All,
			async found =>
			{
				var attributes = await AttributeService!.GetAttributePatternAsync(executor, found,
					attributePattern ?? ".*", false,
					IAttributeService.AttributePatternMode.Regex);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				return attributes.AsAttributes.Length;
			});
	}

	[SharpFunction(Name = "regnattrp", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern"])]
	public static async ValueTask<CallState> RegularExpressionNumberAttributesParent(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true }) // IsNone
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (obj, attributePattern) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, obj,
			LocateFlags.All,
			async found =>
			{
				var attributes = await AttributeService!.GetAttributePatternAsync(executor, found,
					attributePattern ?? ".*", true,
					IAttributeService.AttributePatternMode.Regex);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				return attributes.AsAttributes.Length;
			});
	}

	[SharpFunction(Name = "regxattr", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern"])]
	public static async ValueTask<CallState> RegularExpressionNumberRangeAttributes(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var start = MModule.plainText(parser.CurrentState.Arguments["1"].Message!)!;
		var count = MModule.plainText(parser.CurrentState.Arguments["2"].Message!)!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true }) // IsNone
		{
			return string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper());
		}

		if (!int.TryParse(start, out var startInt) || !int.TryParse(count, out var countInt))
		{
			return Errors.ErrorInteger;
		}

		if (startInt < 1)
		{
			return Errors.ErrorArgRange;
		}

		var (obj, attributePattern) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, obj, LocateFlags.All,
			async found =>
			{
				var attributes = await AttributeService!.GetAttributePatternAsync(executor, found,
					attributePattern ?? ".*", false,
					IAttributeService.AttributePatternMode.Regex);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				var attributesStaging = attributes.AsAttributes.Skip(startInt - 1).Take(countInt);

				var separator = parser.CurrentState.Arguments.TryGetValue("3", out var sepArg)
					? sepArg.Message!.ToPlainText()
					: " ";

				return string.Join(separator!, attributesStaging.Select(x => x.LongName));
			});
	}

	[SharpFunction(Name = "regxattrp", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern"])]
	public static async ValueTask<CallState> RegularExpressionNumberRangeParent(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var start = MModule.plainText(parser.CurrentState.Arguments["1"].Message!)!;
		var count = MModule.plainText(parser.CurrentState.Arguments["2"].Message!)!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true }) // IsNone
		{
			return string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper());
		}

		if (!int.TryParse(start, out var startInt) || !int.TryParse(count, out var countInt))
		{
			return Errors.ErrorInteger;
		}

		if (startInt < 1)
		{
			return Errors.ErrorArgRange;
		}

		var (obj, attributePattern) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, obj, LocateFlags.All,
			async found =>
			{
				var attributes = await AttributeService!.GetAttributePatternAsync(executor, found,
					attributePattern ?? ".*", true,
					IAttributeService.AttributePatternMode.Regex);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				var attributesStaging = attributes.AsAttributes.Skip(startInt - 1).Take(countInt);

				var separator = parser.CurrentState.Arguments.TryGetValue("3", out var sepArg)
					? sepArg.Message!.ToPlainText()
					: " ";

				return string.Join(separator!, attributesStaging.Select(x => x.LongName));
			});
	}

	[SharpFunction(Name = "set", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["object/attribute", "flag or attribute:value"])]
	public static async ValueTask<CallState> Set(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!;
		var executor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		return (arg0, arg1) switch
		{
			// set(<object>/<attribute>, <attribute flag>)
			(_, _) when HelperFunctions.SplitObjectAndAttr(arg0) is { IsT0: true } split =>
				await SetAttributeFlag(split),

			// set(<object>, <attribute>:<value>)
			(_, _) when MModule.indexOf2(arg1, ":") > 1
				=> await SetAttributeValue(),

			// set(<object>, <flag>)
			_ => await SetObjectFlag()
		};

		async ValueTask<CallState> SetAttributeFlag(OneOf<(string db, string Attribute), None> split)
		{
			return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, executor, executor,
				split.AsT0.db, LocateFlags.All,
				async found =>
				{
					var result =
						await AttributeService!.SetAttributeFlagAsync(executor, found, split.AsT0.Attribute, arg1.ToPlainText());
					return result switch
					{
						{ IsT1: true } => result.AsT1,
						_ => new CallState(string.Empty)
					};
				});
		}

		async ValueTask<CallState> SetAttributeValue()
		{
			return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, executor, executor,
				arg0, LocateFlags.All,
				async found =>
				{
					var splitIndex = MModule.indexOf(arg1, MModule.single(":"));
					var attribute = MModule.substring(0, splitIndex, arg1);
					var value = MModule.substring(splitIndex + 1, arg1.Length - (splitIndex + 1), arg1);

					var result = await AttributeService!.SetAttributeAsync(executor, found, attribute.ToPlainText(), value);

					return result switch
					{
						{ IsT1: true } => result.AsT1,
						_ => new CallState(string.Empty)
					};
				});
		}

		async ValueTask<CallState> SetObjectFlag()
		{
			return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, executor, executor,
				arg0, LocateFlags.All,
				async found =>
				{
					var result = await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, found, arg1.ToPlainText(), false);
					
					// Return empty string on success, error message on failure
					return result.Message switch
					{
						{ } when result.Message!.ToPlainText() == "True" => string.Empty,
						_ => result
					};
				});
		}
	}

	[SharpFunction(Name = "subj", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> SubjectivePronoun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, arg0,
			LocateFlags.All,
			async onObject => await AttributeHelpers.GetPronoun(AttributeService!, Mediator!, parser, onObject,
				Configuration!.CurrentValue.Attribute.GenderAttribute,
				Configuration.CurrentValue.Attribute.SubjectivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "he",
					"F" or "Female" => "she",
					_ => "they"
				}));
	}

	[SharpFunction(Name = "udefault", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse, ParameterNames = ["object/attribute", "default..."])]
	public static async ValueTask<CallState> UserAttributeDefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true })
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (dbref, attribute) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, dbref, LocateFlags.All,
			async actualObject =>
			{
				var maybeAttr = await AttributeService!.GetAttributeAsync(
					executor,
					actualObject,
					attribute,
					mode: IAttributeService.AttributeMode.Execute,
					parent: false);

				var orderedArguments = parser.CurrentState.ArgumentsOrdered
					.Skip(1);

				if (!maybeAttr.IsAttribute)
				{
					return await orderedArguments.Skip(1).First().Value.ParsedMessage();
				}

				var get = maybeAttr.AsAttribute.Last();

				var arguments = await orderedArguments
					.SkipLast(1)
					.ToAsyncEnumerable()
					.Select(async (value, i, _) =>
						new KeyValuePair<string, CallState>(i.ToString(), new CallState(await value.Value.ParsedMessage())))
					.ToArrayAsync();

				return (await parser.With(s => s with
					{
						CurrentEvaluation = new DBAttribute(actualObject.Object().DBRef, get.Name),
						Arguments = arguments.ToDictionary(),
						EnvironmentRegisters = arguments.ToDictionary(),
					},
					async np => await np.FunctionParse(get.Value)))!;
			});
	}

	[SharpFunction(Name = "uldefault", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse | FunctionFlags.Localize, ParameterNames = ["object/attribute", "default..."])]
	public static async ValueTask<CallState> UserAttributeLocalizedDefault(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true })
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (dbref, attribute) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, dbref, LocateFlags.All,
			async actualObject =>
			{
				var maybeAttr = await AttributeService!.GetAttributeAsync(
					executor,
					actualObject,
					attribute,
					mode: IAttributeService.AttributeMode.Execute,
					parent: false);

				var orderedArguments = parser.CurrentState.ArgumentsOrdered
					.Skip(1);

				if (!maybeAttr.IsAttribute)
				{
					return await orderedArguments.Skip(1).First().Value.ParsedMessage();
				}

				var get = maybeAttr.AsAttribute.Last();

				var arguments = await orderedArguments
					.SkipLast(1)
					.ToAsyncEnumerable()
					.Select(async (value, i, _) =>
						new KeyValuePair<string, CallState>(i.ToString(), new CallState(await value.Value.ParsedMessage())))
					.ToArrayAsync();

				return (await parser.With(s => s with
					{
						CurrentEvaluation = new DBAttribute(actualObject.Object().DBRef, get.Name),
						Arguments = arguments.ToDictionary(),
						EnvironmentRegisters = arguments.ToDictionary(),
						Registers = []
					},
					async np => await np.FunctionParse(get.Value)))!;
			});
	}

	[SharpFunction(Name = "ufun", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular, ParameterNames = ["object/attribute", "arguments..."])]
	public static async ValueTask<CallState> UserAttributeFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executorMaybe = await parser.CurrentState.ExecutorObject(Mediator!);
		AnySharpObject executor;
		
		if (executorMaybe.IsT4)
		{
			// No executor in context (e.g., direct FunctionParse call), use #1 (God) as default
			var god = await Mediator!.Send(new GetObjectNodeQuery(new DBRef(1)));
			executor = god.Known();
		}
		else
		{
			executor = executorMaybe.Known();
		}

		var result = await AttributeService!.EvaluateAttributeFunctionAsync(
			parser,
			executor,
			objAndAttribute: parser.CurrentState.Arguments["0"].Message!,
			args: parser.CurrentState.Arguments.Skip(1)
				.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary(),
			ignoreLambda: true);

		return new CallState(result);
	}

	[SharpFunction(Name = "pfun", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular, ParameterNames = ["object", "function", "arguments..."])]
	public static async ValueTask<CallState> ParentFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr = parser.CurrentState.Arguments["0"].Message!;

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var parentObject = await executor.Object().Parent.WithCancellation(CancellationToken.None);

		if (parentObject.IsNone)
		{
			return new CallState("#-1 OBJECT HAS NO PARENT");
		}

		// Trust checking and attribute inheritance logic (no_inherit, INTERNAL flags, etc.)
		// should be implemented in EvaluateAttributeFunctionAsync for consistency
		// across all attribute evaluation contexts (ufun, pfun, get, etc.).
		// The evalParent=true parameter enables parent inheritance here.
		// Future work: Add trust checks and attribute flag filtering in AttributeService.

		var result = await AttributeService!.EvaluateAttributeFunctionAsync(parser, parentObject.Known,
			dbrefAndAttr,
			parser.CurrentState.Arguments.Skip(1)
				.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary(), true, false, true);

		return new CallState(result);
	}

	[SharpFunction(Name = "ulambda", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular, ParameterNames = ["parameters", "expression", "arguments..."])]
	public static async ValueTask<CallState> UserFunctionLambda(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var result = await AttributeService!.EvaluateAttributeFunctionAsync(
			parser,
			executor,
			objAndAttribute: parser.CurrentState.Arguments["0"].Message!,
			args: parser.CurrentState.Arguments.Skip(1)
				.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary());

		return new CallState(result);
	}

	[SharpFunction(Name = "ulocal", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.Localize, ParameterNames = ["object/attribute", "arguments..."])]
	public static async ValueTask<CallState> UserAttributeLocalized(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await parser.With(s => s with { Registers = [] },
			async np => await AttributeService!.EvaluateAttributeFunctionAsync(
				np,
				executor,
				objAndAttribute: parser.CurrentState.Arguments["0"].Message!,
				args: parser.CurrentState.EnvironmentRegisters)
		);
	}

	[SharpFunction(Name = "v", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["attribute"])]
	public static async ValueTask<CallState> Variable(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;

		return arg0.ToPlainText() switch
		{
			"#" => (await parser.CurrentState.KnownEnactorObject(Mediator!)).Object().DBRef,
			"@" => (await parser.CurrentState.KnownCallerObject(Mediator!)).Object().DBRef,
			"!" => (await parser.CurrentState.KnownExecutorObject(Mediator!)).Object().DBRef,
			"n" or "N" => (await parser.CurrentState.KnownEnactorObject(Mediator!)).Object().Name,
			"l" or "L" => (await (await parser.CurrentState.KnownEnactorObject(Mediator!)).Where()).Object().DBRef,
			"c" or "C" => Substitutions.Substitutions.LastCommandBeforeEvaluation(parser),
			var number when int.TryParse(number, out _)
				=> parser.CurrentState.EnvironmentRegisters.TryGetValue(number, out var value)
					? value
					: CallState.Empty,
			_ => Errors.ErrorArgRange
		};
	}

	[SharpFunction(Name = "valid", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["type", "name"])]
	public static async ValueTask<CallState> Valid(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var category = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var str = parser.CurrentState.Arguments["1"].Message!;
		var target = parser.CurrentState.Arguments.Count >= 3
			? parser.CurrentState.Arguments["2"].Message!.ToPlainText()
			: null;
		var caller = await parser.CurrentState.KnownCallerObject(Mediator!);

		var validationType = category switch
		{
			"name" => IValidateService.ValidationType.Name,
			"attrname" => IValidateService.ValidationType.AttributeName,
			"attrvalue" => IValidateService.ValidationType.AttributeValue,
			"playername" => IValidateService.ValidationType.PlayerName,
			"password" => IValidateService.ValidationType.Password,
			"command" => IValidateService.ValidationType.CommandName,
			"function" => IValidateService.ValidationType.FunctionName,
			"flag" => IValidateService.ValidationType.FlagName,
			"qreg" => IValidateService.ValidationType.QRegisterName,
			"colorname" => IValidateService.ValidationType.ColorName,
			"ansicodes" => IValidateService.ValidationType.AnsiCode,
			"channel" => IValidateService.ValidationType.ChannelName,
			"timezome" => IValidateService.ValidationType.Timezone,
			"locktype" => IValidateService.ValidationType.LockType,
			"lockkey" => IValidateService.ValidationType.LockKey,
			_ => IValidateService.ValidationType.Invalid
		};

		if (validationType == IValidateService.ValidationType.Invalid)
		{
			return string.Format(Errors.ErrorBadArgumentFormat, "valid");
		}

		return validationType switch
		{
			// TODO: TARGET ATTRIBUTE!
			// TODO: Mediator & Service for getting the entry.
			IValidateService.ValidationType.AttributeValue => await ValidateService!.Valid(validationType, str, new None()),

			IValidateService.ValidationType.PlayerName when target is null
				=> await ValidateService!.Valid(validationType, str, caller),
			IValidateService.ValidationType.PlayerName
				when await LocateService!.LocateAndNotifyIfInvalid(parser, caller, caller, target, LocateFlags.All)
					is { IsAnyObject: true, AsAnyObject: var obj }
				=> await ValidateService!.Valid(validationType, str, obj),
			IValidateService.ValidationType.PlayerName => Errors.ErrorCantSeeThat,

			IValidateService.ValidationType.ChannelName => await ValidateService!.Valid(validationType, str,
				await GetChannel(target ?? string.Empty)),

			IValidateService.ValidationType.LockType when target is null
				=> await ValidateService!.Valid(validationType, str, caller),
			IValidateService.ValidationType.LockType
				when await LocateService!.LocateAndNotifyIfInvalid(parser, caller, caller, target, LocateFlags.All)
					is { IsAnyObject: true, AsAnyObject: var obj }
				=> await ValidateService!.Valid(validationType, str, obj),
			IValidateService.ValidationType.LockType => Errors.ErrorCantSeeThat,
			_ => await ValidateService!.Valid(validationType, str, new None())
		};

		async ValueTask<OneOf<AnySharpObject, SharpAttributeEntry, SharpChannel, None>> GetChannel(string t)
		{
			var channel = await Mediator!.Send(new GetChannelQuery(t));
			return channel is null
				? new None()
				: channel;
		}
	}

	[SharpFunction(Name = "version", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public static ValueTask<CallState> Version(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(Implementation.Generated.VersionInfo.Version);

	[SharpFunction(Name = "visible", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "looker"])]
	public static async ValueTask<CallState> Visible(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;

		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var victimAttribute = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var victAttr = HelperFunctions.SplitDbRefAndOptionalAttr(victimAttribute);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (victAttr.IsT1)
		{
			return Errors.ErrorBadArgumentFormat;
		}

		var (victim, attr) = victAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, obj,
			LocateFlags.All,
			async foundObj =>
			{
				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, victim,
					LocateFlags.All,
					async foundVictim =>
					{
						if (attr is null)
						{
							return await PermissionService!.CanSee(foundObj, foundVictim);
						}

						var realAttr = await AttributeService!.GetAttributeAsync(executor, foundVictim, attr,
							IAttributeService.AttributeMode.Read, false);

						if (realAttr.IsError || realAttr.IsNone)
						{
							return false;
						}

						return await PermissionService!.CanViewAttribute(foundObj, foundVictim, realAttr.AsAttribute);
					});
			}
		);
	}

	[SharpFunction(Name = "wildgrep", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["object", "attribute", "pattern"])]
	public static ValueTask<CallState> WildcardGrep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return WildGrepInternal(parser, false);
	}

	[SharpFunction(Name = "wildgrepi", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["object", "attribute", "pattern"])]
	public static ValueTask<CallState> WildcardGrepCaseInsensitive(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return WildGrepInternal(parser, true);
	}

	/// <summary>
	/// Internal helper for wildgrep, wildgrepi.
	/// </summary>
	private static async ValueTask<CallState> WildGrepInternal(IMUSHCodeParser parser, bool caseInsensitive)
	{
		var args = parser.CurrentState.Arguments;
		var objectStr = args["0"].Message!.ToPlainText();
		var attrsPattern = args["1"].Message!.ToPlainText();
		var valuePattern = args["2"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, objectStr!, LocateFlags.All,
			async found =>
			{
				// Get all attributes matching the attribute pattern (wildcard)
				var attributes = await AttributeService!.GetAttributePatternAsync(executor, found,
					attrsPattern ?? "*", false,
					IAttributeService.AttributePatternMode.Wildcard);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				// Filter attributes whose values match the wildcard pattern
				var matchingAttrs = new List<string>();

				foreach (var attr in attributes.AsAttributes)
				{
					var value = attr.Value.ToPlainText();
					if (value != null)
					{
						var valueToMatch = caseInsensitive ? value.ToLower() : value;
						var patternToMatch = caseInsensitive ? valuePattern!.ToLower() : valuePattern;
						
						if (MModule.isWildcardMatch(MModule.single(valueToMatch), MModule.single(patternToMatch!)))
						{
							matchingAttrs.Add(attr.LongName!);
						}
					}
				}

				return string.Join(" ", matchingAttrs);
			});
	}

	[SharpFunction(Name = "xattr", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "pattern"])]
	public static async ValueTask<CallState> NumberRangeAttribute(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var start = MModule.plainText(parser.CurrentState.Arguments["1"].Message!)!;
		var count = MModule.plainText(parser.CurrentState.Arguments["2"].Message!)!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true }) // IsNone
		{
			return string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper());
		}

		if (!int.TryParse(start, out var startInt) || !int.TryParse(count, out var countInt))
		{
			return Errors.ErrorInteger;
		}

		if (startInt > countInt || startInt < 1)
		{
			return Errors.ErrorArgRange;
		}

		var (obj, attributePattern) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, obj, LocateFlags.All,
			async found =>
			{
				var attributes = await AttributeService!.GetAttributePatternAsync(executor, found,
					attributePattern ?? "*", false,
					IAttributeService.AttributePatternMode.Wildcard);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				var attributesStaging = attributes.AsAttributes.Skip(startInt).Take(countInt);

				var separator = parser.CurrentState.Arguments.TryGetValue("3", out var sepArg)
					? sepArg.Message!.ToPlainText()
					: " ";

				return string.Join(separator!, attributesStaging.Select(x => x.LongName));
			});
	}

	[SharpFunction(Name = "xattrp", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "pattern"])]
	public static async ValueTask<CallState> NumberRangeAttributeParent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDbRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var start = MModule.plainText(parser.CurrentState.Arguments["1"].Message!)!;
		var count = MModule.plainText(parser.CurrentState.Arguments["2"].Message!)!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (dbrefAndAttr is { IsT1: true }) // IsNone
		{
			return string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper());
		}

		if (!int.TryParse(start, out var startInt) || !int.TryParse(count, out var countInt))
		{
			return Errors.ErrorInteger;
		}

		if (startInt > countInt || startInt < 1)
		{
			return Errors.ErrorArgRange;
		}

		var (obj, attributePattern) = dbrefAndAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, obj, LocateFlags.All,
			async found =>
			{
				var attributes = await AttributeService!.GetAttributePatternAsync(executor, found,
					attributePattern ?? "*", true,
					IAttributeService.AttributePatternMode.Wildcard);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				var attributesStaging = attributes.AsAttributes.Skip(startInt).Take(countInt);

				var separator = parser.CurrentState.Arguments.TryGetValue("3", out var sepArg)
					? sepArg.Message!.ToPlainText()
					: " ";

				return string.Join(separator!, attributesStaging.Select(x => x.LongName));
			});
	}

	[SharpFunction(Name = "xget", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "attribute", "default"])]
	public static async ValueTask<CallState> AlternativeGet(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbref = MModule.plainText(parser.CurrentState.Arguments["0"].Message!);
		var attribute = MModule.plainText(parser.CurrentState.Arguments["1"].Message!);

		var executor = (await parser.CurrentState.ExecutorObject(Mediator!)).WithoutNone();
		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, dbref, LocateFlags.All,
			async actualObject =>
			{
				var maybeAttr = await AttributeService!.GetAttributeAsync(
					executor,
					actualObject,
					attribute,
					mode: IAttributeService.AttributeMode.Read,
					parent: false);

				return maybeAttr.AsCallState;
			});
	}

	[SharpFunction(Name = "zfun", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular, ParameterNames = ["zone", "attribute", "arguments..."])]
	public static async ValueTask<CallState> ZoneFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		
		// Get the zone object from enactor
		var zone = await enactor.Object().Zone.WithCancellation(CancellationToken.None);
		
		if (zone.IsNone)
		{
			return new CallState("#-1 NO ZONE SET");
		}
		
		// Evaluate the attribute function on the zone object
		var result = await AttributeService!.EvaluateAttributeFunctionAsync(
			parser,
			zone.Known,
			objAndAttribute: parser.CurrentState.Arguments["0"].Message!,
			args: parser.CurrentState.Arguments.Skip(1)
				.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary(),
			ignoreLambda: true);
		
		return new CallState(result);
	}
}