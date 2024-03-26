using Antlr4.Runtime;
using SharpMUSH.Implementation.Definitions;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Functions
{
	public partial class Functions
	{
		// TODO: Not compatible due to not being able to indicate a DBREF
		[SharpFunction(Name = "pcreate", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
		public static CallState PCreate(Parser parser, SharpFunctionAttribute _2)
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

			return new CallState($"#{created}");
		}

		[SharpFunction(Name = "ansi", MinArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState ANSI(Parser parser, SharpFunctionAttribute _2)
		{
			var args = parser.CurrentState.Arguments;

			return new CallState(args[1].Message);
		}

		[SharpFunction(Name = "@@", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState AtAt(Parser parser, SharpFunctionAttribute _2) =>
			new (string.Empty);

		[SharpFunction(Name = "ALLOF", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState AllOf(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ATRLOCK", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState AtrLock(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "BEEP", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.AdminOnly | FunctionFlags.StripAnsi)]
		public static CallState Beep(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "BENCHMARK", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.NoParse)]
		public static CallState Benchmark(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "CHECKPASS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.StripAnsi)]
		public static CallState Checkpass(Parser parser, SharpFunctionAttribute _2)
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
		public static CallState Clone(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "CREATE", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState Create(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "DIE", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Die(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "DIG", MinArgs = 1, MaxArgs = 6, Flags = FunctionFlags.Regular)]
		public static CallState Dig(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "FN", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState Fn(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "FUNCTIONS", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState FFunctions(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "IBREAK", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IBreak(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ILEV", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ILev(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "INUM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState INum(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ISDBREF", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsDbRef(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ISINT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsInt(Parser parser, SharpFunctionAttribute _2) =>
			new(int.TryParse(parser.CurrentState.Arguments[0].Message!.ToString(), out var _) ? "1" : "0");
		
		[SharpFunction(Name = "ISNUM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsNum(Parser parser, SharpFunctionAttribute _2) =>
			new (decimal.TryParse(parser.CurrentState.Arguments[0].Message!.ToString(), out var _) ? "1" : "0");

		[SharpFunction(Name = "ISOBJID", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsObjId(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ISREGEXP", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState isregexp(Parser parser, SharpFunctionAttribute _2)
		{
			var arg = parser.CurrentState.Arguments[0].Message!.ToString();
			
			if (string.IsNullOrWhiteSpace(arg)) return new("0");
			
			try { Regex.Match("", arg); } catch (ArgumentException) { return new("0"); }
			
			return new("1");
		}

		[SharpFunction(Name = "ISWORD", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsWord(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ITEXT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IText(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LETQ", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState LetQ(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LINK", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Link(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LIST", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState List(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LISTQ", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ListQ(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LSET", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState LSet(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "NULL", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
		public static CallState Null(Parser parser, SharpFunctionAttribute _2) =>
			new (string.Empty);

		[SharpFunction(Name = "OPEN", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState Open(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "R", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState R(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "RAND", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Rand(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "REGISTERS", MinArgs = 0, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Registers(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "RENDER", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState Render(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "S", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState S(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SCAN", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Scan(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "SOUNDEX", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState SoundEx(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "SOUNDSLIKE", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState SoundLike(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "SPEAK", MinArgs = 2, MaxArgs = 7, Flags = FunctionFlags.Regular)]
		public static CallState Speak(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "STRALLOF", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState StrAllOf(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "STRINSERT", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState StrInsert(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "STRREPLACE", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState StrReplace(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SUGGEST", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Suggest(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SLEV", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
		public static CallState SLev(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "STEXT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState SText(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TEL", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Tel(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TESTLOCK", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState TestLock(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TEXTENTRIES", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState TextEntries(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TEXTFILE", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState TextFile(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "UNSETQ", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState UnSetQ(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "WIPE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState Wipe(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
	}
}