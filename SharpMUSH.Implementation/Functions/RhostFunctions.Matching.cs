using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using MarkupString;
using MarkupString.Ansi;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Softcode;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// RhostMUSH's counting, wildcard and register functions: randpos(), nummember(), numwildmatch(), setqmatch(),
/// listmatch(), pushregs() and ofparse(); its bracket checkers parenstr() and parenmatch(); and bypass(), which
/// has nothing to do here.
/// </summary>
public partial class Functions
{
	/// <summary>The registers setqmatch() and listmatch() fill, as RhostMUSH caps them: one per wildcard, 0 to 9.</summary>
	private const int RhostMatchRegisters = 10;

	/// <summary>randpos(): totpos()'s positions in a random order.</summary>
	[SharpFunction(Name = "randpos", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["match", "string"])]
	public ValueTask<CallState> RandPos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var text = args["1"].Message.ToPlainText();
		var positions = OverlappingMatches(text, args["0"].Message.ToPlainText())
			.Select(i => (i + 1).ToString(CultureInfo.InvariantCulture))
			.ToArray();
		Random.Shared.Shuffle(positions);
		return ValueTask.FromResult(new CallState(string.Join(' ', positions)));
	}

	/// <summary>nummember(): how many elements are exactly the word, case and all.</summary>
	[SharpFunction(Name = "nummember", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["list", "word", "delimiter"])]
	public ValueTask<CallState> NumMember(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var word = args["1"].Message.ToPlainText();
		var count = MushText.SplitList(ArgHelpers.Delimiter(args, 2), args["0"].Message)
			.Count(element => element.ToPlainText() == word);
		return ValueTask.FromResult(new CallState(count));
	}

	/// <summary>
	/// numwildmatch(): how many elements, each read as a wildcard pattern, match the word. Case does not matter.
	/// The list holds the patterns, as in RhostMUSH, so <c>numwildmatch(*e* *ed,fred)</c> is 2.
	/// </summary>
	[SharpFunction(Name = "numwildmatch", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["list", "word", "delimiter"])]
	public ValueTask<CallState> NumWildMatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var word = args["1"].Message.ToPlainText();
		var count = MushText.SplitList(ArgHelpers.Delimiter(args, 2), args["0"].Message)
			.Count(element => SoftcodeRegex.Wildcard(element.ToPlainText()).IsMatch(word));
		return ValueTask.FromResult(new CallState(count));
	}

	/// <summary>
	/// setqmatch(): matches the wildcard pattern against the string and, on a match, puts what each wildcard
	/// matched into registers 0 to 9 in turn. Returns 1 when it matched and set a register, else 0; with a third
	/// argument of <c>n</c> it returns nothing either way.
	/// </summary>
	[SharpFunction(Name = "setqmatch", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["string", "pattern", "quiet"])]
	public ValueTask<CallState> SetQMatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var set = false;
		foreach (var (register, capture) in WildcardCaptures(args["0"].Message, args["1"].Message.ToPlainText()))
		{
			set |= parser.CurrentState.AddRegister(register.ToString(CultureInfo.InvariantCulture), capture);
		}

		var quiet = args.Count > 2 && args["2"].Message.ToPlainText().StartsWith('n');
		return ValueTask.FromResult(quiet ? CallState.Empty : new CallState(set ? "1" : "0"));
	}

	/// <summary>
	/// listmatch(): what each wildcard of the pattern matched in the string, joined by the first character of
	/// the delimiter (a space by default). The fourth argument, a space-separated list of wildcard numbers from
	/// 0, keeps only those, still in pattern order. Nothing when it does not match.
	/// </summary>
	[SharpFunction(Name = "listmatch", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular,
		ParameterNames = ["string", "pattern", "delimiter", "wildcards"])]
	public ValueTask<CallState> ListMatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delimiterText = args.Count > 2 ? args["2"].Message.ToPlainText() : string.Empty;
		var delimiter = MarkupText.Plain(delimiterText.Length > 0 ? delimiterText[..1] : " ");
		var wanted = args.Count > 3 && args["3"].Message.ToPlainText().Trim().Length > 0
			? args["3"].Message.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(RhostInteger).ToHashSet()
			: null;

		var kept = WildcardCaptures(args["0"].Message, args["1"].Message.ToPlainText())
			.Where(capture => wanted is null || wanted.Contains(capture.Register))
			.Select(capture => capture.Text);
		return ValueTask.FromResult(new CallState(MarkupText.Join(delimiter, kept)));
	}

	/// <summary>What each of the first ten wildcards matched, when the pattern matches the whole string.</summary>
	private static IEnumerable<(int Register, MString Text)> WildcardCaptures(MString text, string pattern)
	{
		var plain = text.ToPlainText();
		var match = SoftcodeRegex.Wildcard(pattern).Match(plain);
		if (!match.Success) yield break;

		for (var group = 1; group < match.Groups.Count && group <= RhostMatchRegisters; group++)
		{
			var captured = match.Groups[group];
			yield return (group - 1, captured.Success ? PlainSlice(text, plain, captured.Index, captured.Length) : MarkupText.Empty);
		}
	}

	/// <summary>
	/// pushregs()'s saved registers, one set for each evaluation's register stack, so a save lasts as long as the
	/// command that made it.
	/// </summary>
	private static readonly ConditionalWeakTable<ConcurrentStack<Dictionary<string, MString>>, Dictionary<string, MString>>
		SavedRegisters = new();

	/// <summary>
	/// pushregs(): a number other than 0 saves every register that is set; 0 puts every saved register back
	/// and empties the saved copies. A space-separated list of <c>+&lt;register&gt;</c> saves and
	/// <c>-&lt;register&gt;</c> puts back single registers, each named by one letter or digit. Returns nothing.
	/// </summary>
	[SharpFunction(Name = "pushregs", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["registers"])]
	public ValueTask<CallState> PushRegs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var state = parser.CurrentState;
		var request = state.ArgumentsOrdered["0"].Message.ToPlainText();
		if (request.Trim().Length == 0)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.FunctionRequiresOneArgument));
		}

		if (!state.Registers.TryPeek(out var current))
		{
			return ValueTask.FromResult(CallState.Empty);
		}

		var saved = SavedRegisters.GetValue(state.Registers, _ => new Dictionary<string, MString>());
		if (!request.Contains('-') && decimal.TryParse(request.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
					out var number))
		{
			if (decimal.Truncate(number) != 0)
			{
				foreach (var register in RegisterLabels.Registers(current).Where(current.ContainsKey))
				{
					saved[register] = current[register];
				}
			}
			else
			{
				foreach (var register in saved.Keys.ToArray())
				{
					state.AddRegister(register, saved[register]);
					saved[register] = MarkupText.Empty;
				}
			}

			return ValueTask.FromResult(CallState.Empty);
		}

		foreach (var token in request.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
		{
			if (token.Length != 2 || !char.IsAsciiLetterOrDigit(token[1])) continue;

			var register = char.ToUpperInvariant(token[1]).ToString();
			switch (token[0])
			{
				case '+' when current.TryGetValue(register, out var value):
					saved[register] = value;
					break;
				case '-' when saved.TryGetValue(register, out var value):
					state.AddRegister(register, value);
					break;
			}
		}

		return ValueTask.FromResult(CallState.Empty);
	}

	private enum OfParseKeep { FirstTrue = 1, AllTrue, FirstFalse, AllFalse, FirstSet, AllSet, FirstEmpty, AllEmpty }

	/// <summary>
	/// ofparse(): firstof(), allof(), strfirstof() and strallof() in one function, with their opposites. The
	/// first argument picks which; the odd types stop at the first value that counts and fall back on the last
	/// argument, and the even types keep every value that counts, joined by the first character of the last
	/// argument. Arguments are evaluated only as they are reached.
	/// </summary>
	[SharpFunction(Name = "ofparse", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse,
		ParameterNames = ["type", "value...", "default|separator"])]
	public async ValueTask<CallState> OfParse(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var errors = new ListEvaluationErrors();
		var args = parser.CurrentState.ArgumentsOrdered;
		var type = RhostInteger(errors.Record(await args["0"].GetParsedResultAsync()).ToPlainText());
		if (type is < 1 or > 8)
		{
			return errors.Complete(new CallState("#-1 UNKNOWN TYPE SPECIFIED.  SELECT BETWEEN 1-8"));
		}

		var keep = (OfParseKeep)type;
		var keepsAll = type % 2 == 0;
		var needed = keepsAll ? 3 : 2;
		if (args.Count < needed)
		{
			return errors.Complete(new CallState(
				$"#-1 FUNCTION (OFPARSE KEYTYPE {type}) EXPECTS {needed} OR MORE ARGUMENTS [RECEIVED {args.Count}]"));
		}

		var last = args[(args.Count - 1).ToString(CultureInfo.InvariantCulture)];
		var separator = MarkupText.Empty;
		if (keepsAll)
		{
			var separatorText = errors.Record(await last.GetParsedResultAsync()).ToPlainText();
			separator = MarkupText.Plain(separatorText.Length > 0 ? separatorText[..1] : string.Empty);
		}

		var kept = new List<MString>();
		for (var i = 1; i < args.Count - 1; i++)
		{
			var value = errors.Record(await args[i.ToString(CultureInfo.InvariantCulture)].GetParsedResultAsync());
			if (!OfParseCounts(keep, value.ToPlainText())) continue;
			if (!keepsAll) return errors.Complete(new CallState(value));
			kept.Add(value);
		}

		return keepsAll
			? errors.Complete(new CallState(MarkupText.Join(separator, kept)))
			: errors.Complete(new CallState(errors.Record(await last.GetParsedResultAsync())));
	}

	/// <summary>
	/// Whether ofparse() keeps <paramref name="value"/>. RhostMUSH's false is empty or blank, <c>0</c> exactly, or
	/// anything starting <c>#-</c>, leading spaces aside; everything else is true.
	/// </summary>
	private static bool OfParseCounts(OfParseKeep keep, string value)
	{
		var trimmed = value.TrimStart();
		var isFalse = trimmed.Length == 0 || trimmed == "0" || trimmed.StartsWith("#-", StringComparison.Ordinal);
		return keep switch
		{
			OfParseKeep.FirstTrue or OfParseKeep.AllTrue => !isFalse,
			OfParseKeep.FirstFalse or OfParseKeep.AllFalse => isFalse,
			OfParseKeep.FirstSet or OfParseKeep.AllSet => value.Length > 0,
			_ => value.Length == 0
		};
	}

	/// <summary>
	/// bypass(): RhostMUSH uses it in a global function that replaces a built-in one, to reach the built-in. A
	/// global function here never hides a built-in from fn(), so it does nothing and returns nothing.
	/// </summary>
	[SharpFunction(Name = "bypass", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular,
		ParameterNames = ["on"])]
	public ValueTask<CallState> Bypass(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(CallState.Empty);

	/// <summary>
	/// parenstr(): the text, unevaluated, with every bracket, parenthesis and brace coloured by its depth and the
	/// first one that does not match in bold red. Starting the text with <c>!</c> colours only that one.
	/// Commas are part of the text.
	/// </summary>
	[SharpFunction(Name = "parenstr", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse | FunctionFlags.StripAnsi,
		ParameterNames = ["string"])]
	public ValueTask<CallState> ParenStr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var text = string.Join(',', parser.CurrentState.ArgumentsOrdered.Values.Select(argument => argument.Message.ToPlainText()));
		var onlyMismatch = text.StartsWith('!');
		return ValueTask.FromResult(new CallState(BracketMatch(onlyMismatch ? text[1..] : text, onlyMismatch, string.Empty)));
	}

	/// <summary>
	/// parenmatch(): parenstr() on an attribute's text, read as get() reads it. A type of 1 colours only the
	/// mismatch. The flags add spaces: <c>s</c> after <c>,</c> and <c>;</c> and around <c>=</c>, <c>p</c> inside
	/// parentheses, <c>b</c> inside brackets and <c>B</c> inside braces.
	/// </summary>
	[SharpFunction(Name = "parenmatch", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object/attribute", "type", "key", "flags"])]
	public async ValueTask<CallState> ParenMatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		if (HelperFunctions.SplitOptionalObjectAndAttr(args["0"].Message.ToPlainText()) is not { } split)
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "PARENMATCH"));
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var target = split.Object is { } name
			? await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, name, LocateFlags.All)
			: executor;
		if (target is not AnySharpObject found)
		{
			return new CallState(ErrorMessages.Returns.NotVisible);
		}

		var onlyMismatch = args.Count > 1 && RhostInteger(args["1"].Message.ToPlainText()) == 1;
		var spacing = args.Count > 3 ? args["3"].Message.ToPlainText() : string.Empty;
		return await AttributeService.GetAttributeAsync(executor, found, split.Attribute,
				mode: IAttributeService.AttributeMode.Read, parent: true) switch
		{
			SharpAttribute[] chain => new CallState(BracketMatch(chain.Last().Value.ToPlainText(), onlyMismatch, spacing)),
			None => CallState.Empty,
			Error<string> error => new CallState(error.Value)
		};
	}

	/// <summary>
	/// Colours <paramref name="text"/>'s brackets, parentheses and braces by depth, reading left to right. A
	/// character after <c>%</c> or <c>\</c> is escaped and not counted. The first closer without its opener, or
	/// else the last opener left open, is bold red, and the text after it is left as it is. With
	/// <paramref name="onlyMismatch"/>, a text with a mismatch has only that character coloured.
	/// </summary>
	private static MString BracketMatch(string text, bool onlyMismatch, string spacing)
	{
		var mismatch = BracketMismatch(text);
		if (onlyMismatch && mismatch is { } at)
		{
			return MarkupText.Concat([MarkupText.Plain(text[..at]), Mismatched(text[at]), MarkupText.Plain(text[(at + 1)..])]);
		}

		var pieces = new List<MString>();
		var plain = new StringBuilder();
		var depth = 0;
		var escaped = false;
		for (var i = 0; i < text.Length; i++)
		{
			var c = text[i];
			if (i == mismatch)
			{
				if (IsCloser(c) && BracketSpacing(c, spacing)) plain.Append(' ');
				Flush();
				pieces.Add(Mismatched(c));
				pieces.Add(MarkupText.Plain(text[(i + 1)..]));
				return MarkupText.Concat(pieces.ToArray());
			}

			var wasEscaped = escaped;
			escaped = !escaped && c is '%' or '\\';
			if (wasEscaped || !IsBracket(c))
			{
				if (!wasEscaped && c == '=' && spacing.Contains('s')) plain.Append(' ');
				plain.Append(c);
				if (!wasEscaped && (c is ',' or ';' or '=') && spacing.Contains('s')) plain.Append(' ');
				continue;
			}

			var inside = BracketSpacing(c, spacing);
			if (IsCloser(c))
			{
				depth--;
				if (inside) plain.Append(' ');
			}

			Flush();
			pieces.Add(MarkupText.Wrap(SemanticTokenAnsiPalette.GetBracketDepthStyle(depth), c.ToString()));
			if (!IsCloser(c))
			{
				depth++;
				if (inside) plain.Append(' ');
			}
		}

		Flush();
		return MarkupText.Concat(pieces.ToArray());

		void Flush()
		{
			if (plain.Length == 0) return;
			pieces.Add(MarkupText.Plain(plain.ToString()));
			plain.Clear();
		}
	}

	/// <summary>Where the first closer without its opener is, or else the last opener left open; null when all match.</summary>
	private static int? BracketMismatch(string text)
	{
		var open = new Stack<int>();
		var escaped = false;
		for (var i = 0; i < text.Length; i++)
		{
			var c = text[i];
			var wasEscaped = escaped;
			escaped = !escaped && c is '%' or '\\';
			if (wasEscaped || !IsBracket(c)) continue;
			if (!IsCloser(c))
			{
				open.Push(i);
				continue;
			}

			if (open.Count == 0 || Closer(text[open.Pop()]) != c) return i;
		}

		return open.Count > 0 ? open.Peek() : null;
	}

	/// <summary>The mismatch, in what <c>ansi(hr, ...)</c> writes.</summary>
	private static readonly Ansi MismatchStyle = AnsiCodeParser.Parse("hr", _ => null);

	private static MString Mismatched(char c) => MarkupText.Wrap(MismatchStyle, c.ToString());

	private static bool IsBracket(char c) => c is '[' or ']' or '(' or ')' or '{' or '}';

	private static bool IsCloser(char c) => c is ']' or ')' or '}';

	private static char Closer(char opener) => opener switch { '[' => ']', '(' => ')', _ => '}' };

	private static bool BracketSpacing(char c, string spacing) => c switch
	{
		'(' or ')' => spacing.Contains('p'),
		'[' or ']' => spacing.Contains('b'),
		_ => spacing.Contains('B')
	};
}
