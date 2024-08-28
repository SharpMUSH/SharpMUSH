using ANSILibrary;
using DotNext.Collections.Generic;
using MarkupString;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using System.Text.RegularExpressions;
using static ANSILibrary.ANSI;

namespace SharpMUSH.Implementation.Functions
{
	public partial class Functions
	{
		// TODO: Not compatible due to not being able to indicate a DBREF
		[SharpFunction(Name = "pcreate", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
		public static CallState PCreate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			var args = parser.CurrentState.Arguments;
			var location = parser.Database.GetObjectNodeAsync(new Library.Models.DBRef
			{
				Number = Configurable.PlayerStart
			}).Result;

			var trueLocation = location.Match(
				player => player.Object!.Key,
				room => room.Object!.Key,
				exit => exit.Object!.Key,
				thing => thing.Object!.Key,
				none => -1);

			var created = parser.Database.CreatePlayerAsync(
				args[0].Message!.ToString(),
				args[1].Message!.ToString(),
				new Library.Models.DBRef(trueLocation == -1 ? 1 : trueLocation)).Result;

			return new CallState($"#{created.Number}:{created.CreationMilliseconds}");
		}

		[SharpFunction(Name = "ansi", MinArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState ANSI(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			var args = parser.CurrentState.Arguments;

			var foreground = AnsiColor.NoAnsi;
			var background = AnsiColor.NoAnsi;
			var blink = false;
			var bold = false;
			var clear = false;
			var invert = false;
			var underline = false;

			var ansiCodes = args[0].Message!.ToString().Split(' ');
			Func<bool, byte, byte[]> highlightFunc = (highlight, b) => highlight ? [1, b] : [b];

			foreach (var cde in ansiCodes)
			{
				var code = cde.AsSpan();
				var curHilight = false;
				if (code.StartsWith(['#']) || code.StartsWith("/#"))
				{
					// Hex.
					continue;
				}
				if (code.StartsWith(['+']) && !code.StartsWith("+xterm"))
				{
					// colorname
					continue;
				}
				var xterm = 0;
				if (
					(int.TryParse(code, out xterm) && xterm > -1 && xterm < 256) ||
					(code.StartsWith("+xterm") && int.TryParse(code[5..], out xterm) && xterm > -1 && xterm < 256))
				{
					// xterm color
					continue;
				}
				if (code.StartsWith(['<']) && code.EndsWith(['>']))
				{
					// Check for triple RGB values
					continue;
				}
				// ansi code. each gets evaluated individually.
				foreach (var chr in code)
				{
					switch (chr)
					{
						case 'i':
							invert = true;
							break;
						case 'I':
							invert = false;
							break;
						case 'f':
							blink = true;
							break;
						case 'F':
							blink = false;
							break;
						case 'u':
							underline = true;
							break;
						case 'U':
							underline = false;
							break;
						case 'h':
							curHilight = true;
							break;
						case 'H':
							curHilight = false;
							break;
						case 'n':
							clear = true; // TODO: This PROBABLY needs better handling. No doubt this is not correct due to the tree structure.
							break;
						case 'd':
							// TODO: Inline this as a function.
							foreground = StringExtensions.ansiByte(39);
							break;
						case 'x':
							foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 30));
							break;
						case 'r':
							foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 31));
							break;
						case 'g':
							foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 32));
							break;
						case 'y':
							foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 33));
							break;
						case 'b':
							foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 34));
							break;
						case 'm':
							foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 35));
							break;
						case 'c':
							foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 36));
							break;
						case 'w':
							foreground = StringExtensions.ansiBytes(highlightFunc(curHilight, 37));
							break;
						case 'D':
							background = StringExtensions.ansiByte(49);
							break;
						case 'X':
							background = StringExtensions.ansiBytes(highlightFunc(curHilight, 40));
							break;
						case 'R':
							background = StringExtensions.ansiBytes(highlightFunc(curHilight, 41));
							break;
						case 'G':
							background = StringExtensions.ansiBytes(highlightFunc(curHilight, 42));
							break;
						case 'Y':
							background = StringExtensions.ansiBytes(highlightFunc(curHilight, 43));
							break;
						case 'B':
							background = StringExtensions.ansiBytes(highlightFunc(curHilight, 44));
							break;
						case 'M':
							background = StringExtensions.ansiBytes(highlightFunc(curHilight, 45));
							break;
						case 'C':
							background = StringExtensions.ansiBytes(highlightFunc(curHilight, 46));
							break;
						case 'W':
							background = StringExtensions.ansiBytes(highlightFunc(curHilight, 47));
							break;
						default:
							// Do nothing. Just skip.
							// Should probably warn about invalid ansi codes.
							break;
					}
				}
			}

			var details = new MarkupImplementation.AnsiStructure(
				foreground: foreground,
				background: background,
				blink: blink,
				bold: bold,
				clear: clear,
				inverted: invert,
				underlined: underline,
				faint: false,
				italic: false,
				overlined: false,
				strikeThrough: false,
				linkText: null,
				linkUrl: null);

			return new CallState(MModule.markupSingle2(new MarkupImplementation.AnsiMarkup(details), args[1].Message));
		}

		[SharpFunction(Name = "@@", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState AtAt(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			new(string.Empty);

		[SharpFunction(Name = "ALLOF", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState AllOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ATRLOCK", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState AtrLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "BEEP", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.AdminOnly | FunctionFlags.StripAnsi)]
		public static CallState Beep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "BENCHMARK", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.NoParse)]
		public static CallState Benchmark(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "CHECKPASS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.StripAnsi)]
		public static CallState Checkpass(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			var dbRefConversion = HelperFunctions.ParseDBRef(MModule.plainText(parser.CurrentState.Arguments[0].Message));
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
		public static CallState Clone(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "CREATE", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState Create(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "DIE", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Die(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "DIG", MinArgs = 1, MaxArgs = 6, Flags = FunctionFlags.Regular)]
		public static CallState Dig(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "FN", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState Fn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "FUNCTIONS", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState FFunctions(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "IBREAK", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IBreak(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ILEV", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ILev(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "INUM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState INum(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ISDBREF", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsDbRef(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ISINT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsInt(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			new(int.TryParse(parser.CurrentState.Arguments[0].Message!.ToString(), out var _) ? "1" : "0");

		[SharpFunction(Name = "ISNUM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsNum(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			new(decimal.TryParse(parser.CurrentState.Arguments[0].Message!.ToString(), out var _) ? "1" : "0");

		[SharpFunction(Name = "ISOBJID", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsObjId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ISREGEXP", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState isregexp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			var arg = parser.CurrentState.Arguments[0].Message!.ToString();

			if (string.IsNullOrWhiteSpace(arg)) return new("0");

			try { Regex.Match("", arg); } catch (ArgumentException) { return new("0"); }

			return new("1");
		}

		[SharpFunction(Name = "ISWORD", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IsWord(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ITEXT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState IText(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "letq", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse | FunctionFlags.UnEvenArgsOnly)]
		public static CallState LetQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			var everythingIsOkay = true;

			// TODO: Check if MarkupString is properly Immutable. If not, make it Immutable!
			var currentRegisters = parser.CurrentState.Registers.Peek();
			var newRegisters = currentRegisters.ToDictionary(k => k.Key, kv => kv.Value);
			parser.CurrentState.Registers.Push(newRegisters);

			for (var i = 0; i < parser.CurrentState.Arguments.Count - 1; i += 2)
			{
				everythingIsOkay &= parser.CurrentState.AddRegister(
					parser.CurrentState.Arguments[i].Message!.ToString().ToUpper(),
					parser.CurrentState.Arguments[i + 1].Message!);
			}

			if (everythingIsOkay)
			{
				var parsed = parser.EvaluationFunctionParse(parser.CurrentState.Arguments.Last().Message!)!;
				_ = parser.CurrentState.Registers.Pop();
				return parsed;
			}
			else
			{
				_ = parser.CurrentState.Registers.Pop();
				return new CallState("#-1 REGISTER NAME INVALID");
			}
		}

		[SharpFunction(Name = "LINK", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Link(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LIST", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState List(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LISTQ", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ListQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LSET", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState LSet(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "NULL", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
		public static CallState Null(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			new(string.Empty);

		[SharpFunction(Name = "OPEN", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState Open(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "R", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState R(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "RAND", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Rand(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "REGISTERS", MinArgs = 0, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Registers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "RENDER", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState Render(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "S", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState S(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SCAN", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Scan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "setq", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
		public static CallState setq(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			var everythingIsOkay = true;

			for (var i = 0; i < parser.CurrentState.Arguments.Count; i += 2)
			{
				everythingIsOkay &= parser.CurrentState.AddRegister(
					parser.CurrentState.Arguments[i].Message!.ToString().ToUpper(),
					parser.CurrentState.Arguments[i + 1].Message!);
			}

			if (everythingIsOkay)
			{
				return new CallState(string.Empty);
			}
			else
			{
				return new CallState("#-1 REGISTER NAME INVALID");
			}
		}

		[SharpFunction(Name = "setr", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.EvenArgsOnly)]
		public static CallState setr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			var everythingIsOkay = true;

			for (var i = 0; i < parser.CurrentState.Arguments.Count; i += 2)
			{
				everythingIsOkay &= parser.CurrentState.AddRegister(
					parser.CurrentState.Arguments[i].Message!.ToString().ToUpper(),
					parser.CurrentState.Arguments[i + 1].Message!);
			}

			if (everythingIsOkay)
			{
				return new CallState(parser.CurrentState.Arguments[1].Message!);
			}
			else
			{
				return new CallState("#-1 REGISTER NAME INVALID");
			}
		}
		[SharpFunction(Name = "SOUNDEX", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState SoundEx(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SOUNDSLIKE", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState SoundLike(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SPEAK", MinArgs = 2, MaxArgs = 7, Flags = FunctionFlags.Regular)]
		public static CallState Speak(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "STRALLOF", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
		public static CallState StrAllOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "STRINSERT", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState StrInsert(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "STRREPLACE", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState StrReplace(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SUGGEST", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Suggest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SLEV", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
		public static CallState SLev(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "STEXT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState SText(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TEL", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Tel(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TESTLOCK", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState TestLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TEXTENTRIES", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState TextEntries(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TEXTFILE", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState TextFile(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "UNSETQ", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState UnSetQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "WIPE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState Wipe(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
	}
}