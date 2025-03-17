using System.Collections.Concurrent;
using Core.Arango.Protocol;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
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

	[SharpFunction(Name = "attrib_set", MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> AttributeSet(IMUSHCodeParser parser,
		SharpFunctionAttribute functionAttribute)
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
		var dbrefAndAttr = HelperFunctions.SplitDBRefAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));

		if (dbrefAndAttr is { IsT1: true, AsT1: false })
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

		switch (maybeAttr)
		{
			case { IsNone: true }:
				return new CallState(Errors.ErrorNoSuchAttribute);
			case { IsError : true }:
				return new CallState(maybeAttr.AsError.Value);
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
		var dbrefAndAttr = HelperFunctions.SplitDBRefAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));

		if (dbrefAndAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (dbref, attribute) = dbrefAndAttr.AsT0;
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();
		var maybeDBref =
			await parser.LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, dbref, LocateFlags.All);

		if (!maybeDBref.IsValid())
		{
			return new CallState(maybeDBref.IsError ? maybeDBref.AsError.Value : Errors.ErrorCantSeeThat);
		}

		var actualObject = maybeDBref.WithoutError().WithoutNone()!;

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

	[SharpFunction(Name = "HASATTRPVAL", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> hasattrpval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "HASATTRVAL", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> hasattrval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "HASFLAG", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> hasflag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// hasflag(<object>[/<attrib>], <flag>)
		// Must look at full name, or alias.
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
	public static async ValueTask<CallState> Owner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndMaybeArg =
			HelperFunctions.SplitDBRefAndOptionalAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();

		if (dbrefAndMaybeArg is { IsT1: true, AsT1: false })
		{
			return new CallState(Errors.ErrorCantSeeThat);
		}

		var locatedObject = await parser.LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			dbrefAndMaybeArg.AsT0.db,
			LocateFlags.All
		);

		switch (locatedObject)
		{
			case { IsNone: true }:
				return new CallState(Errors.ErrorCantSeeThat);
			case { IsError: true }:
				return new CallState(locatedObject.AsError.Value);
		}

		var actualObject = locatedObject.WithoutError().WithoutNone();

		if (dbrefAndMaybeArg.AsT0.Attribute is not null)
		{
			return new CallState((await actualObject.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef.ToString());
		}

		var attribute = dbrefAndMaybeArg.AsT0.Attribute!;

		var attributeObject = await parser.AttributeService.GetAttributeAsync(executor, executor, attribute,
			IAttributeService.AttributeMode.Read, false);

		return attributeObject switch
		{
			{ IsNone: true } => new CallState(Errors.ErrorNoSuchAttribute),
			{ IsError: true } => new CallState(attributeObject.AsError.Value),
			_ => new CallState((await attributeObject.AsAttribute.Owner.WithCancellation(CancellationToken.None))!.Object.DBRef.ToString())
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

	[SharpFunction(Name = "SUBJ", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> subj(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "UDEFAULT", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> UDefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr = HelperFunctions.SplitDBRefAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));

		if (dbrefAndAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, nameof(Get).ToUpper()));
		}

		var (dbref, attribute) = dbrefAndAttr.AsT0;

		var executor =
			(await parser.Mediator.Send(new GetObjectNodeQuery(parser.CurrentState.Executor!.Value))).WithoutNone();
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
			mode: IAttributeService.AttributeMode.Execute,
			parent: false);

		var orderedArguments = parser.CurrentState.ArgumentsOrdered
			.Skip(1);

		switch (maybeAttr)
		{
			case { IsNone: true }:
				return new CallState(Errors.ErrorNoSuchAttribute);
			case { IsError: true }:
				return new CallState(maybeAttr.AsError.Value);
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
	public static ValueTask<CallState> uldefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ufun", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> UFun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndAttr = HelperFunctions.SplitDBRefAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message));

		if (dbrefAndAttr is { IsT1: true, AsT1: false })
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

		switch (maybeAttr)
		{
			case { IsNone: true }:
				return new CallState(Errors.ErrorNoSuchAttribute);
			case { IsError : true }:
				return new CallState(maybeAttr.AsError.Value);
		}

		var get = maybeAttr.AsAttribute;

		var newParser = parser.Push(parser.CurrentState with
		{
			CurrentEvaluation = new DBAttribute(actualObject.Object().DBRef, get.Name),
			Arguments = new ConcurrentDictionary<string, CallState>(parser.CurrentState.Arguments.Skip(1)
				.Select(
					(value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary())
		});

		return (await newParser.FunctionParse(get.Value))!;
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
	public static async ValueTask<CallState> XGet(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
	public static ValueTask<CallState> zfun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}