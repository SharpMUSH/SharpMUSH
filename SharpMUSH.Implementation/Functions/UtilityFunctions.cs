using Antlr4.Runtime;
using SharpMUSH.Implementation.Definitions;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Functions
{
	public partial class Functions
	{
		// TODO: Not compatible due to not being able to indicate a DBREF
		[SharpFunction(Name = "pcreate", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
		public static CallState PCreate(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			var args = parser.CurrentState.Arguments;
			var location = parser.Database.GetObjectNodeAsync(new Library.Models.DBRef { 
				Number = Configurable.PlayerStart 
			}).Result;

			var trueLocation = location.Match(
				player => player.Object!.Key,
				room => room.Object!.Key,
				exit => exit.Object!.Key,
				thing => thing.Object!.Key,
				none => null);

			var created = parser.Database.CreatePlayerAsync(
				args[0].Message!.ToString(), 
				args[1].Message!.ToString(), 
				new Library.Models.DBRef(trueLocation ?? 1)).Result;

			return new CallState($"#{created.Number}:{created.CreationMilliseconds}");
		}

		[SharpFunction(Name = "ansi", MinArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState ANSI(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			var args = parser.CurrentState.Arguments;

			return new CallState(args[1].Message);
		}

		[SharpFunction(Name = "@@", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState AtAt(MUSHCodeParser parser, SharpFunctionAttribute _2) =>
			new (string.Empty);

		[SharpFunction(Name = "ALLOF", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState AllOf(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ATRLOCK", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState AtrLock(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "BEEP", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.AdminOnly | FunctionFlags.StripAnsi)]
		public static CallState Beep(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "BENCHMARK", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.NoParse)]
		public static CallState Benchmark(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "CHECKPASS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.StripAnsi)]
		public static CallState Checkpass(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			var dbRefConversion = ParseDBRef(MModule.plainText(parser.CurrentState.Arguments[0].Message));
			if (dbRefConversion.IsNone())
			{
				parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, "I can't see that here.");
				return new CallState("#-1 NO SUCH PLAYER");
			}

			var dbRef = dbRefConversion.AsT1.Value;
			var objectInfo = parser.Database.GetObjectNodeAsync(dbRef).Result;
			if (!objectInfo!.IsT0)
			{
				return new CallState("#-1 NO SUCH PLAYER");
			}
			
			var player = objectInfo.AsT0;

			var result = parser.PasswordService.PasswordIsValid(
				$"#{player!.Object!.Key}:{player!.Object!.CreationTime}", 
				parser.CurrentState.Arguments[1].Message!.ToString(), 
				player.PasswordHash);

			return result ? new("1") : new("0");
		}
		
		[SharpFunction(Name = "CLONE", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState Clone(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "CREATE", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState Create(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "DIE", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Die(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "DIG", MinArgs = 1, MaxArgs = 6, Flags = FunctionFlags.Regular)]
		public static CallState Dig(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "FN", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState Fn(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "FUNCTIONS", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState FFunctions(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "IBREAK", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IBreak(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ILEV", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ILev(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "INUM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState INum(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ISDBREF", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsDbRef(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ISINT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsInt(MUSHCodeParser parser, SharpFunctionAttribute _2) =>
			new(int.TryParse(parser.CurrentState.Arguments[0].Message!.ToString(), out var _) ? "1" : "0");
		
		[SharpFunction(Name = "ISNUM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsNum(MUSHCodeParser parser, SharpFunctionAttribute _2) =>
			new (decimal.TryParse(parser.CurrentState.Arguments[0].Message!.ToString(), out var _) ? "1" : "0");

		[SharpFunction(Name = "ISOBJID", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsObjId(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ISREGEXP", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState isregexp(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			var arg = parser.CurrentState.Arguments[0].Message!.ToString();
			
			if (string.IsNullOrWhiteSpace(arg)) return new("0");
			
			try { Regex.Match("", arg); } catch (ArgumentException) { return new("0"); }
			
			return new("1");
		}

		[SharpFunction(Name = "ISWORD", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsWord(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ITEXT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IText(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LETQ", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState LetQ(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LINK", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Link(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LIST", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState List(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LISTQ", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ListQ(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LSET", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState LSet(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "NULL", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
		public static CallState Null(MUSHCodeParser parser, SharpFunctionAttribute _2) =>
			new (string.Empty);

		[SharpFunction(Name = "OPEN", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState Open(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "R", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState R(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "RAND", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Rand(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "REGISTERS", MinArgs = 0, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Registers(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "RENDER", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState Render(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "S", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState S(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SCAN", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Scan(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "SOUNDEX", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState SoundEx(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "SOUNDSLIKE", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState SoundLike(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "SPEAK", MinArgs = 2, MaxArgs = 7, Flags = FunctionFlags.Regular)]
		public static CallState Speak(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "STRALLOF", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState StrAllOf(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "STRINSERT", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState StrInsert(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "STRREPLACE", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState StrReplace(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SUGGEST", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Suggest(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SLEV", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
		public static CallState SLev(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "STEXT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState SText(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TEL", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Tel(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TESTLOCK", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState TestLock(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TEXTENTRIES", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState TextEntries(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TEXTFILE", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState TextFile(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "UNSETQ", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState UnSetQ(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "WIPE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState Wipe(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
	}
}