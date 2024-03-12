using SharpMUSH.Implementation.Definitions;

namespace SharpMUSH.Implementation.Functions
{
	/*
		aposs()
		attrib_set()
		default()
		edefault()
		eval()
		flags()
		get()
		grep()
		grepi()
		hasattr()
		hasattrp()
		hasattrval()
		hasflag()
		lattr()
		lflags()
		nattr()
		obj()
		owner()
		pfun()
		poss()
		reglattr()
		regrep()
		regrepi()
		regxattr()
		set()
		subj()
		udefault()
		ufun()
		ulambda()
		uldefault()
		ulocal()
		v()
		wildgrep()
		wildgrepi()
		xattr()
		xget()
		zfun()
	*/
	public partial class Functions
	{
		[SharpFunction(Name = "attrib_set", MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState Attrib_Set(Parser parser, SharpFunctionAttribute _2)
		{
			// TODO: If we have the NoSideFX flag, don't function! 
			// That should be handled by the parser before it gets here.

			var args = parser.State.Peek().Arguments;
			var split = SplitDBRefAndAttr(MModule.plainText(args[0].Message!));

			if (!split.TryPickT0(out var details, out var _))
			{
				return new CallState("#-1 BAD ARGUMENT FORMAT TO ATTRIB_SET");
			}

			(var dbref, var attribute) = details;

			// TODO: Confirm DBRef Exists.
			// TODO: Confirm attribute is a valid path.
			// TODO: Confirm attribute exists.
			// TODO: Confirm Permissions


			// Clear on only having 1 arg. 
			// Write on having 2 args.
			if (args.Count == 1)
			{
				// Database.Clear.
			}
			else
			{
				var value = args[0].Message!.ToString();
				// TODO: Out of Band message of success, if they are not set QUIET.
				return new CallState(string.Empty);
			}

			throw new NotImplementedException();
		}
	}
}
