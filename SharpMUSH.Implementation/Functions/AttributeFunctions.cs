using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "APOSS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> aposs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "attrib_set", MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Attrib_Set(IMUSHCodeParser parser, SharpFunctionAttribute functionAttribute)
	{
		// TODO: If we have the NoSideFX flag, don't function! 
		// That should be handled by the parser before it gets here.

		var args = parser.CurrentState.Arguments;
		var split = HelperFunctions.SplitDBRefAndAttr(MModule.plainText(args["0"].Message!));
		var enactor = (await parser.CurrentState.EnactorObject(parser.Mediator)).WithoutNone();
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();

		if (!split.TryPickT0(out var details, out var _))
		{
			return new CallState("#-1 BAD ARGUMENT FORMAT TO ATTRIB_SET");
		}

		(var dbref, var attribute) = details;

		var locate = await parser.LocateService.LocateAndNotifyIfInvalid(parser,
			enactor,
			executor,
			dbref,
			Library.Services.LocateFlags.All);

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
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "EDEFAULT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> edefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "EVAL", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> eval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "FLAGS", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> flags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "get", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Get(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr = HelperFunctions.SplitDBRefAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		if (dbrefAndAttr.IsT1 && dbrefAndAttr.AsT1 == false)
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (dbref, attribute) = dbrefAndAttr.AsT0;
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();
		var maybeDBref = await parser.LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, dbref, Library.Services.LocateFlags.All);

		if (!maybeDBref.IsValid())
		{
			return new CallState(maybeDBref.IsError ? maybeDBref.AsError.Value : Errors.ErrorCantSeeThat);
		}

		var actualObject = maybeDBref.WithoutError().WithoutNone()!;
		var actualDBref = actualObject.Object().DBRef;

		var maybeAttr = await parser.AttributeService.GetAttributeAsync(
			executor,
			actualObject,
			attribute,
			mode: Library.Services.IAttributeService.AttributeMode.Read,
			parent: false);

		if (maybeAttr.IsError)
		{
			return new CallState(maybeAttr.AsError.Value);
		}
		else if (maybeAttr.IsNone)
		{
			return new CallState(string.Empty);
		}
		else
		{
			return new CallState(maybeAttr.AsAttribute.Value);
		}
	}

	[SharpFunction(Name = "GET_EVAL", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> get_eval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "GREP", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> grep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "PGREP", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> pgrep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "GREPI", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> grepi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "HASATTR", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> hasattr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "HASATTRP", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> hasattrp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "HASATTRPVAL", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> hasattrpval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "HASATTRVAL", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> hasattrval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "HASFLAG", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> hasflag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "LATTR", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> lattr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "LATTRP", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> lattrp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "LFLAGS", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> lflags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "NATTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> nattr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "NATTRP", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> nattrp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "OBJ", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> obj(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "OBJEVAL", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> objeval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "OBJID", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> objid(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "OBJMEM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> objmem(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "OWNER", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> owner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "POSS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> poss(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "REGEDIT", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> regedit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "REGEDITALL", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> regeditall(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "REGEDITALLI", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> regeditalli(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "REGEDITI", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> regediti(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "REGREP", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> regrep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "REGREPI", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> regrepi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "REGLATTR", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> reglattr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "REGLATTRP", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> reglattrp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "REGNATTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> regnattr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "REGNATTRP", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> regnattrp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "REGXATTR", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> regxattr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "REGXATTRP", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> regxattrp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "SET", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> set(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SETDIFF", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> setmanip(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "SETINTER", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> setinter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "SETSYMDIFF", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> setsmydiff(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "SETUNION", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> setunion(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var list1 = args["0"].Message;
		var list2 = args["1"].Message;
		var delimiter = NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var sortType = NoParseDefaultNoParseArgument(args, 3, MModule.single("m"));
		var outputSeparator = NoParseDefaultNoParseArgument(args, 4, delimiter);

		var aList1 = MModule.split2(delimiter, list1);
		var aList2 = MModule.split2(delimiter, list2);

		var result = aList1
			.Concat(aList2)
			.DistinctBy(MModule.plainText)
			.Aggregate((aggregated, next) => MModule.concat(aggregated, next));

		return new ValueTask<CallState>(new CallState(result));
	}
	[SharpFunction(Name = "SUBJ", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> subj(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	
	[SharpFunction(Name = "UDEFAULT", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> udefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr = HelperFunctions.SplitDBRefAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));

		if (dbrefAndAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (dbref, attribute) = dbrefAndAttr.AsT0;

		var executor = (await parser.Mediator.Send(new GetObjectNodeQuery(parser.CurrentState.Executor!.Value))).WithoutNone();
		var maybeDBref = await parser.LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, dbref, Library.Services.LocateFlags.All);

		if (!maybeDBref.IsValid())
		{
			return new CallState(maybeDBref.IsError ? maybeDBref.AsError.Value : Errors.ErrorCantSeeThat);
		}

		var actualObject = maybeDBref.WithoutError().WithoutNone();

		var maybeAttr = await parser.AttributeService.GetAttributeAsync(
			executor,
			actualObject,
			attribute,
			mode: Library.Services.IAttributeService.AttributeMode.Execute,
			parent: false);

		if (maybeAttr.IsError)
		{
			return new CallState(maybeAttr.AsError.Value);
		}

		var orderedArguments = parser.CurrentState.Arguments.OrderBy(x => int.Parse(x.Key))
			.Skip(1);
		
		if (maybeAttr.IsNone)
		{
			return new CallState(await orderedArguments.Last().Value.ParsedMessage());
		}
		
		var get = maybeAttr.AsAttribute;

		var newParser = parser.Push(parser.CurrentState with
		{
			CurrentEvaluation = new DBAttribute(actualObject.Object().DBRef, get.Name),
			Arguments = new(orderedArguments
				.SkipLast(1)
				.Select(
					(value, i) => new KeyValuePair<string, CallState>(
						i.ToString(), 
						new CallState(value.Value.ParsedMessage().GetAwaiter().GetResult())))
				.ToDictionary())
		});

		var parsed = await newParser.FunctionParse(get.Value);

		return parsed!;
	}
	
	[SharpFunction(Name = "ULDEFAULT", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse | FunctionFlags.Localize)]
	public static ValueTask<CallState> uldefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ufun", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Ufun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr = HelperFunctions.SplitDBRefAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));

		if (dbrefAndAttr.IsT1 && dbrefAndAttr.AsT1 == false)
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (dbref, attribute) = dbrefAndAttr.AsT0;

		var executor = (await parser.Mediator.Send(new GetObjectNodeQuery(parser.CurrentState.Executor!.Value))).WithoutNone();
		var maybeDBref = await parser.LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, dbref, Library.Services.LocateFlags.All);

		if (!maybeDBref.IsValid())
		{
			return new CallState(maybeDBref.IsError ? maybeDBref.AsError.Value : Errors.ErrorCantSeeThat);
		}

		var actualObject = maybeDBref.WithoutError().WithoutNone();

		var maybeAttr = await parser.AttributeService.GetAttributeAsync(
			executor,
			actualObject,
			attribute,
			mode: Library.Services.IAttributeService.AttributeMode.Execute,
			parent: false);

		if (maybeAttr.IsNone)
		{
			return new CallState(Errors.ErrorNoSuchAttribute);
		}

		if (maybeAttr.IsError)
		{
			return new CallState(maybeAttr.AsError.Value);
		}

		var get = maybeAttr.AsAttribute;

		var newParser = parser.Push(parser.CurrentState with
		{
			CurrentEvaluation = new DBAttribute(actualObject.Object().DBRef, get.Name),
			Arguments = new(parser.CurrentState.Arguments.Skip(1)
				.Select(
					(value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary())
		});

		var parsed = await newParser.FunctionParse(get.Value);

		return parsed!;
	}

	[SharpFunction(Name = "PFUN", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> pfun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ULAMBDA", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ulambda(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ULOCAL", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.Localize)]
	public static ValueTask<CallState> ulocal(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "V", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> v(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "VALID", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> valid(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "VERSION", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> version(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "VISIBLE", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> visible(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "WILDGREP", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> wildgrep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "WILDGREPI", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> wildgrepi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "XATTR", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> xattr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "XATTRP", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> xattrp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "XGET", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async static ValueTask<CallState> xget(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbref = MModule.plainText(parser.CurrentState.Arguments["0"].Message!);
		var attribute = MModule.plainText(parser.CurrentState.Arguments["1"].Message!);

		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();
		var maybeDBref = await parser.LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, dbref, Library.Services.LocateFlags.All);

		if (!maybeDBref.IsValid())
		{
			return new CallState(maybeDBref.IsError ? maybeDBref.AsError.Value : Errors.ErrorCantSeeThat);
		}

		var actualObject = maybeDBref.WithoutError().WithoutNone()!;
		var actualDBref = actualObject.Object().DBRef;

		var maybeAttr = await parser.AttributeService.GetAttributeAsync(
			executor,
			actualObject,
			attribute,
			mode: Library.Services.IAttributeService.AttributeMode.Read,
			parent: false);

		if (maybeAttr.IsError)
		{
			return new CallState(maybeAttr.AsError.Value);
		}
		else if (maybeAttr.IsNone)
		{
			return new CallState(string.Empty);
		}
		else
		{
			return new CallState(maybeAttr.AsAttribute.Value);
		}
	}
	[SharpFunction(Name = "ZFUN", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> zfun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}
