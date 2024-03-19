using SharpMUSH.Implementation.Definitions;

namespace SharpMUSH.Implementation.Functions
{
	public partial class Functions
	{
		[SharpFunction(Name = "APOSS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState aposs(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "attrib_set", MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState Attrib_Set(Parser parser, SharpFunctionAttribute _2)
		{
			// TODO: If we have the NoSideFX flag, don't function! 
			// That should be handled by the parser before it gets here.

			var args = parser.CurrentState().Arguments;
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

		[SharpFunction(Name = "DEFAULT", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState Default(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "EDEFAULT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.NoParse)]
		public static CallState edefault(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "EVAL", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState eval(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "FLAGS", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState flags(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "GET", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState get(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "GET_EVAL", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState get_eval(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "GREP", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState grep(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "PGREP", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState pgrep(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "GREPI", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState grepi(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "HASATTR", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState hasattr(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "HASATTRP", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState hasattrp(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "HASATTRPVAL", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState hasattrpval(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "HASATTRVAL", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState hasattrval(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "HASFLAG", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState hasflag(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "LATTR", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState lattr(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "LATTRP", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState lattrp(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "LFLAGS", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState lflags(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "NATTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState nattr(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "NATTRP", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState nattrp(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "OBJ", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState obj(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "OBJEVAL", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.NoParse)]
		public static CallState objeval(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "OBJID", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState objid(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "OBJMEM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState objmem(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "OWNER", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState owner(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "POSS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState poss(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "REGEDIT", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState regedit(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "REGEDITALL", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState regeditall(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "REGEDITALLI", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState regeditalli(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "REGEDITI", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState regediti(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "REGREP", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState regrep(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "REGREPI", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState regrepi(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "REGLATTR", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState reglattr(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "REGLATTRP", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState reglattrp(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "REGNATTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState regnattr(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "REGNATTRP", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState regnattrp(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "REGXATTR", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState regxattr(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "REGXATTRP", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState regxattrp(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SET", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState set(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SETQ", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
		public static CallState setq(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SETR", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
		public static CallState setr(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SETDIFF", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
		public static CallState setmanip(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SETINTER", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
		public static CallState setinter(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SETSYMDIFF", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
		public static CallState setsmydiff(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SETUNION", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
		public static CallState setunion(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SUBJ", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState subj(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "UDEFAULT", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse)]
		public static CallState udefault(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ULDEFAULT", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse | FunctionFlags.Localize)]
		public static CallState uldefault(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "UFUN", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
		public static CallState ufun(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "PFUN", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
		public static CallState pfun(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ULAMBDA", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
		public static CallState ulambda(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ULOCAL", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.Localize)]
		public static CallState ulocal(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "V", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState v(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "VALID", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState valid(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "VERSION", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
		public static CallState version(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "VISIBLE", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState visible(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "WILDGREP", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState wildgrep(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "WILDGREPI", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState wildgrepi(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "XATTR", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState xattr(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "XATTRP", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState xattrp(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "XGET", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState xget(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ZFUN", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular)]
		public static CallState zfun(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "VADD", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState vadd(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "VCROSS", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState vcross(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "VSUB", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState vsub(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "VMAX", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState vmax(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "VMIN", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState vmin(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "VMUL", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState vmul(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "VDOT", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState vdot(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "VMAG", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState vmag(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "VUNIT", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState vunit(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
	}
}
