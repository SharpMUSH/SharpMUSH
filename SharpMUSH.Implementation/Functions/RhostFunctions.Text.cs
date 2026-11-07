using System.Globalization;
using System.Text;
using MarkupString;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// RhostMUSH's list-position, character-position, character-set and number-base functions: aindex(), aiindex(),
/// iindex() and rindex(); creplace() and numpos(); andchr(), orchr(), xorchr() and notchr(); mask(); tobin(),
/// tooct(), todec() and tohex(). Colour is never part of what they compare.
/// </summary>
public partial class Functions
{
	private enum ListIndexEdit { Replace, Insert, Append, InsertAny }

	[SharpFunction(Name = "rindex", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular,
		ParameterNames = ["list", "elements", "position", "delimiter"])]
	public ValueTask<CallState> RIndex(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(IndexEdit(parser, ListIndexEdit.Replace));

	[SharpFunction(Name = "iindex", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular,
		ParameterNames = ["list", "elements", "position", "delimiter"])]
	public ValueTask<CallState> IIndex(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(IndexEdit(parser, ListIndexEdit.Insert));

	[SharpFunction(Name = "aindex", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular,
		ParameterNames = ["list", "elements", "position", "delimiter"])]
	public ValueTask<CallState> AIndex(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(IndexEdit(parser, ListIndexEdit.Append));

	[SharpFunction(Name = "aiindex", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular,
		ParameterNames = ["list", "elements", "position", "delimiter"])]
	public ValueTask<CallState> AIIndex(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(IndexEdit(parser, ListIndexEdit.InsertAny));

	/// <summary>
	/// The four index edits. Positions count every stretch between delimiters, empty ones too, from 1; the
	/// number of elements, as RhostMUSH counts it, leaves out an empty stretch at either end. A position outside
	/// 1 to that number, an empty delimiter or empty elements leave the list as it is; aiindex() alone puts its
	/// elements into an empty list, at position 1.
	/// </summary>
	private static CallState IndexEdit(IMUSHCodeParser parser, ListIndexEdit edit)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var list = args["0"].Message;
		var elements = args["1"].Message;
		var delimiterText = args["3"].Message.ToPlainText();
		if (delimiterText.Length == 0 || elements.Length == 0) return new CallState(list);

		var delimiter = delimiterText[..1];
		var count = RhostElementCount(list.Text, delimiter[0]);
		var position = RhostInteger(args["2"].Message.ToPlainText());
		if (count == 0 || position < 1 || position > count)
		{
			return new CallState(count == 0 && position == 1 && edit == ListIndexEdit.InsertAny ? elements : list);
		}

		var stretches = list.Split(delimiter);
		var separator = MarkupText.Plain(delimiter);
		var output = new List<MString>();
		for (var i = 0; i < position - 1; i++)
		{
			output.Add(stretches[i]);
			output.Add(separator);
		}

		var rest = position - 1;
		switch (edit)
		{
			case ListIndexEdit.Replace:
				output.Add(elements);
				var replaced = RhostElementCount(elements.Text, delimiter[0]);
				if (replaced > count - position)
				{
					rest = stretches.Length;
				}
				else
				{
					output.Add(separator);
					rest += replaced;
				}

				break;
			case ListIndexEdit.Insert or ListIndexEdit.InsertAny:
				output.Add(elements);
				output.Add(separator);
				break;
			case ListIndexEdit.Append:
				output.Add(stretches[rest]);
				output.Add(separator);
				output.Add(elements);
				if (position < count) output.Add(separator);
				rest++;
				break;
		}

		if (rest < stretches.Length)
		{
			output.Add(MarkupText.Join(separator, stretches[rest..]));
		}

		return new CallState(MarkupText.Concat(output.ToArray()));
	}

	/// <summary>How many elements RhostMUSH counts in a list: every stretch between delimiters but an empty first or last one.</summary>
	private static int RhostElementCount(string list, char delimiter)
	{
		if (list.Length == 0) return 0;
		var count = list.Count(c => c == delimiter) + 1;
		if (list[0] == delimiter) count--;
		if (list[^1] == delimiter) count--;
		return Math.Max(0, count);
	}

	/// <summary>
	/// creplace(): characters of a string overwritten, overwritten up to its length (<c>c</c>), inserted
	/// (<c>i</c>) or replaced to the end (<c>r</c>) from a position; or, given a space-separated list of
	/// positions, the character at each overwritten by the next of the third argument's, round again when they
	/// run out.
	/// </summary>
	[SharpFunction(Name = "creplace", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular,
		ParameterNames = ["string", "position", "replacement", "type", "count"])]
	public ValueTask<CallState> CReplace(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var text = args["0"].Message;
		var positions = args["1"].Message.ToPlainText();
		var replacement = args["2"].Message;

		// The first number is checked even when a list of them follows.
		var start = RhostInteger(positions);
		if (start < 1 || start > CReplaceLimit)
		{
			return ValueTask.FromResult(new CallState($"#-1 VALUE MUST BE > 0 < {CReplaceLimit}"));
		}

		if (positions.AsSpan().IndexOfAny(' ', '\t') >= 0)
		{
			if (args.Count > 3) return ValueTask.FromResult(new CallState("#-1 MULTI-REPLACE OPTION REQUIRES 3 ARGS ONLY."));
			return ValueTask.FromResult(new CallState(ReplaceAtPositions(text, positions, replacement)));
		}

		var type = args.Count > 3 ? args["3"].Message.ToPlainText() : "o";
		var count = args.Count > 4 ? Math.Max(0, RhostInteger(args["4"].Message.ToPlainText())) : 0;
		// A count of characters shorter than the replacement takes that many of it.
		var used = count > 0 && count < replacement.Length ? replacement.Substring(0, count) : replacement;
		var before = text.Substring(0, Math.Min(start - 1, text.Length));
		var from = before.Length;
		var after = from == text.Length ? MarkupText.Empty : text.Substring(from);

		return ValueTask.FromResult(new CallState((type.Length > 0 ? char.ToLowerInvariant(type[0]) : '\0') switch
		{
			'o' => MarkupText.Concat([before, used, Skip(after, used.Length)]),
			'c' => MarkupText.Concat([before, Take(used, after.Length), Skip(after, used.Length)]),
			'i' => MarkupText.Concat([before, used, after]),
			'r' => MarkupText.Concat(before, used),
			_ => MarkupText.Empty
		}));

		static MString Skip(MString text, int count) => count >= text.Length ? MarkupText.Empty : text.Substring(count);
		static MString Take(MString text, int count) => count >= text.Length ? text : text.Substring(0, count);
	}

	/// <summary>The largest position creplace() takes, one short of RhostMUSH's usual buffer of 4,000 characters.</summary>
	private const int CReplaceLimit = 3999;

	private static MString ReplaceAtPositions(MString text, string positions, MString replacement)
	{
		if (replacement.Length == 0) return text;

		// Each position takes the next replacement character; a position given twice keeps the later one. The
		// string is rebuilt once from those choices, so many positions cost no more than one pass.
		var chosen = new SortedDictionary<int, int>();
		var next = 0;
		foreach (var index in positions.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(word => RhostInteger(word) - 1))
		{
			if (index < 0 || index >= text.Length) continue;
			chosen[index] = next;
			next = (next + 1) % replacement.Length;
		}

		var pieces = new List<MString>(chosen.Count * 2 + 1);
		var start = 0;
		foreach (var (index, character) in chosen)
		{
			if (index > start) pieces.Add(text.Substring(start, index - start));
			pieces.Add(replacement.Substring(character, 1));
			start = index + 1;
		}

		if (start < text.Length) pieces.Add(text.Substring(start));
		return MarkupText.Concat(pieces.ToArray());
	}

	/// <summary>
	/// numpos(): how many times the first argument appears in the second, overlapping matches counted; with a
	/// type of 1, how many characters of the second are among the first's. The fourth argument, RhostMUSH's
	/// choice of colour-aware matching, changes nothing here: colour is never part of the text.
	/// </summary>
	[SharpFunction(Name = "numpos", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["match", "string", "type", "colour-aware"])]
	public ValueTask<CallState> NumPos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var match = args["0"].Message.ToPlainText();
		var text = args["1"].Message.ToPlainText();
		var anyCharacter = args.Count > 2 && RhostInteger(args["2"].Message.ToPlainText()) != 0;

		var count = 0;
		if (anyCharacter)
		{
			var listed = match.EnumerateRunes().ToHashSet();
			count = text.EnumerateRunes().Count(listed.Contains);
		}
		else
		{
			count = OverlappingMatches(text, match).Count();
		}

		return ValueTask.FromResult(new CallState(count));
	}

	private enum CharacterTest { All, Any, ExactlyOne, None }

	[SharpFunction(Name = "andchr", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string", "characters", "each-once"])]
	public ValueTask<CallState> AndChr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(CharacterSetTest(parser, CharacterTest.All));

	[SharpFunction(Name = "orchr", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string", "characters", "each-once"])]
	public ValueTask<CallState> OrChr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(CharacterSetTest(parser, CharacterTest.Any));

	[SharpFunction(Name = "xorchr", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string", "characters", "each-once"])]
	public ValueTask<CallState> XorChr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(CharacterSetTest(parser, CharacterTest.ExactlyOne));

	[SharpFunction(Name = "notchr", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string", "characters", "each-once"])]
	public ValueTask<CallState> NotChr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(CharacterSetTest(parser, CharacterTest.None));

	/// <summary>
	/// How many of the characters listed the string holds, as RhostMUSH counts them. Without a third argument a
	/// character of the string matches one listed character and uses it up, so a character listed twice must be in
	/// the string twice. With a true third argument (RhostMUSH's colour-aware form) each listed character counts
	/// when the string holds it at all.
	/// </summary>
	private static CallState CharacterSetTest(IMUSHCodeParser parser, CharacterTest test)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var text = args["0"].Message.ToPlainText().EnumerateRunes().ToArray();
		var characters = args["1"].Message.ToPlainText().EnumerateRunes().ToArray();
		var eachOnce = args.Count > 2 && RhostInteger(args["2"].Message.ToPlainText()) != 0;

		int found;
		if (eachOnce)
		{
			var present = text.ToHashSet();
			found = characters.Count(present.Contains);
		}
		else
		{
			// How many of each listed character are left to match.
			var unused = characters.CountBy(c => c).ToDictionary();
			found = 0;
			foreach (var c in text.Where(unused.ContainsKey))
			{
				if (unused[c] == 0) continue;
				unused[c]--;
				found++;
			}
		}

		return new CallState(test switch
		{
			CharacterTest.All => found == characters.Length,
			CharacterTest.Any => found > 0,
			CharacterTest.ExactlyOne => found == 1,
			_ => found == 0
		});
	}

	/// <summary>
	/// mask(): the bitwise AND of whole numbers, or with a last argument of <c>|</c>, <c>^</c> or <c>~</c> their OR,
	/// XOR, or the first with the others' bits cleared; a last argument of <c>1</c> or <c>2</c> gives the first
	/// number's ones' or two's complement. The answer is an unsigned 32-bit number; a negative or non-numeric
	/// argument gives <c>#-1</c>.
	/// </summary>
	[SharpFunction(Name = "mask", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["number", "number...", "operation"])]
	public ValueTask<CallState> Mask(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered.Values.Select(argument => argument.Message.ToPlainText()).ToArray();
		var operation = '&';
		var numbers = args;
		if (args.Length > 2)
		{
			numbers = args[..^1];
			operation = args[^1] is [var first, ..] && first is '|' or '^' or '~' or '1' or '2' ? first : '&';
		}

		if (!numbers.All(IsRhostNumber)) return ValueTask.FromResult(new CallState("#-1"));
		var value = RhostInteger(numbers[0]);
		if (value < 0) return ValueTask.FromResult(new CallState("#-1"));

		switch (operation)
		{
			case '1':
				value = ~value;
				break;
			case '2':
				value = ~value + 1;
				break;
			default:
				foreach (var other in numbers[1..].Select(RhostInteger))
				{
					if (other < 0) return ValueTask.FromResult(new CallState("#-1"));
					value = operation switch
					{
						'|' => value | other,
						'^' => value ^ other,
						'~' => value & ~other,
						_ => value & other
					};
				}

				break;
		}

		return ValueTask.FromResult(new CallState(unchecked((uint)value).ToString(CultureInfo.InvariantCulture)));
	}

	[SharpFunction(Name = "tobin", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number"])]
	public ValueTask<CallState> ToBin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(Convert.ToString(RhostNumberArgument(parser), 2).PadLeft(32, '0')));

	[SharpFunction(Name = "tooct", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number"])]
	public ValueTask<CallState> ToOct(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(Convert.ToString(unchecked((uint)RhostNumberArgument(parser)), 8)));

	[SharpFunction(Name = "todec", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number"])]
	public ValueTask<CallState> ToDec(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(unchecked((uint)RhostNumberArgument(parser)).ToString(CultureInfo.InvariantCulture)));

	[SharpFunction(Name = "tohex", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number"])]
	public ValueTask<CallState> ToHex(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(RhostNumberArgument(parser).ToString("X", CultureInfo.InvariantCulture)));

	private static int RhostNumberArgument(IMUSHCodeParser parser)
		=> RhostBaseNumber(parser.CurrentState.ArgumentsOrdered["0"].Message.ToPlainText());

	/// <summary>
	/// A number for the base functions, as a 32-bit value: binary when its second character is <c>b</c>
	/// (<c>0b101</c>; past 32 digits, every bit set), hexadecimal after <c>0x</c>, octal after a leading
	/// <c>0</c>, decimal otherwise. What follows the digits is ignored, and no digits at all is 0.
	/// </summary>
	private static int RhostBaseNumber(string text)
	{
		if (text.Length > 1 && text[1] is 'b' or 'B')
		{
			if (text[2..].TrimStart('0').Length > 32) return -1;
			var bits = 0;
			for (int i = text.Length - 1, bit = 0; i > 1; i--, bit++)
			{
				if (text[i] == '1' && bit < 32) bits |= 1 << bit;
			}

			return bits;
		}

		var span = text.AsSpan().TrimStart(" \t\r\n\v\f");
		var negative = false;
		if (span is ['+' or '-', ..])
		{
			negative = span[0] == '-';
			span = span[1..];
		}

		var radix = 10;
		if (span is ['0', 'x' or 'X', var digit, ..] && char.IsAsciiHexDigit(digit))
		{
			radix = 16;
			span = span[2..];
		}
		else if (span is ['0', ..])
		{
			radix = 8;
		}

		return Saturated(span, radix, negative);
	}

	/// <summary>C's <c>atoi()</c>: a leading whole number, after spaces and a sign, and 0 when there is none.</summary>
	private static int RhostInteger(string text)
	{
		var span = text.AsSpan().TrimStart(" \t\r\n\v\f");
		var negative = false;
		if (span is ['+' or '-', ..])
		{
			negative = span[0] == '-';
			span = span[1..];
		}

		return Saturated(span, 10, negative);
	}

	/// <summary>
	/// The leading digits in <paramref name="radix"/>, held at the limits of a 64-bit number and then cut to 32 bits,
	/// as C's <c>strtol()</c> into an <c>int</c> leaves them.
	/// </summary>
	private static int Saturated(ReadOnlySpan<char> digits, int radix, bool negative)
	{
		long value = 0;
		foreach (var c in digits)
		{
			var digit = c switch
			{
				>= '0' and <= '9' => c - '0',
				>= 'a' and <= 'f' => c - 'a' + 10,
				>= 'A' and <= 'F' => c - 'A' + 10,
				_ => 99
			};
			if (digit >= radix) break;
			value = value > (long.MaxValue - digit) / radix ? long.MaxValue : value * radix + digit;
		}

		return unchecked((int)(negative ? (value == long.MaxValue ? long.MinValue : -value) : value));
	}

	/// <summary>RhostMUSH's test for a number: an optional minus, digits with at most one decimal point, spaces either side.</summary>
	private static bool IsRhostNumber(string text)
	{
		var span = text.AsSpan().Trim(" \t\r\n\v\f");
		if (span is ['-', ..]) span = span[1..];
		var whole = span.Length - span.TrimStart("0123456789").Length;
		span = span[whole..];
		var fraction = 0;
		if (span is ['.', ..])
		{
			span = span[1..];
			fraction = span.Length - span.TrimStart("0123456789").Length;
			span = span[fraction..];
		}

		return span.IsEmpty && whole + fraction > 0;
	}

	/// <summary>str(): the string from the first place the match starts, or <c>#-1</c>. Case matters.</summary>
	[SharpFunction(Name = "str", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "match"])]
	public ValueTask<CallState> Str(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var text = args["0"].Message;
		var at = text.ToPlainText().IndexOf(args["1"].Message.ToPlainText(), StringComparison.Ordinal);
		return ValueTask.FromResult(new CallState(at < 0 ? MarkupText.Plain("#-1") : text.Substring(TextIndex(text, at))));
	}

	/// <summary>
	/// Where the plain text's character at <paramref name="plainIndex"/> sits in <see cref="MarkupText.Text"/>, which
	/// also holds the carriers of points such as sounds; the length of the text when the index is past the end.
	/// </summary>
	private static int TextIndex(MString text, int plainIndex)
	{
		var plain = text.ToPlainText();
		var raw = text.Text;
		var seen = 0;
		for (var i = 0; i < raw.Length; i++)
		{
			if (seen >= plain.Length || raw[i] != plain[seen]) continue;
			if (seen == plainIndex) return i;
			seen++;
		}

		return raw.Length;
	}

	/// <summary>totmember(): the position, from 1, of every element equal to the word, case and all; 0 when none is.</summary>
	[SharpFunction(Name = "totmember", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["list", "word", "delimiter"])]
	public ValueTask<CallState> TotMember(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var word = args["1"].Message.ToPlainText();
		var positions = MushText.SplitList(ArgHelpers.Delimiter(args, 2), args["0"].Message)
			.Select((element, index) => (Element: element.ToPlainText(), Position: index + 1))
			.Where(element => element.Element == word)
			.Select(element => element.Position.ToString(CultureInfo.InvariantCulture))
			.ToArray();
		return ValueTask.FromResult(new CallState(positions.Length == 0 ? "0" : string.Join(' ', positions)));
	}

	/// <summary>
	/// totpos(): the position, from 1, of every place the match starts in the string, overlapping ones too; with a
	/// type of 1, of every character of the string that is one of the match's characters; with a type of 2, those
	/// characters themselves. Nothing when there are none.
	/// </summary>
	[SharpFunction(Name = "totpos", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["match", "string", "type", "colour-aware"])]
	public ValueTask<CallState> TotPos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var match = args["0"].Message.ToPlainText();
		var text = args["1"].Message.ToPlainText();
		var type = args.Count > 2 ? RhostInteger(args["2"].Message.ToPlainText()) : 0;

		var listed = match.EnumerateRunes().ToHashSet();
		if (type == 2) return ValueTask.FromResult(new CallState(string.Concat(text.EnumerateRunes().Where(listed.Contains))));

		var positions = (type != 0 ? ListedCharacterPositions(text, listed) : OverlappingMatches(text, match))
			.Select(i => (i + 1).ToString(CultureInfo.InvariantCulture));
		return ValueTask.FromResult(new CallState(string.Join(' ', positions)));
	}

	/// <summary>Where in the text, counted as positions are elsewhere, each character that is one of those listed starts.</summary>
	private static IEnumerable<int> ListedCharacterPositions(string text, HashSet<Rune> listed)
	{
		var index = 0;
		foreach (var rune in text.EnumerateRunes())
		{
			if (listed.Contains(rune)) yield return index;
			index += rune.Utf16SequenceLength;
		}
	}

	/// <summary>
	/// securex(): secure() that keeps the characters listed in the second argument. Its letter <c>a</c> keeps
	/// RhostMUSH's colour substitutions, which SharpMUSH code does not have, so it keeps nothing more.
	/// </summary>
	[SharpFunction(Name = "securex", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "keep"])]
	public ValueTask<CallState> SecureX(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var keep = (args.Count > 1 ? args["1"].Message.ToPlainText() : "").ToHashSet();
		return ValueTask.FromResult<CallState>(args["0"].Message.WithoutDataMarkup()
			.Apply(text => SoftcodeDecomposer.SoftcodeSpecial().Replace(text, special => keep.Contains(special.Value[0]) ? special.Value : " ")));
	}

	/// <summary>
	/// Where <paramref name="match"/> starts in <paramref name="text"/>, overlapping places too; an empty match is
	/// at every place. Knuth-Morris-Pratt, so a long repetitive match costs one pass over the text.
	/// </summary>
	private static IEnumerable<int> OverlappingMatches(string text, string match)
	{
		if (match.Length == 0)
		{
			for (var i = 0; i < text.Length; i++) yield return i;
			yield break;
		}

		var border = new int[match.Length];
		for (int i = 1, k = 0; i < match.Length; i++)
		{
			while (k > 0 && match[i] != match[k]) k = border[k - 1];
			if (match[i] == match[k]) k++;
			border[i] = k;
		}

		for (int i = 0, k = 0; i < text.Length; i++)
		{
			while (k > 0 && text[i] != match[k]) k = border[k - 1];
			if (text[i] == match[k]) k++;
			if (k == match.Length)
			{
				yield return i - match.Length + 1;
				k = border[k - 1];
			}
		}
	}
}
