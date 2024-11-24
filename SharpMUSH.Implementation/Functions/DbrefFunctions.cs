using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
		[SharpFunction(Name = "loc", MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static async ValueTask<CallState> Loc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				var dbRefConversion = HelperFunctions.ParseDBRef(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
				if (dbRefConversion.IsNone())
				{
						await parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, "I can't see that here.");
						return new CallState("#-1");
				}

				var dbRef = dbRefConversion.AsValue();
				var objectInfo = await parser.Mediator.Send(new GetObjectNodeQuery(dbRef));

				// TODO: Check the type, as an Exit doesn't return the right thing or Loc on a Location Search.
				// It has a few things that return different results.
				// A room returns #-1 if there is no DROP-TO set.
				var id = objectInfo!.Match(
					player => player.Id,
					room => room.Id,
					exit => exit.Id,
					thing => thing.Id,
					none => null
				);

				// TODO: Do the regular search otherwise.
				// TODO: Permissions Check?

				throw new NotImplementedException(nameof(Loc));
		}

		[SharpFunction(Name = "CHILDREN", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static ValueTask<CallState> Children(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				throw new NotImplementedException();
		}

		[SharpFunction(Name = "CON", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static ValueTask<CallState> Con(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				throw new NotImplementedException();
		}

		[SharpFunction(Name = "CONTROLS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static ValueTask<CallState> Controls(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				throw new NotImplementedException();
		}

		[SharpFunction(Name = "ENTRANCES", MinArgs = 0, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static ValueTask<CallState> Entrances(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				throw new NotImplementedException();
		}

		[SharpFunction(Name = "EXIT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static ValueTask<CallState> Exit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				throw new NotImplementedException();
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
		public static ValueTask<CallState> Home(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				throw new NotImplementedException();
		}

		[SharpFunction(Name = "LLOCKFLAGS", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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

		[SharpFunction(Name = "LOCKFILTER", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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
		public static ValueTask<CallState> namelist(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				throw new NotImplementedException();
		}

		[SharpFunction(Name = "NCHILDREN", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static ValueTask<CallState> nchildren(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				throw new NotImplementedException();
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
		public static ValueTask<CallState> pmatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				throw new NotImplementedException();
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
				var dbRefConversion = HelperFunctions.ParseDBRef(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
				if (dbRefConversion.IsNone())
				{
						await parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, "I can't see that here.");
						return new CallState("#-1");
				}

				var contents = await parser.Mediator.Send(new GetContentsQuery(dbRefConversion.AsValue()));
				if (contents is null)
				{
						await parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, "I can't see that here.");
						return new CallState("#-1");
				}

				return new CallState(string.Join(" ", contents!.Select(x => x.Object()!.DBRef.ToString())));
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

		[SharpFunction(Name = "ANDLPOWERS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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