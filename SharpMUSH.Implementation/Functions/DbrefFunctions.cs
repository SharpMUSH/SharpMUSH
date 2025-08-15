using DotNext;
using MoreLinq.Extensions;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "loc", MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Loc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var maybeFound = await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0, LocateFlags.All);

		if (maybeFound.IsError)
		{
			return maybeFound.AsError;
		}

		if (maybeFound.AsSharpObject.IsContent)
		{
			var location = await maybeFound.AsSharpObject.AsContent.Location();
			return new(location.Object().DBRef);
		}
		else
		{
			return new(maybeFound.AsSharpObject.AsRoom.Object.DBRef);
		}
	}

	[SharpFunction(Name = "CHILDREN", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Children(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var maybeLocate = await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All);

		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError;
		}

		var locate = maybeLocate.AsSharpObject;
		var children = await locate.Object().Children.WithCancellation(CancellationToken.None);

		return new CallState(string.Join(" ", children.Select(x => x.DBRef.ToString())));
	}

	[SharpFunction(Name = "CON", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Con(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var maybeLocate = await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All);

		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError;
		}

		var locate = maybeLocate.AsSharpObject;

		if (!locate.IsContainer)
		{
			return CallState.Empty;
		}

		var contents = await locate.AsContainer.Content(parser);
		return new CallState(string.Join(" ", contents.Take(1).Select(x => x.Object().DBRef.ToString())));
	}

	[SharpFunction(Name = "CONTROLS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Controls(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		//   controls(<object>, <victim>[/<attribute>])
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		var arg1Split = arg1.Split('/');
		var isAttributeCheck = arg1Split.Length > 1;

		var maybeLocateObject = await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All);

		if (maybeLocateObject.IsError)
		{
			return maybeLocateObject.AsError;
		}

		var locateObject = maybeLocateObject.AsSharpObject;

		if (isAttributeCheck)
		{
			var attributeObj = arg1Split[0];
			var attribute = string.Join("/", arg1Split.Skip(1));
			var maybeLocateAttributeObject = await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
				executor,
				executor,
				attributeObj,
				LocateFlags.All);

			if (maybeLocateAttributeObject.IsError)
			{
				return maybeLocateAttributeObject.AsError;
			}

			var attributeObject = maybeLocateAttributeObject.AsSharpObject;

			var locateAttribute = await parser.AttributeService.GetAttributeAsync(executor, attributeObject, attribute, IAttributeService.AttributeMode.Read);

			if (locateAttribute.IsError)
			{
				return new CallState(locateAttribute.AsError.Value);
			}
			else if (locateAttribute.IsNone)
			{
				return new CallState(Errors.ErrorNotVisible);
			}

			// TODO: This should return an array. This needs changing.
			var foundAttribute = locateAttribute.AsAttribute;

			var controlsAttribute = await parser.PermissionService.Controls(locateObject, attributeObject, [foundAttribute]);

			return new CallState(controlsAttribute);
		}

		var maybeLocateVictim = await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			arg1,
			LocateFlags.All);

		if (maybeLocateVictim.IsError)
		{
			return maybeLocateVictim.AsError;
		}

		var locateVictim = maybeLocateVictim.AsSharpObject;

		var controls = await parser.PermissionService.Controls(locateObject, locateVictim);

		return new CallState(controls);
	}

	[SharpFunction(Name = "ENTRANCES", MinArgs = 0, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Entrances(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "EXIT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Exit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(parser.Mediator);
		var maybeLocate = await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All);

		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError;
		}

		var locate = maybeLocate.AsSharpObject;

		if (locate.IsPlayer && enactor.Object().DBRef == locate.Object().DBRef)
		{
			locate = (await locate.AsPlayer.Location.WithCancellation(CancellationToken.None)).WithExitOption();
		}

		if (!locate.IsRoom)
		{
			// TODO: Create a proper error constant for this.
			return new CallState("#-1 OBJECT IS NOT A ROOM");
		}

		var content = await locate.AsContainer.Content(parser);
		var exits = content.Where(x => x.IsExit).Select(x => x.Object().DBRef);

		return new CallState(exits.Any() ? exits.First().ToString() : "");
	}

	[SharpFunction(Name = "FOLLOWERS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Followers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "FOLLOWING", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Following(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "HOME", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Home(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var maybeLocate = await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All);

		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError;
		}

		if (maybeLocate.AsSharpObject.IsContent)
		{
			var home = await maybeLocate.AsSharpObject.AsContent.Home();
			return new(home.Object().DBRef);
		}

		// Implement DROP-TO behavior.
		return new("#-1 DROPTO TO BE IMPLEMENTED");
	}

	[SharpFunction(Name = "LLOCKFLAGS", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> LockFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ELOCK", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ELock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LLOCKS", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Locks(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LOCALIZE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> Localize(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LOCATE", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> locate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LOCK", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Lock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LOCKFILTER", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> lockfilter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LOCKOWNER", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> lockowner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LPARENT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> lparent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LSEARCH", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> lsearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LSEARCHR", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> lsearchr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NAMELIST", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> namelist(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var namelist = NameList(parser.CurrentState.Arguments["0"].Message!.ToPlainText());
		var (almostDbrefList, almostStrList) = namelist.Partition(x => x.IsT0);
		var dbrefList = Enumerable.ToHashSet(almostDbrefList.Select(x => x.AsT0));
		var strList = Enumerable.ToHashSet(almostStrList.Select(x => x.AsT1));

		var dbrefListExisting = await dbrefList
			.ToAsyncEnumerable()
			.WhereAwait(async x => await parser.Mediator.Send(new GetBaseObjectNodeQuery(x)) is not null)
			.ToHashSetAsync();

		// var dbrefListNotExisting = dbrefList.Except(dbrefListExisting); 

		var strListExisting = await strList
			.ToAsyncEnumerable()
			.SelectAwait(async x => (x, await parser.LocateService.Locate(parser,
				executor,
				executor,
				parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
				LocateFlags.All)))
			.Where(x => x.Item2.IsValid())
			.ToHashSetAsync();

		// var strListNotExisting = strList.Except(strListExisting.Select(x => x.x));

		var strListAsDbrefs = strListExisting.Select(x => x.Item2.AsAnyObject.Object().DBRef);

		var theGoodOnes = dbrefListExisting.Union(strListAsDbrefs);
		// TODO: obj/attr for evaluation of bad results.

		return new CallState(string.Join(" ", theGoodOnes));
	}

	[SharpFunction(Name = "NCHILDREN", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> nchildren(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var maybeLocate = await parser.LocateService.LocateAndNotifyIfInvalid(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All);

		switch (maybeLocate)
		{
			case { IsError: true }:
				return new CallState(maybeLocate.AsError.Value);
			case { IsNone: true }:
				return new CallState(Errors.ErrorNotVisible);
		}

		var locate = maybeLocate.AsAnyObject;
		var children = await locate.Object().Children.WithCancellation(CancellationToken.None);

		return new CallState(children.Count());
	}

	[SharpFunction(Name = "NEXT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> next(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NEXTDBREF", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> nextdbref(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NLSEARCH", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> nlsearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NSEARCH", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> nsearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NUM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> num(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NUMVERSION", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> numversion(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "PARENT", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> parent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "PMATCH", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> pmatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var result = await parser.LocateService.LocatePlayerAndNotifyIfInvalid(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText());

		return result switch
		{
			{ IsError: true } => new CallState(result.AsError.Value),
			{ IsNone: true } => new CallState(Errors.ErrorNotVisible),
			_ => new CallState(result.AsAnyObject.Object().DBRef.ToString())
		};
	}

	[SharpFunction(Name = "RLOC", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> rloc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ROOM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> room(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "WHERE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> where(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ZONE", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> zone(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XTHINGS", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> xthings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XVCON", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> xvcon(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XVEXITS", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> xvexits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XVPLAYERS", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> xvplayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XVTHINGS", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> xvthings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XCON", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> xcon(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XEXITS", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> xexits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XPLAYERS", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> xplayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LCON", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> lcon(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var maybeLocate = await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All);

		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError;
		}

		var locate = maybeLocate.AsSharpObject;
		if (!locate.IsContainer)
		{
			return new CallState("#-1 EXITS CANNOT CONTAIN THINGS");
		}

		var contents = await locate.AsContainer.Content(parser);

		return new CallState(string.Join(" ", contents.Select(x => x.Object().DBRef.ToString())));
	}

	[SharpFunction(Name = "LEXITS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> lexits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LPLAYERS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> lplayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LTHINGS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> lthings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LVCON", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> lvcon(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LVEXITS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> lvexits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LVPLAYERS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> lvplayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LVTHINGS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> lvthings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ORFLAGS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> orflags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ORLFLAGS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> orlflags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ORLPOWERS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> orlpowers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ANDFLAGS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> andflags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ANDLFLAGS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> andlflags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ANDLPOWERS", MinArgs = 2, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> andlpowers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NCON", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> dbwalker(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NEXITS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> nexits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NPLAYERS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> nplayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NTHINGS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> nthings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NVCON", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> nvcon(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NVEXITS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> nvexits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NVPLAYERS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> nvplayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NVTHINGS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> nvthings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}