using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions
{
	public static partial class Functions
	{
		[SharpFunction(Name = "after", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState After(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			var args = parser.CurrentState.Arguments;
			var fullString = args[0]!.Message;
			var search = args[1]!.Message;
			var idx = MModule.indexOf(fullString, search);

			return new CallState(MModule.substring(idx, MModule.getLength(fullString) - idx, args[0].Message));
		}

		[SharpFunction(Name = "strcat", Flags = FunctionFlags.Regular)]
		public static CallState Concat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
					=> new(parser.CurrentState.Arguments
							.Select(x => x.Message)
							.Aggregate((x, y) => MModule.concat(x, y)));

		[SharpFunction(Name = "cat", Flags = FunctionFlags.Regular)]
		public static CallState Cat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments
					.Select(x => x.Message)
					.Aggregate((x, y) => MModule.concat(x, y, MModule.single(" "))));

		[SharpFunction(Name = "ACCENT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState Accent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ALIGN", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
		public static CallState Align(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LALIGN", MinArgs = 2, MaxArgs = 6, Flags = FunctionFlags.Regular)]
		public static CallState LAlign(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ALPHAMAX", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState AlphaMax(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ALPHAMIN", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState AlphaMin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ART", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Art(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "BEFORE", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState Before(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "BRACKETS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Brackets(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "CAPSTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState CapStr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "CASE", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState Case(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "CASEALL", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState CaseAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "CENTER", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState Center(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "CHR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Chr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "COMP", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Comp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "COND", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState Cond(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "CONDALL", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState CondAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "DIGEST", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState Digest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "EDIT", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
		public static CallState Edit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ESCAPE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState Escape(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "FLIP", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState Flip(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "FOREACH", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState ForEach(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "FORMDECODE", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState FormDecode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "HMAC", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState HMAC(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "IF", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.NoParse)]
		public static CallState If(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "IFELSE", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.NoParse)]
		public static CallState IfElse(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LCSTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState LCStr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LEFT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState Left(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LJUST", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState LJust(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LPOS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState LPos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "MERGE", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState Merge(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "MID", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState Mid(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "NCOND", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState NCond(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "NCONDALL", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState NCondAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ORD", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Ord(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ORDINAL", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Ordinal(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "POS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Pos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "REPEAT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState Repeat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "RIGHT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState Right(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "RJUST", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState RJust(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SCRAMBLE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState Scramble(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SECURE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState Secure(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SPACE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Space(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SPELLNUM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState SpellNum(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SQUISH", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState Squish(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "STRIPACCENTS", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState StripAccents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "STRIPANSI", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState StripAnsi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "STRLEN", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState StrLen(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "STRMATCH", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState StrMatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SWITCH", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState Switch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SWITCHALL", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState Switchall(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TR", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState Tr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TRIM", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState Trim(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TRIMPENN", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState TrimPenn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TRIMTINY", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState trimTiny(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "UCSTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState UCStr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "URLDECODE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState URLDecode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "URLENCODE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState URLEncode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "WRAP", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState Wrap(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "STRDELETE", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState StrDelete(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TEXTSEARCH", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState TextSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
	}
}
