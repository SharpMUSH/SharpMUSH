using System.Drawing;
using System.Text.RegularExpressions;
using ANSILibrary;
using DotNext.Collections.Generic;
using MarkupString;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using XSoundex;
using static ANSILibrary.ANSI;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	// TODO: Not compatible due to not being able to indicate a DBREF
	[SharpFunction(Name = "pcreate", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
	public static async ValueTask<CallState> PCreate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var location = await Mediator!.Send(new GetObjectNodeQuery(new DBRef
		{
			Number = Convert.ToInt32(Configuration!.CurrentValue.Database.PlayerStart)
		}));

		var trueLocation = location.Match(
			player => player.Object.Key,
			room => room.Object.Key,
			exit => exit.Object.Key,
			thing => thing.Object.Key,
			none => -1);

		var created = await Mediator.Send(new CreatePlayerCommand(
			args["0"].Message!.ToString(),
			args["1"].Message!.ToString(),
			new DBRef(trueLocation == -1 ? 1 : trueLocation)));

		return new CallState($"#{created.Number}:{created.CreationMilliseconds}");
	}

	[SharpFunction(Name = "ansi", MinArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ANSI(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		// TODO: Move this to AnsiMarkup, to get a parsed Markup.
		// That way, align() has access to it.
		var foreground = AnsiColor.NoAnsi;
		var background = AnsiColor.NoAnsi;
		var blink = false;
		var bold = false;
		var clear = false;
		var invert = false;
		var underline = false;

		var ansiCodes = args["0"].Message!.ToString().Split(' ');
		Func<bool, byte, byte[]> highlightFunc = (highlight, b) => highlight ? [1, b] : [b];

		foreach (var cde in ansiCodes)
		{
			var code = cde.AsSpan();
			var curHilight = false;
			if (code.StartsWith(['#']) || code.StartsWith("/#"))
			{
				// TODO: Handle background.
				foreground = AnsiColor.NewRGB(ColorTranslator.FromHtml(code[1..].ToString()));
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

		return ValueTask.FromResult(new CallState(MModule.markupSingle2(new Ansi(details), args["1"].Message)));
	}

	[SharpFunction(Name = "@@", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> AtAt(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(new(string.Empty));

	[SharpFunction(Name = "allof", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> AllOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "atrlock", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> AtrLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "beep", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.AdminOnly | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Beep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "benchmark", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> Benchmark(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "checkpass", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Checkpass(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbRefConversion = HelperFunctions.ParseDbRef(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		if (dbRefConversion.IsNone())
		{
			await NotifyService!.Notify(parser.CurrentState.Executor!.Value, "I can't see that here.");
			return new CallState("#-1 NO SUCH PLAYER");
		}

		var dbRef = dbRefConversion.AsValue();
		var objectInfo = await Mediator!.Send(new GetObjectNodeQuery(dbRef));
		if (!objectInfo.IsPlayer)
		{
			return new CallState("#-1 NO SUCH PLAYER");
		}

		var player = objectInfo.AsPlayer;

		var result = PasswordService!.PasswordIsValid(
			$"#{player.Object.Key}:{player.Object.CreationTime}",
			parser.CurrentState.Arguments["1"].Message!.ToString(),
			player.PasswordHash);

		return result ? new("1") : new("0");
	}

	[SharpFunction(Name = "clone", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Clone(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "create", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Create(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "die", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Die(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "dig", MinArgs = 1, MaxArgs = 6, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Dig(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "fn", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> Fn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "functions", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> FFunctions(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}


	[SharpFunction(Name = "isdbref", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> IsDbRef(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var parsed = HelperFunctions.ParseDbRef(MModule.plainText(parser.CurrentState.Arguments["0"].Message));
		if (parsed.IsNone()) return new("0");
		return new CallState(!(await Mediator!.Send(new GetObjectNodeQuery(parsed.AsValue()))).IsNone);
	}

	[SharpFunction(Name = "isint", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IsInt(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(new(int.TryParse(parser.CurrentState.Arguments["0"].Message!.ToString(), out var _) ? "1" : "0"));

	[SharpFunction(Name = "isnum", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IsNum(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(new(decimal.TryParse(parser.CurrentState.Arguments["0"].Message!.ToString(), out var _) ? "1" : "0"));

	[SharpFunction(Name = "isobjid", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IsObjId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "isregexp", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> isregexp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg = parser.CurrentState.Arguments["0"].Message!.ToString();

		if (string.IsNullOrWhiteSpace(arg)) return ValueTask.FromResult<CallState>(new("0"));

		try { Regex.Match("", arg); } catch (ArgumentException) { return ValueTask.FromResult<CallState>(new("0")); }

		return ValueTask.FromResult<CallState>(new("1"));
	}

	[SharpFunction(Name = "isword", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IsWord(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = MModule.plainText(parser.CurrentState.Arguments["0"].Message);
		return ValueTask.FromResult(new CallState(Regex.IsMatch(str, @"^[a-zA-Z]$")));
	}

	[SharpFunction(Name = "itext", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IText(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "letq", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse | FunctionFlags.UnEvenArgsOnly)]
	public static async ValueTask<CallState> LetQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var everythingIsOkay = true;

		// TODO: Check if MarkupString is properly Immutable. If not, make it Immutable!
		var validPeek = parser.CurrentState.Registers.TryPeek(out var currentRegisters);
		var newRegisters = currentRegisters!.ToDictionary(k => k.Key, kv => kv.Value);
		parser.CurrentState.Registers.Push(newRegisters);

		var numberedArguments = parser.CurrentState.ArgumentsOrdered;

		for (var i = 0; i < numberedArguments.Count - 1; i += 2)
		{
			everythingIsOkay &= parser.CurrentState.AddRegister(
				numberedArguments[i.ToString()].Message!.ToString().ToUpper(),
				numberedArguments[(i + 1).ToString()].Message!);
		}

		if (everythingIsOkay)
		{
			var parsed = await parser.FunctionParse(numberedArguments.Last().Value.Message!);
			_ = parser.CurrentState.Registers.TryPop(out _);
			return parsed!;
		}

		_ = parser.CurrentState.Registers.TryPop(out _);
		return new CallState("#-1 REGISTER NAME INVALID");
	}

	[SharpFunction(Name = "link", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Link(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "list", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> List(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "listq", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ListQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		_ = parser.CurrentState.Registers.TryPeek(out var kv);
		return ValueTask.FromResult(new CallState(string.Join(" ", kv!.Keys)));
	}

	[SharpFunction(Name = "lset", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> LSet(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "null", MinArgs = 0, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular )]
	public static ValueTask<CallState> Null(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(CallState.Empty);

	[SharpFunction(Name = "open", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Open(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "r", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> R(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "rand", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Rand(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "registers", MinArgs = 0, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Registers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	
	[SharpFunction(Name = "render", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Render(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	
	[SharpFunction(Name = "s", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> S(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> (await parser.FunctionParse(parser.CurrentState.Arguments.Last().Value.Message!))!;

	[SharpFunction(Name = "scan", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Scan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "setq", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> setq(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var everythingIsOkay = true;

		var numberedArguments = parser.CurrentState.ArgumentsOrdered;
		
		for (var i = 0; i < numberedArguments.Count; i += 2)
		{
			everythingIsOkay &= parser.CurrentState.AddRegister(
				numberedArguments[i.ToString()].Message!.ToString().ToUpper(),
				numberedArguments[(i + 1).ToString()].Message!);
		}

		if (everythingIsOkay)
		{
			return ValueTask.FromResult(new CallState(string.Empty));
		}
		else
		{
			return ValueTask.FromResult(new CallState("#-1 REGISTER NAME INVALID"));
		}
	}

	[SharpFunction(Name = "setr", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.EvenArgsOnly)]
	public static ValueTask<CallState> setr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var everythingIsOkay = true;

		var numberedArguments = parser.CurrentState.ArgumentsOrdered;
		
		for (var i = 0; i < numberedArguments.Count; i += 2)
		{
			everythingIsOkay &= parser.CurrentState.AddRegister(
				numberedArguments[$"{i}"].Message!.ToString().ToUpper(),
				numberedArguments[$"{i + 1}"].Message!);
		}

		if (everythingIsOkay)
		{
			return ValueTask.FromResult(new CallState(numberedArguments["1"].Message!));
		}
		else
		{
			return ValueTask.FromResult(new CallState("#-1 REGISTER NAME INVALID"));
		}
	}
	[SharpFunction(Name = "soundex", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> SoundEx(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments.TryGetValue("1", out var val) 
			? val.Message!.ToPlainText().ToLowerInvariant() 
			: "soundex";

		return ValueTask.FromResult<CallState>(
			arg1 == "soundex" 
				? arg0.ToSoundex()
				: Errors.NotSupported);
	}

	[SharpFunction(Name = "soundslike", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> SoundLike(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments.TryGetValue("2", out var val) 
			? val.Message!.ToPlainText().ToLowerInvariant() 
			: "soundex";

		return ValueTask.FromResult<CallState>(
			arg2 == "soundex" 
				? arg0.HasTheSameSoundex(arg1)
				: Errors.NotSupported);
	}

	[SharpFunction(Name = "suggest", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Suggest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "slev", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> SLev(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "stext", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> SText(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "tel", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Tel(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "testlock", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TestLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "textentries", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TextEntries(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "textfile", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TextFile(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "unsetq", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> UnSetQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (parser.CurrentState.Arguments.Count == 0)
		{
			var canPeek = parser.CurrentState.Registers.TryPeek(out var peek);
			peek!.Clear();
		}
		else
		{
			var registers = MModule.plainText(parser.CurrentState.Arguments["0"].Message).Split(" ");
			foreach (var r in registers)
			{
				var canPeek = parser.CurrentState.Registers.TryPeek(out var peek);
				peek!.TryRemove(r);
			}
		}

		return ValueTask.FromResult<CallState>(new(string.Empty));
	}

	[SharpFunction(Name = "wipe", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Wipe(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}