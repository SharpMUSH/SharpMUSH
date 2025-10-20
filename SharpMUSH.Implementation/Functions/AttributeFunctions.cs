using System.Reflection;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "aposs", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> AbsolutePossessivePronoun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, arg0,
			LocateFlags.All,
			async onObject => await AttributeHelpers.GetPronoun(AttributeService!, Mediator!, parser, onObject,
				Configuration!.CurrentValue.Attribute.GenderAttribute,
				Configuration!.CurrentValue.Attribute.AbsolutePossessivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "his",
					"F" or "Female" => "hers",
					_ => "theirs"
				}));
	}

	[SharpFunction(Name = "attrib_set", MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX)]
	public static async ValueTask<CallState> AttributeSet(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFX);
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

	[SharpFunction(Name = "default", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
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

	// TODO: Update Documentation so users know it's like default() now
	[SharpFunction(Name = "edefault", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
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

	[SharpFunction(Name = "eval", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Eval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbref = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var attribute = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, dbref,
			LocateFlags.All,
			async actualObject =>
			{
				var top2 = parser.State.Take(2).ToArray();
				var args = top2.Length > 1
					? top2.Last().Arguments
					: [];

				return await parser.With(s => s with
					{
						Enactor = parser.CurrentState.Executor
					},
					async newParser => await AttributeService!.EvaluateAttributeFunctionAsync(
						newParser,
						executor,
						actualObject,
						attribute,
						args));
			});
	}

	[SharpFunction(Name = "flags", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Flags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (parser.CurrentState.Arguments.Count == 0)
		{
			// List all flags known to the server
			var flags = await Mediator!.Send(new GetAllObjectFlagsQuery());
			return string.Join("", flags.Select(x => x.Symbol));
		}

		// List flags on an object or the object attribute
		var dbrefAndAttr =
			HelperFunctions.SplitDBRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
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
					var flags = await found.Object().Flags.WithCancellation(CancellationToken.None);
					return string.Join("", flags.Select(x => x.Symbol));
				}

				// Attribute Flags
				var attr = await AttributeService!.LazilyGetAttributeAsync(
					executor, found, attributePattern, IAttributeService.AttributeMode.Read, false);

				return attr.Match(
					attribute => string.Join("", attribute.Last().Flags.Select(x => x.Symbol)),
					_ => Errors.ErrorNoSuchAttribute,
					error => error.Value);
			});
	}

	[SharpFunction(Name = "get", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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

	[SharpFunction(Name = "get_eval", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
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

	[SharpFunction(Name = "GREP", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Grep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "PGREP", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ParentGrep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "GREPI", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> GrepCaseInsensitive(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "HASATTR", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> HasAttribute(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "HASATTRP", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> HasAttributeParent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "HASATTRPVAL", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> HasAttributeParentValue(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "HASATTRVAL", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> HasAttributeValue(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "HASFLAG", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> HasFlag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// hasflag(<object>[/<attrib>], <flag>)
		// Must look at full name, or alias.
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "lattr", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ListAttributes(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDBRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
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
				var attributes = await AttributeService!.LazilyGetAttributePatternAsync(executor, found,
					attributePattern ?? "*", false,
					IAttributeService.AttributePatternMode.Wildcard);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				return string.Join(" ", attributes.AsAttributes.Select(x => x.LongName));
			});
	}

	[SharpFunction(Name = "lattrp", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ListAttributesParent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDBRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
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
				var attributes = await AttributeService!.LazilyGetAttributePatternAsync(executor, found,
					attributePattern ?? "*", true,
					IAttributeService.AttributePatternMode.Wildcard);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				return string.Join(" ", attributes.AsAttributes.Select(x => x.LongName));
			});
	}

	[SharpFunction(Name = "lflags", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (parser.CurrentState.Arguments.Count == 0)
		{
			// List all flags known to the server
			var flags = await Mediator!.Send(new GetAllObjectFlagsQuery());
			return string.Join(" ", flags.Select(x => x.Name));
		}

		// List flags on an object or the object attribute
		var dbrefAndAttr =
			HelperFunctions.SplitDBRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
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
					var flags = await found.Object().Flags.WithCancellation(CancellationToken.None);
					return string.Join(" ", flags.Select(x => x.Name));
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

	[SharpFunction(Name = "nattr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> NumberAttributes(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDBRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
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
				var attributes = await AttributeService!.LazilyGetAttributePatternAsync(executor, found,
					attributePattern ?? "*", true,
					IAttributeService.AttributePatternMode.Wildcard);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				return await attributes.AsAttributes.CountAsync();
			});
	}

	[SharpFunction(Name = "nattrp", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> NumberAttributesParent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr =
			HelperFunctions.SplitDBRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
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
				var attributes = await AttributeService!.LazilyGetAttributePatternAsync(executor, found,
					attributePattern ?? "*", true,
					IAttributeService.AttributePatternMode.Wildcard);

				if (attributes.IsError)
				{
					return attributes.AsError;
				}

				return await attributes.AsAttributes.CountAsync();
			});
	}

	[SharpFunction(Name = "obj", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ObjectivePronoun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, arg0, LocateFlags.All,
			async onObject => await AttributeHelpers.GetPronoun(AttributeService!, Mediator!, parser, onObject,
				Configuration!.CurrentValue.Attribute.GenderAttribute,
				Configuration!.CurrentValue.Attribute.ObjectivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "him",
					"F" or "Female" => "her",
					_ => "them"
				}));
	}

	[SharpFunction(Name = "objeval", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.NoParse)]
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

	[SharpFunction(Name = "objid", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ObjectId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, arg0, LocateFlags.All,
			found => ValueTask.FromResult<CallState>(found.Object().DBRef));
	}

	[SharpFunction(Name = "OBJMEM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ObjectMemory(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>("0");

	[SharpFunction(Name = "owner", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Owner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndMaybeArg =
			HelperFunctions.SplitDBRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
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

	[SharpFunction(Name = "poss", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> PossessivePronoun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, arg0, LocateFlags.All,
			async onObject => await AttributeHelpers.GetPronoun(AttributeService!, Mediator!, parser, onObject,
				Configuration!.CurrentValue.Attribute.GenderAttribute,
				Configuration!.CurrentValue.Attribute.PossessivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "his",
					"F" or "Female" => "her",
					_ => "their"
				}));
	}

	[SharpFunction(Name = "REGEDIT", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> RegularExpressionEdit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGEDITALL", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> RegularExpressionEditAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGEDITALLI", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> RegularExpressionAllCaseInsensitive(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGEDITI", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> RegularExpressionEditCaseInsensitive(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGREP", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionGrep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGREPI", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionGrepCaseInsensitive(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGLATTR", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionListAttribute(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGLATTRP", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionListAttributeParent(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGNATTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionNumberAttributes(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGNATTRP", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionNumberAttributesParent(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGXATTR", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionNumberRangeAttributes(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGXATTRP", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionNumberRangeParent(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SET", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Set(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFX);
		}

		// set(<object>, <flag>)
		// set(<object>/<attribute>, <attribute flag>)
		// set(<object>, <attribute>:<value>)

		throw new NotImplementedException();
	}

	[SharpFunction(Name = "subj", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> SubjectivePronoun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, arg0,
			LocateFlags.All,
			async onObject => await AttributeHelpers.GetPronoun(AttributeService!, Mediator!, parser, onObject,
				Configuration!.CurrentValue.Attribute.GenderAttribute,
				Configuration!.CurrentValue.Attribute.SubjectivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "he",
					"F" or "Female" => "she",
					_ => "they"
				}));
	}

	[SharpFunction(Name = "udefault", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse)]
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

	[SharpFunction(Name = "uldefault", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse | FunctionFlags.Localize)]
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

	[SharpFunction(Name = "ufun", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> UserAttributeFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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

	[SharpFunction(Name = "pfun", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> ParentFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr = parser.CurrentState.Arguments["0"].Message!;

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var parentObject = await executor.Object().Parent.WithCancellation(CancellationToken.None);

		if (parentObject.IsNone)
		{
			return new CallState("#-1 OBJECT HAS NO PARENT");
		}

		// TODO: CHECK TRUST AGAINST OBJECT
		// TODO: Logic should live in EvaluateAttributeFunctionAsync, as it also needs to start considering 
		// 'INTERNAL' etc attributes, that are not actually inheritable.
		// Also, debug?

		var result = await AttributeService!.EvaluateAttributeFunctionAsync(parser, parentObject.Known,
			dbrefAndAttr,
			parser.CurrentState.Arguments.Skip(1)
				.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary(), true, false);

		return new CallState(result);
	}

	[SharpFunction(Name = "ULAMBDA", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> UserFunctionLambda(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ulocal", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.Localize)]
	public static async ValueTask<CallState> UserAttributeLocalized(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await parser.With(s => s with
			{
				Registers = []
			},
			async np => await AttributeService!.EvaluateAttributeFunctionAsync(
				parser,
				executor,
				objAndAttribute: parser.CurrentState.Arguments["0"].Message!,
				args: parser.CurrentState.Arguments.Skip(1)
					.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
					.ToDictionary())
		);
	}

	[SharpFunction(Name = "v", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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
			var number when int.TryParse(number, out _) => parser.StateHistory(2)
				.Match(
					state => state.ArgumentsOrdered.TryGetValue(number, out var value)
						? value.Message
						: MModule.empty(),
					_ => MModule.empty()),
			_ => Errors.ErrorRange
		};
	}

	[SharpFunction(Name = "VALID", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Valid(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "VERSION", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Version(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty);

	[SharpFunction(Name = "VISIBLE", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Visible(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		/*
		 visible(<object>, <victim>[/<attribute>])

		If no attribute name is provided, this function returns 1 if <object> can examine <victim>, or 0, if it cannot. If an attribute name is given, the function returns 1 if <object> can see the attribute <attribute> on <victim>, or 0, if it cannot.

		If <object>, <victim>, or <attribute> is invalid, the function returns 0.

		 */

		throw new NotImplementedException();
	}

	[SharpFunction(Name = "WILDGREP", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> WildcardGrep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "WILDGREPI", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> WildcardGrepCaseInsensitive(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XATTR", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberRangeAttribute(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XATTRP", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberRangeAttributeParent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "xget", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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

	[SharpFunction(Name = "ZFUN", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ZoneFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}