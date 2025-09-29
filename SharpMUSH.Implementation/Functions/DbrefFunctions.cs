using DotNext;
using MoreLinq.Extensions;
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
using SharpMUSH.Implementation;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "loc", MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Location(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0,
				LocateFlags.All) switch
			{
				{ IsError: true, AsError: var error } => error,
				{ AsSharpObject: { IsContent: true } found } => (await found.AsContent.Location()).Object().DBRef,
				var container => container.AsSharpObject.AsRoom.Object.DBRef
			};
	}

	[SharpFunction(Name = "CHILDREN", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Children(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				var children = await locate.Object().Children.WithCancellation(CancellationToken.None);
				return string.Join(" ", children.Select(x => x.DBRef.ToString()));
				
			} );
	}

	[SharpFunction(Name = "CON", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Con(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return CallState.Empty;
				}

				var contents = await locate.AsContainer.Content(Mediator!);
				return string.Join(" ", contents.Take(1).Select(x => x.Object().DBRef.ToString()));
			});
	}

	[SharpFunction(Name = "CONTROLS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Controls(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		var arg1Split = arg1.Split('/');
		var isAttributeCheck = arg1Split.Length > 1;

		var maybeLocateObject = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
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
			var maybeLocateAttributeObject = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
				executor,
				executor,
				attributeObj,
				LocateFlags.All);

			if (maybeLocateAttributeObject.IsError)
			{
				return maybeLocateAttributeObject.AsError;
			}

			var attributeObject = maybeLocateAttributeObject.AsSharpObject;

			var locateAttribute = await AttributeService!.GetAttributeAsync(executor, attributeObject, attribute,
				IAttributeService.AttributeMode.Read);

			if (locateAttribute.IsError)
			{
				return locateAttribute.AsError.Value;
			}
			if (locateAttribute.IsNone)
			{
				return Errors.ErrorNotVisible;
			}

			var foundAttribute = locateAttribute.AsAttribute;

			var controlsAttribute = await PermissionService!.Controls(locateObject, attributeObject, foundAttribute);

			return controlsAttribute;
		}

		var maybeLocateVictim = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			arg1,
			LocateFlags.All);

		if (maybeLocateVictim.IsError)
		{
			return maybeLocateVictim.AsError;
		}

		var locateVictim = maybeLocateVictim.AsSharpObject;

		var controls = await PermissionService!.Controls(locateObject, locateVictim);

		return controls;
	}

	[SharpFunction(Name = "ENTRANCES", MinArgs = 0, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Entrances(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "EXIT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Exit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (locate.IsPlayer && enactor.Object().DBRef == locate.Object().DBRef)
				{
					locate = (await locate.AsPlayer.Location.WithCancellation(CancellationToken.None)).WithExitOption();
				}

				if (!locate.IsRoom)
				{
					// TODO: Create a proper error constant for this.
					return "#-1 OBJECT IS NOT A ROOM";
				}

				// Todo: Turn Content into async enumerable.
				var exits = (await locate.AsContainer.Content(Mediator!))
					.Where(x => x.IsExit)
					.Select(x => x.Object().DBRef)
					.ToArray();

				return exits.Length != 0
					? exits.First().ToString()
					: string.Empty;
			});
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
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, arg0, LocateFlags.All,
			async found =>
			{
				if (found.IsContent)
				{
					return (await found.AsContent.Home()).Object().DBRef;
				}

				// Implement DROP-TO behavior.
				return "#-1 DROPTO TO BE IMPLEMENTED";
			});
	}

	[SharpFunction(Name = "LLOCKFLAGS", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> LockFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ELOCK", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> EvaluateLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LLOCKS", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Locks(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LOCALIZE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> Localize(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await parser
			   .With(
				   x => x with { Registers = [] },
				   newParser => newParser.FunctionParse(parser.CurrentState.Arguments["0"].Message!))
		   ?? CallState.Empty;

	[SharpFunction(Name = "LOCATE", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Locate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
	public static ValueTask<CallState> LockFilter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LOCKOWNER", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> LockOwner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LPARENT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ListParents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var maybeLocate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All);

		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError;
		}

		var locate = maybeLocate.AsSharpObject;
		var list = new List<DBRef>();

		while (true)
		{
			var parent = await locate.Object().Parent.WithCancellation(CancellationToken.None);
			if (parent.IsNone)
			{
				break;
			}

			var knownParent = parent.Known;
			if (!await PermissionService!.CanExamine(executor, knownParent))
			{
				break;
			}

			locate = knownParent;
			list.Add(knownParent.Object().DBRef);
		}

		return string.Join(" ", list);
	}

	[SharpFunction(Name = "LSEARCH", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ListSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LSEARCHR", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ListSearchRegex(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NAMELIST", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> NameList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var namelist = Common.ArgHelpers.NameList(parser.CurrentState.Arguments["0"].Message!.ToPlainText());
		var (almostDbrefList, almostStrList) = namelist.Partition(x => x.IsT0);
		var dbrefList = Enumerable.ToHashSet(almostDbrefList.Select(x => x.AsT0));
		var strList = Enumerable.ToHashSet(almostStrList.Select(x => x.AsT1));

		var dbrefListExisting = await dbrefList
			.ToAsyncEnumerable()
			.Where(async (x, ct) => await Mediator!.Send(new GetBaseObjectNodeQuery(x), ct) is not null)
			.ToHashSetAsync();

		// var dbrefListNotExisting = dbrefList.Except(dbrefListExisting); 

		var strListExisting = await strList
			.ToAsyncEnumerable()
			.Select<string, (string x, AnyOptionalSharpObjectOrError)>(async (x, _) => (x, await LocateService!.Locate(
				parser,
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

		return string.Join(" ", theGoodOnes);
	}

	[SharpFunction(Name = "NCHILDREN", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> NumberOfChildren(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg1 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, arg1, LocateFlags.All,
			async x =>
			{
				var children = await x.Object().Children.WithCancellation(CancellationToken.None);
				return children.Count();
			});
	}

	[SharpFunction(Name = "NEXT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Next(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NEXTDBREF", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NextDbReference(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NLSEARCH", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NumberOfListSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NSEARCH", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NumberOfSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NUM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Number(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			found =>
				ValueTask.FromResult<CallState>(found.Object().DBRef));
	}

	[SharpFunction(Name = "NUMVERSION", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NumVersion(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "parent", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Parent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var arg0 = args["0"].Message!.ToPlainText()!;
		var arg1 = args.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;
		
		if (arg1 == null)
		{
			return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, executor, executor, arg0, LocateFlags.All,
				async found =>
					(await found.Object().Parent.WithCancellation(CancellationToken.None)).Object()
					?.DBRef.ToString() ?? "");
		}

		if (Configuration!.CurrentValue.Function.FunctionSideEffects == false)
		{
			return Errors.ErrorNoSideFX;
		}
		
		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, args["0"].Message!.ToPlainText(), LocateFlags.All,
			async target =>
			{
				if (!await PermissionService!.Controls(executor, target))
				{
					return Errors.ErrorPerm;
				}
				
				switch(args)
				{
					case { Count: 1 }:
					case { Count: 2 } when args["1"].Message!.ToPlainText().Equals("none", StringComparison.InvariantCultureIgnoreCase): 
						await Mediator!.Send(new UnsetObjectParentCommand(target));
						return CallState.Empty;
					default: 
						return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
							parser, executor, executor, args["1"].Message!.ToPlainText(), LocateFlags.All,
							async newParent =>
							{
								if (!await PermissionService.Controls(executor, newParent) 
								    || (!await target.HasFlag("LINK_OK") 
								        && !PermissionService.PassesLock(executor, newParent, LockType.Parent)))
								{
									return Errors.ErrorPerm;
								}

								if (!await HelperFunctions.SafeToAddParent(target, newParent))
								{
									return CallState.Empty;
								}
									
								await Mediator!.Send(new SetObjectParentCommand(target, newParent));
								return CallState.Empty;
							}
						);
				}
			}
		);
	}

	[SharpFunction(Name = "pmatch", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> PlayerMatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			x => ValueTask.FromResult<CallState>(x.Object.DBRef));
	}

	[SharpFunction(Name = "RLOC", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> RecursiveLocation(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "room", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Room(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async x =>
			{
				var room = await LocateService!.Room(x); 
				return room.Object().DBRef;
			});
	}

	[SharpFunction(Name = "where", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Where(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async x =>
				await x.Match<ValueTask<string>>(
					async player => (await player.Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString(), 
					_ => ValueTask.FromResult<string>("#-1 THIS IS A ROOM"), 
					// TODO: Exit may need editing
					async exit => (await exit.Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString(),
					async thing => (await thing.Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString()));
	}

	[SharpFunction(Name = "ZONE", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Zone(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XTHINGS", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ExtractThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XVCON", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ExtractVisualContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XVEXITS", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ExtractVisualExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XVPLAYERS", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ExtractVisualPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XVTHINGS", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ExtractVisualThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XCON", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ExtractContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XEXITS", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ExtractExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XPLAYERS", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ExtractPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LCON", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ListContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return  Errors.ExitsCannotContainThings;
				}

				return string.Join(" ", (await locate.AsContainer.Content(Mediator!))
					.Select(x => x.Object().DBRef.ToString()));
			});
	}

	[SharpFunction(Name = "lexits", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ListExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return  Errors.ExitsCannotContainThings;
				}

				return string.Join(" ", (await locate.AsContainer.Content(Mediator!))
					.Where(x => x.IsExit)
					.Select(x => x.Object().DBRef.ToString()));
			});
	}

	[SharpFunction(Name = "lplayers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ListPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return  Errors.ExitsCannotContainThings;
				}

				return string.Join(" ", (await locate.AsContainer.Content(Mediator!))
					.Where(x => x.IsPlayer)
					.Select(x => x.Object().DBRef.ToString()));
			});
	}

	[SharpFunction(Name = "lthings", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ListThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return  Errors.ExitsCannotContainThings;
				}

				return string.Join(" ", (await locate.AsContainer.Content(Mediator!))
					.Where(x => x.IsThing)
					.Select(x => x.Object().DBRef.ToString()));
			});
	}

	[SharpFunction(Name = "LVCON", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ListVisualContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LVEXITS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ListVisualExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LVPLAYERS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ListVisualPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LVTHINGS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ListVisualThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ORFLAGS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> OrFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ORLFLAGS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> OrListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ORLPOWERS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> OrListPowers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ANDFLAGS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> AndFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ANDLFLAGS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> AndListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ANDLPOWERS", MinArgs = 2, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> AndListPowers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NCON", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> dbwalker(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NEXITS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberOfExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NPLAYERS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberOfPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NTHINGS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberOfThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NVCON", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberOfVisualContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NVEXITS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberOfVisualExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NVPLAYERS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberOfVisualPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NVTHINGS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberOfVisualThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}