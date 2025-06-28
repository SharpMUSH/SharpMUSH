using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "APOSS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> aposs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "attrib_set", MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX)]
	public static async ValueTask<CallState> AttributeSet(IMUSHCodeParser parser,
		SharpFunctionAttribute functionAttribute)
	{
		if(parser.Configuration.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFX);
		}
		
		var args = parser.CurrentState.Arguments;
		var split = HelperFunctions.SplitObjectAndAttr(MModule.plainText(args["0"].Message!));
		var enactor = (await parser.CurrentState.EnactorObject(parser.Mediator)).WithoutNone();
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();

		if (!split.TryPickT0(out var details, out _))
		{
			return new CallState("#-1 BAD ARGUMENT FORMAT TO ATTRIB_SET");
		}

		var (dbref, attribute) = details;

		var locate = await parser.LocateService.LocateAndNotifyIfInvalid(parser,
			enactor,
			executor,
			dbref,
			LocateFlags.All);

		// Arguments are getting here in an evaluated state, when they should not be.
		if (!locate.IsValid())
		{
			return new CallState(locate.IsError ? locate.AsError.Value : Errors.ErrorCantSeeThat);
		}

		var realLocated = locate.WithoutError().WithoutNone();
		var contents = args.TryGetValue("1", out var tmpContents) ? tmpContents.Message! : MModule.empty();

		var setResult = await parser.AttributeService.SetAttributeAsync(executor, realLocated, attribute, contents);
		await parser.NotifyService.Notify(enactor,
			setResult.Match(
				_ => $"{realLocated.Object().Name}/{args["0"].Message} - Set.",
				failure => failure.Value)
		);

		return new CallState(setResult.Match(
			_ => string.Empty, // $"{realLocated.Object().Name}/{args["0"].Message}",
			failure => failure.Value));
	}

	[SharpFunction(Name = "DEFAULT", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> Default(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// default([<obj>/]<attr>[, ... ,[<objN>]/<attrN>], <default>)
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "EDEFAULT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> edefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// edefault([<obj>/]<attr>, <default case>)
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "EVAL", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Eval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr = HelperFunctions.SplitObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));

		if (dbrefAndAttr is { IsT1: true })
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (dbref, attribute) = dbrefAndAttr.AsT0;

		var executor =
			(await parser.Mediator.Send(new GetObjectNodeQuery(parser.CurrentState.Executor!.Value))).WithoutNone();
		var maybeDBref =
			await parser.LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, dbref, LocateFlags.All);

		if (!maybeDBref.IsValid())
		{
			return new CallState(maybeDBref.IsError ? maybeDBref.AsError.Value : Errors.ErrorCantSeeThat);
		}

		var actualObject = maybeDBref.WithoutError().WithoutNone();

		var maybeAttr = await parser.AttributeService.GetAttributeAsync(
			executor,
			actualObject,
			attribute,
			mode: IAttributeService.AttributeMode.Execute,
			parent: false);

		if (!maybeAttr.IsAttribute)
		{
			return maybeAttr.AsCallStateError;
		}

		var get = maybeAttr.AsAttribute;

		var top2 = parser.State.Take(2).ToArray();
		var lastArguments = top2.Length > 1 ? top2.Last().Arguments : [];

		var newParser = parser.Push(parser.CurrentState with
		{
			CurrentEvaluation = new DBAttribute(actualObject.Object().DBRef, get.Name),
			Arguments = lastArguments,
			Enactor = parser.CurrentState.Executor
		});

		return (await newParser.FunctionParse(get.Value))!;
	}

	[SharpFunction(Name = "FLAGS", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Flags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (string.IsNullOrWhiteSpace(parser.CurrentState.Arguments["0"].Message!.ToPlainText()))
		{
			var allFlags = await parser.Mediator.Send(new GetAllObjectFlagsQuery());
			return new CallState(string.Join(" ", allFlags));
		}

		// TODO: Implement version that looks at attribute flags.
		// TODO: Implement locate() and look at the player, after checking permissions.
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();
		var flags = await executor.Object().Flags.WithCancellation(CancellationToken.None);

		return new CallState(String.Join(" ", flags.Select(x => x.Name)));
	}

	[SharpFunction(Name = "get", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Get(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr = HelperFunctions.SplitObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));

		if (dbrefAndAttr is { IsT1: true })
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (dbref, attribute) = dbrefAndAttr.AsT0;
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();
		var maybeDBref =
			await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, dbref, LocateFlags.All);

		if (maybeDBref.IsError)
		{
			return maybeDBref.AsError;
		}

		var actualObject = maybeDBref.AsSharpObject;

		var maybeAttr = await parser.AttributeService.GetAttributeAsync(
			executor,
			actualObject,
			attribute,
			mode: IAttributeService.AttributeMode.Read,
			parent: false);

		return maybeAttr switch
		{
			{ IsError: true } => new CallState(maybeAttr.AsError.Value),
			{ IsNone: true } => new CallState(string.Empty),
			_ => new CallState(maybeAttr.AsAttribute.Value)
		};
	}

	[SharpFunction(Name = "GET_EVAL", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> GetEval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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

	[SharpFunction(Name = "LATTR", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ListAttributes(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LATTRP", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ListAttributesParent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LFLAGS", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NATTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberAttributes(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NATTRP", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberAttributesParent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "OBJ", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ObjectivePronoun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "OBJEVAL", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> ObjectEvaluation(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "OBJID", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ObjectId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "OBJMEM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ObjectMemory(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "OWNER", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Owner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndMaybeArg =
			HelperFunctions.SplitDBRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();

		if (dbrefAndMaybeArg is { IsT1: true, AsT1: false })
		{
			return new CallState(Errors.ErrorCantSeeThat);
		}

		var locatedObject = await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(
			parser,
			executor,
			executor,
			dbrefAndMaybeArg.AsT0.db,
			LocateFlags.All
		);

		if (locatedObject.IsError)
		{
			return locatedObject.AsError;
		}

		var actualObject = locatedObject.AsSharpObject;

		if (dbrefAndMaybeArg.AsT0.Attribute is not null)
		{
			return new CallState((await actualObject.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef
				.ToString());
		}

		var attribute = dbrefAndMaybeArg.AsT0.Attribute!;

		var attributeObject = await parser.AttributeService.GetAttributeAsync(executor, executor, attribute,
			IAttributeService.AttributeMode.Read, false);

		return attributeObject switch
		{
			{ IsNone: true } => new CallState(Errors.ErrorNoSuchAttribute),
			{ IsError: true } => new CallState(attributeObject.AsError.Value),
			_ => new CallState((await attributeObject.AsAttribute.Owner.WithCancellation(CancellationToken.None))!.Object
				.DBRef.ToString())
		};
	}

	[SharpFunction(Name = "POSS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> poss(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Should consider the Config for Possessive Form.
		// parser.Configuration.CurrentValue.Attribute.PossessivePronounAttribute
		throw new NotImplementedException();
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
	public static ValueTask<CallState> RegularExpressionAllCaseInsensitive(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGEDITI", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> RegularExpressionEditCaseInsensitive(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGREP", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionGrep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGREPI", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionGrepCaseInsensitive(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGLATTR", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionListAttribute(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGLATTRP", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionListAttributeParent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGNATTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionNumberAttributes(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGNATTRP", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionNumberAttributesParent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGXATTR", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionNumberRangeAttributes(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REGXATTRP", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RegularExpressionNumberRangeParent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SET", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Set(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		if(parser.Configuration.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFX);
		}
		
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SUBJ", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> SubjectivePronoun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "UDEFAULT", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> UserAttributeDefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr = HelperFunctions.SplitObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));

		if (dbrefAndAttr is { IsT1: true })
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (dbref, attribute) = dbrefAndAttr.AsT0;

		var executor =
			(await parser.Mediator.Send(new GetObjectNodeQuery(parser.CurrentState.Executor!.Value))).WithoutNone();
		var maybeDBref =
			await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, dbref,
				LocateFlags.All);

		if (maybeDBref.IsError)
		{
			return maybeDBref.AsError;
		}

		var actualObject = maybeDBref.AsSharpObject;

		var maybeAttr = await parser.AttributeService.GetAttributeAsync(
			executor,
			actualObject,
			attribute,
			mode: IAttributeService.AttributeMode.Execute,
			parent: false);

		var orderedArguments = parser.CurrentState.ArgumentsOrdered
			.Skip(1);

		if (!maybeAttr.IsAttribute)
		{
			return maybeAttr.AsCallStateError;
		}

		var get = maybeAttr.AsAttribute;

		var arguments = await orderedArguments
			.SkipLast(1)
			.ToAsyncEnumerable()
			.SelectAwait(
				async (value, i) => new KeyValuePair<string, CallState>(
					i.ToString(),
					new CallState(await value.Value.ParsedMessage())))
			.ToArrayAsync();

		var newParser = parser.Push(parser.CurrentState with
		{
			CurrentEvaluation = new DBAttribute(actualObject.Object().DBRef, get.Name),
			Arguments = new(arguments.ToDictionary())
		});

		return (await newParser.FunctionParse(get.Value))!;
	}

	[SharpFunction(Name = "ULDEFAULT", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse | FunctionFlags.Localize)]
	public static ValueTask<CallState> UserAttributeLocalizedDefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ufun", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> UserAttributeFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);

		var result = await parser.AttributeService.EvaluateAttributeFunctionAsync(
			parser, 
			executor, 
			objAndAttribute: parser.CurrentState.Arguments["0"].Message!, 
			args: parser.CurrentState.Arguments.Skip(1)
				.Select(
					(value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary());

		return new CallState(result);
	}

	[SharpFunction(Name = "PFUN", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> ParentFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr = parser.CurrentState.Arguments["0"].Message!;

		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var parentObject = await executor.Object().Parent.WithCancellation(CancellationToken.None);

		if (parentObject.IsNone)
		{
			return new CallState("#-1 OBJECT HAS NO PARENT");
		}
		
		// TODO: CHECK TRUST AGAINST OBJECT
		// TODO: Logic should live in EvaluateAttributeFunctionAsync, as it also needs to start considering 
		// 'INTERNAL' etc attributes, that are not actually inhertable.
		// Also, debug?

		var result = await parser.AttributeService.EvaluateAttributeFunctionAsync(parser, parentObject.Known, 
			dbrefAndAttr, 
			parser.CurrentState.Arguments.Skip(1)
				.Select(
					(value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary(), true, false);

		return new CallState(result);
	}

	[SharpFunction(Name = "ULAMBDA", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> UserFunctionLambda(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ULOCAL", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.Localize)]
	public static ValueTask<CallState> UserAttributeLocalized(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "V", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Variable(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "VALID", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Valid(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "VERSION", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Version(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "VISIBLE", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Visible(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
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

	[SharpFunction(Name = "XGET", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> AlternativeGet(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbref = MModule.plainText(parser.CurrentState.Arguments["0"].Message!);
		var attribute = MModule.plainText(parser.CurrentState.Arguments["1"].Message!);

		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();
		var maybeDBref =
			await parser.LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, dbref, LocateFlags.All);

		if (!maybeDBref.IsValid())
		{
			return new CallState(maybeDBref.IsError ? maybeDBref.AsError.Value : Errors.ErrorCantSeeThat);
		}

		var actualObject = maybeDBref.WithoutError().WithoutNone();

		var maybeAttr = await parser.AttributeService.GetAttributeAsync(
			executor,
			actualObject,
			attribute,
			mode: IAttributeService.AttributeMode.Read,
			parent: false);

		return maybeAttr switch
		{
			{ IsError: true } => new CallState(maybeAttr.AsError.Value),
			{ IsNone: true } => new CallState(string.Empty),
			_ => new CallState(maybeAttr.AsAttribute.Value)
		};
	}

	[SharpFunction(Name = "ZFUN", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ZoneFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}