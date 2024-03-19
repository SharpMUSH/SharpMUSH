using SharpMUSH.Implementation.Definitions;

namespace SharpMUSH.Implementation.Functions
{
	public static partial class Functions
	{
		[SharpFunction(Name = "after", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState After(Parser parser, SharpFunctionAttribute _2)
		{
			var args = parser.CurrentState().Arguments;
			var fullString = args[0]!.Message;
			var search = args[1]!.Message;
			var idx = MModule.indexOf(fullString, search);

			return new CallState(MModule.substring(idx, MModule.getLength(fullString) - idx, args[0].Message));
		}

		[SharpFunction(Name = "strcat", Flags = FunctionFlags.Regular)]
		public static CallState Concat(Parser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState().Arguments
					.Select(x => x.Message)
					.Aggregate((x,y) => MModule.concat(x,y)));

		[SharpFunction(Name = "cat", Flags = FunctionFlags.Regular)]
		public static CallState Cat(Parser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState().Arguments
					.Select(x => x.Message)
					.Aggregate((x, y) => MModule.concat(x, y, MModule.single(" "))));

		[SharpFunction(Name = "ACCENT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState accent(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ALIGN", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
		public static CallState align(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "LALIGN", MinArgs = 2, MaxArgs = 6, Flags = FunctionFlags.Regular)]
		public static CallState lalign(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ALPHAMAX", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState alphamax(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ALPHAMIN", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState alphamin(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ART", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState art(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "BEFORE", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState before(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "BRACKETS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState brackets(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "CAPSTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState capstr(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "CASE", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState Case(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "CASEALL", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState caseall(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "CENTER", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState center(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "CHR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState chr(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "COMP", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState comp(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "COND", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState cond(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "CONDALL", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState condall(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "DIGEST", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState digest(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "FLIP", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState flip(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "FOREACH", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState Foreach(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "FORMDECODE", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState formdecode(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "HMAC", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState hmac(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "IF", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.NoParse)]
		public static CallState If(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "IFELSE", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.NoParse)]
		public static CallState ifelse(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "LCSTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState lcstr(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "LCSTR2", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState lcstr2(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "LEFT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState left(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "LJUST", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState ljust(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "LPOS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState lpos(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "MERGE", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState merge(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "MID", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState mid(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "NCOND", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState ncond(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "NCONDALL", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState ncondall(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ORD", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ord(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ORDINAL", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ordinal(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "POS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState pos(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "REPEAT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState repeat(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "RIGHT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState right(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "RJUST", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState rjust(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SCRAMBLE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState scramble(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SECURE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState secure(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SPACE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState space(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SPELLNUM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState spellnum(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SQUISH", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState squish(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "STRIPACCENTS", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState stripaccents(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "STRIPANSI", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState stripansi(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "STRLEN", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState strlen(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "STRMATCH", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState strmatch(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SWITCH", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState Switch(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "SWITCHALL", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState Switchall(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "TR", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState Tr(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "TRIM", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState trim(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "TRIMPENN", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState trimpenn(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "TRIMTINY", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState trimtiny(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "UCSTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState ucstr(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "UCSTR2", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ucstr2(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "URLDECODE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState urldecode(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "URLENCODE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState urlencode(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "WRAP", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState wrap(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "STRDELETE", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState delete(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "TEXTSEARCH", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState textsearch(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
	}
}
