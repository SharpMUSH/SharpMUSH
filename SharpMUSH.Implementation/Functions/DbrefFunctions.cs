using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Functions
{
	/*
		children()
		con()
		entrances()
		exit()
		followers()
		following()
		home()
		lcon()
		lexits()
		loc()
		locate()
		lparent()
		lplayers()
		lsearch()
		lvcon()
		lvexits()
		lvplayers()
		namelist()
		next()
		nextdbref()
		num()
		owner()
		parent()
		pmatch()
		rloc()
		rnum()
		room()
		where()
		zone()
	*/
	public partial class Functions
	{
		[SharpFunction(Name = "loc", MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Loc(Parser parser, SharpFunctionAttribute _2)
		{
			var dbRefConversion = ParseDBRef(MModule.plainText(parser.CurrentState().Arguments[0].Message));
			if(!dbRefConversion.TryPickT0(out var dbRef, out _))
			{
				parser.NotifyService.Notify(parser.CurrentState().Executor, "I can't see that here.");
				return new CallState("#-1");
			}

			var objectInfo = parser.Database.GetObjectNode(dbRef).Result;

			// TODO: Check the type, as an Exit doesn't return the right thing or Loc on a Location Search.
			// It has a few things that return different results.
			// A room returns #-1 if there is no DROP-TO set.
			var id = objectInfo!.Value.Match(
				player => player.Id,
				room => room.Id,
				exit => exit.Id,
				thing => thing.Id
				);

			// TODO: Do the regular search otherwise.
			// TODO: Permissions Check?

			throw new NotImplementedException(nameof(Loc));
		}
	}
}
