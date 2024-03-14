using SharpMUSH.Implementation.Definitions;

namespace SharpMUSH.Implementation.Functions
{
	/*
		allof()
		ansi()
		atrlock()
		beep()
		benchmark()
		checkpass()
		clone()
		create()
		die()
		dig()
		endtag()
		firstof()
		functions()
		fn()
		html()
		ibreak()
		ilev()
		inum()
		isdbref()
		isint()
		isnum()
		isobjid()
		isregexp()
		isword()
		itext()
		letq()
		localize()
		link()
		list()
		listq()
		lnum()
		lset()
		null()
		numversion()
		objeval()
		open()
		r()
		rand()
		s()
		scan()
		set()
		setq()
		setr()
		slev()
		soundex()
		soundslike()
		speak()
		stext()
		suggest()
		tag()
		tagwrap()
		tel()
		testlock()
		textentries()
		textfile()
		unsetq()
		valid()
		wipe()
		@@()
		uptime()
	 */
	public partial class Functions
	{
		// TODO: Not compatible due to not being able to indicate a DBREF
		[SharpFunction(Name = "pcreate", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
		public static CallState PCreate(Parser parser, SharpFunctionAttribute _2)
		{
			var args = parser.State.Peek().Arguments;
			var location = parser.Database.GetObjectNode(new SharpMUSH.Library.Models.DBRef { Number = Configurable.PlayerStart }).Result;

			var trueLocation = location!.Value.Match(
				player => player.Object!.Key,
				room => room.Object!.Key,
				exit => exit.Object!.Key,
				thing => thing.Object!.Key);

			var created = parser.Database.CreatePlayer(
				args[0].Message!.ToString(), 
				args[1].Message!.ToString(), 
				new SharpMUSH.Library.Models.DBRef(trueLocation ?? 1)).Result;

			return new CallState($"#{created}");
		}

		[SharpFunction(Name = "ansi", MinArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState ANSI(Parser parser, SharpFunctionAttribute _2)
		{
			var args = parser.State.Peek().Arguments;

			// [1] contains the wrong message because CommandArg has been adding to the Arguments.
			return new CallState(args[1].Message);
		}
	}
}