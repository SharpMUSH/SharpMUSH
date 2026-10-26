using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "strdistance", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["source", "target"])]
	public ValueTask<CallState> StringDistanceFunction(IMUSHCodeParser parser, SharpFunctionAttribute _)
	{
		var arguments = parser.CurrentState.Arguments;
		return StringDistance.TryCalculate(arguments["0"].Message, arguments["1"].Message, out var distance)
			? ValueTask.FromResult<CallState>(distance)
			: ValueTask.FromResult<CallState>(StringDistance.WorkLimitExceeded);
	}

	[SharpFunction(Name = "soundex", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> SoundEx(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var arg1 = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 1, "soundex").ToPlainText().ToLowerInvariant();

		return arg1 switch
		{
			"soundex" => ValueTask.FromResult<CallState>(ComputeSoundex(
				arg0.StartsWith("ph", StringComparison.OrdinalIgnoreCase) ? "f" + arg0[2..] : arg0)),
			"phone" => ValueTask.FromResult<CallState>(ComputePhoneticHash(arg0)),
			_ => ValueTask.FromResult<CallState>(ErrorMessages.Returns.InvalidHashType)
		};
	}

	[SharpFunction(Name = "soundslike", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> SoundLike(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message.ToPlainText();
		var arg2 = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, "soundex").ToPlainText().ToLowerInvariant();

		return arg2 switch
		{
			"soundex" => ValueTask.FromResult<CallState>(
				ComputeSoundex(arg0.StartsWith("ph", StringComparison.OrdinalIgnoreCase) ? "f" + arg0[2..] : arg0) ==
				ComputeSoundex(arg1.StartsWith("ph", StringComparison.OrdinalIgnoreCase) ? "f" + arg1[2..] : arg1)
					? "1" : "0"),
			"phone" => ValueTask.FromResult<CallState>(
				ComputePhoneticHash(arg0) == ComputePhoneticHash(arg1) ? "1" : "0"),
			_ => ValueTask.FromResult<CallState>(ErrorMessages.Returns.InvalidHashType)
		};
	}

	/// <summary>
	/// Compute the American Soundex code for a string.
	/// Standard mapping: B/F/P/V=1, C/G/J/K/Q/S/X/Z=2, D/T=3, L=4, M/N=5, R=6
	/// </summary>
	private string ComputeSoundex(string input)
	{
		if (string.IsNullOrEmpty(input)) return "0000";

		// Standard American Soundex mapping
		const string soundexMap = "01230120022455012623010202";
		// Index:                   A B C D E F G H I J K L M N O P Q R S T U V W X Y Z

		var result = new char[4];
		result[0] = char.ToUpper(input[0]);
		var lastCode = result[0] >= 'A' && result[0] <= 'Z'
			? soundexMap[result[0] - 'A']
			: '0';
		var count = 1;

		for (var i = 1; i < input.Length && count < 4; i++)
		{
			var c = char.ToUpper(input[i]);
			if (c < 'A' || c > 'Z') continue;

			var code = soundexMap[c - 'A'];
			if (code != '0' && code != lastCode)
			{
				result[count++] = code;
			}
			lastCode = code;
		}

		// Pad with zeros
		while (count < 4)
		{
			result[count++] = '0';
		}

		return new string(result);
	}

	/// <summary>
	/// Compute the phonetic hash using SQLite's spellfix1 algorithm (used by PennMUSH).
	/// Maps characters to phonetic classes, omits vowels beside R/L, deduplicates.
	/// </summary>
	private string ComputePhoneticHash(string input)
	{
		if (string.IsNullOrEmpty(input)) return "";

		var word = input.ToLowerInvariant();
		var result = new System.Text.StringBuilder();
		var length = word.Length;

		// Character classes
		const int SILENT = 0, VOWEL = 1, B = 2, C = 3, D = 4, L = 6, R = 7, M = 8, Y = 9, DIGIT = 10, SPACE = 11, OTHER = 12;

		// className maps class index to output character
		const string classOutput = ".ABCDHLRMY9 ?";

		// midClass lookup (H, W, Y differ in initClass)
		int MidClass(char ch) => ch switch
		{
			>= 'a' and <= 'z' => ch switch
			{
				'a' or 'e' or 'i' or 'o' or 'u' or 'y' => VOWEL,
				'b' or 'f' or 'p' or 'v' or 'w' => B,
				'c' or 'g' or 'j' or 'k' or 'q' or 's' or 'x' or 'z' => C,
				'd' or 't' => D,
				'h' => SILENT,
				'l' => L,
				'r' => R,
				'm' or 'n' => M,
				_ => OTHER
			},
			>= '0' and <= '9' => DIGIT,
			' ' or '\t' or '\r' or '\n' => SPACE,
			'\'' => SILENT,
			_ => OTHER
		};

		// initClass: same as midClass except H→SILENT, W→B, Y→Y (not VOWEL)
		int InitClass(char ch) => ch switch
		{
			'y' => Y,
			'h' => SILENT,
			_ => MidClass(ch)
		};

		// Drop initial GN/KN
		int start = 0;
		if (length >= 2 && (word[0] == 'g' || word[0] == 'k') && word[1] == 'n')
		{
			start = 1;
		}

		int cPrev = OTHER; // 0x77 maps to OTHER range
		int cPrevX = OTHER;
		bool isFirst = true;

		for (int i = start; i < length; i++)
		{
			var ch = word[i];

			// Skip D before J/G, W before R, T before CH
			if (i + 1 < length)
			{
				if (ch == 'w' && word[i + 1] == 'r') continue;
				if (ch == 'd' && (word[i + 1] == 'j' || word[i + 1] == 'g')) continue;
				if (i + 2 < length && ch == 't' && word[i + 1] == 'c' && word[i + 2] == 'h') continue;
			}

			int c = isFirst ? InitClass(ch) : MidClass(ch);

			if (c == SPACE) continue;
			if (c == OTHER && cPrev != DIGIT) continue;

			isFirst = false;

			// Omit vowels beside R and L
			if (c == VOWEL && (cPrevX == R || cPrevX == L))
			{
				continue;
			}
			if ((c == R || c == L) && cPrevX == VOWEL)
			{
				// Remove the preceding vowel
				if (result.Length > 0) result.Length--;
			}

			cPrev = c;
			if (c == SILENT) continue;
			cPrevX = c;

			char output = classOutput[c];
			// Deduplicate consecutive same output chars
			if (result.Length == 0 || output != result[result.Length - 1])
			{
				result.Append(output);
			}
		}

		return result.ToString();
	}

	[SharpFunction(Name = "suggest", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> Suggest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var category = args["0"].Message.ToPlainText();
		var word = args["1"].Message.ToPlainText();
		var separator = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ").ToPlainText();
		var limit = 20;

		if (args.ContainsKey("3"))
		{
			// fun_suggest reads its limit with is_integer and refuses anything else with e_int
			// (src/help.c:1843-1847).
			if (!ArgHelpers.TryInteger(parser, args["3"].Message.ToPlainText(), out limit))
			{
				return new CallState(ErrorMessages.Returns.Integer);
			}

			if (limit < 1)
			{
				return new CallState(ErrorMessages.Returns.Integers);
			}
		}

		// Get suggestion data from expanded server data
		var suggestionData = await ObjectDataService.GetExpandedServerDataAsync<SuggestionData>();
		if (suggestionData?.Categories == null || !suggestionData.Categories.ContainsKey(category))
		{
			// If category doesn't exist, return empty string
			return new CallState(string.Empty);
		}

		var vocabulary = suggestionData.Categories[category];
		if (vocabulary == null || vocabulary.Count == 0)
		{
			return new CallState(string.Empty);
		}

		return new CallState(string.Join(separator, SpellfixMatch(vocabulary, word, limit)));
	}

	/// <summary>
	/// What PennMUSH's <c>SELECT upper(word) FROM suggest WHERE word MATCH ?1 AND top=?3</c> returns
	/// (src/help.c:1853-1878): SQLite's spellfix1 table, which PennMUSH builds without a cost table.
	/// Only words whose phonetic hash starts with the first few characters of the query's are candidates
	/// (spellfix1RunQuery, src/spellfix.c:2441-2573); they are ranked by editdist1 and the closest
	/// <paramref name="limit"/> are kept. Words are stored lower-cased and returned upper-cased, ASCII only,
	/// as SQLite's lower() and upper() do.
	/// </summary>
	private IEnumerable<string> SpellfixMatch(IEnumerable<string> vocabulary, string word, int limit)
	{
		const int maxHash = 32; // SPELLFIX_MX_HASH
		const int defaultScope = 3;

		var pattern = SpellfixTransliterate(word);
		var hash = ComputePhoneticHash(pattern.EndsWith('*') ? pattern[..^1] : pattern);
		if (hash.Length > maxHash - 2)
		{
			hash = hash[..(maxHash - 2)];
		}

		var scope = hash.Length switch
		{
			> defaultScope => defaultScope,
			> 2 => hash.Length - 1,
			_ => hash.Length
		};
		var low = hash[..scope];
		var high = low + "Z";

		// The shadow table is read in (langid, k2) index order, then rowid: the order words were added.
		var candidates = vocabulary
			.Select(AsciiLower)
			.Distinct(StringComparer.Ordinal)
			.Select(stored => (Stored: stored, K1: SpellfixTransliterate(stored)))
			.Select(entry => (entry.Stored, entry.K1, K2: ComputePhoneticHash(entry.K1)))
			.Where(entry => string.CompareOrdinal(entry.K2, low) >= 0 && string.CompareOrdinal(entry.K2, high) < 0)
			.OrderBy(entry => entry.K2, StringComparer.Ordinal);

		// Every row has rank 0, so spellfix1Score is the distance plus 32: ordering by distance is the same.
		var kept = new List<(string Stored, int Distance)>(Math.Min(limit, 64));
		foreach (var (stored, k1, _) in candidates)
		{
			var distance = SpellfixEditDistance(pattern, k1);
			if (kept.Count < limit)
			{
				kept.Add((stored, distance));
				continue;
			}

			var worst = 0;
			for (var i = 1; i < kept.Count; i++)
			{
				if (kept[i].Distance > kept[worst].Distance)
				{
					worst = i;
				}
			}

			if (distance < kept[worst].Distance)
			{
				kept[worst] = (stored, distance);
			}
		}

		return kept.OrderBy(entry => entry.Distance).Select(entry => AsciiUpper(entry.Stored));
	}

	private static string AsciiLower(string text)
		=> string.Create(text.Length, text, (span, source) =>
		{
			for (var i = 0; i < source.Length; i++)
			{
				span[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + 32) : source[i];
			}
		});

	private static string AsciiUpper(string text)
		=> string.Create(text.Length, text, (span, source) =>
		{
			for (var i = 0; i < source.Length; i++)
			{
				span[i] = source[i] is >= 'a' and <= 'z' ? (char)(source[i] - 32) : source[i];
			}
		});

	/// <summary>
	/// spellfix1's transliterate() (src/spellfix.c:1766): ASCII as it is, any other character as its
	/// unaccented ASCII letters, or <c>?</c> when it has none. spellfix1 uses its own table; the Unicode
	/// decomposition gives the same answer for accented Latin letters.
	/// </summary>
	private static string SpellfixTransliterate(string text)
	{
		var result = new System.Text.StringBuilder(text.Length);
		foreach (var rune in text.EnumerateRunes())
		{
			if (rune.IsAscii)
			{
				result.Append((char)rune.Value);
				continue;
			}

			var ascii = rune.ToString().Normalize(System.Text.NormalizationForm.FormD).Where(c => c <= 127).ToArray();
			if (ascii.Length == 0)
			{
				result.Append('?');
			}
			else
			{
				result.Append(ascii);
			}
		}

		return result.ToString();
	}

	private const int SpellfixSilent = 0, SpellfixVowel = 1, SpellfixB = 2, SpellfixC = 3, SpellfixD = 4,
		SpellfixL = 6, SpellfixR = 7, SpellfixM = 8, SpellfixY = 9, SpellfixDigit = 10, SpellfixSpace = 11,
		SpellfixOther = 12;

	/// <summary>
	/// spellfix1's characterClass(): <c>initClass</c> for the first character of a word (no previous
	/// character), <c>midClass</c> after it. The two differ only for Y and the apostrophe.
	/// </summary>
	private static int SpellfixClass(char previous, char ch)
	{
		var initial = previous == '\0';
		return char.ToLowerInvariant((char)(ch & 0x7f)) switch
		{
			'a' or 'e' or 'i' or 'o' or 'u' => SpellfixVowel,
			'y' => initial ? SpellfixY : SpellfixVowel,
			'b' or 'f' or 'p' or 'v' or 'w' => SpellfixB,
			'c' or 'g' or 'j' or 'k' or 'q' or 's' or 'x' or 'z' => SpellfixC,
			'd' or 't' => SpellfixD,
			'h' => SpellfixSilent,
			'l' => SpellfixL,
			'r' => SpellfixR,
			'm' or 'n' => SpellfixM,
			'\'' => initial ? SpellfixOther : SpellfixSilent,
			>= '0' and <= '9' => SpellfixDigit,
			'\t' or '\f' or '\r' or ' ' => SpellfixSpace,
			_ => SpellfixOther
		};
	}

	/// <summary>spellfix1's insertOrDeleteCost() (src/spellfix.c:287).</summary>
	private static int SpellfixInsertOrDeleteCost(char previous, char ch, char next)
	{
		var chClass = SpellfixClass(previous, ch);
		if (chClass == SpellfixSilent)
		{
			return 1;
		}

		if (previous == ch)
		{
			return 10;
		}

		if (chClass == SpellfixVowel && (previous == 'r' || next == 'r'))
		{
			return 20;
		}

		if (chClass != SpellfixClass(previous, previous))
		{
			return 100;
		}

		return chClass == SpellfixVowel ? 15 : 50;
	}

	/// <summary>spellfix1's substituteCost() (src/spellfix.c:328).</summary>
	private static int SpellfixSubstituteCost(char previous, char from, char to)
	{
		if (from == to || (from == (to ^ 0x20) && char.IsAsciiLetter(to)))
		{
			return 0;
		}

		var fromClass = SpellfixClass(previous, from);
		var toClass = SpellfixClass(previous, to);
		if (fromClass == toClass)
		{
			return 40;
		}

		return fromClass is >= SpellfixB and <= SpellfixY && toClass is >= SpellfixB and <= SpellfixY ? 75 : 100;
	}

	/// <summary>
	/// spellfix1's editdist1() (src/spellfix.c:372): the cost of turning <paramref name="source"/> into
	/// <paramref name="target"/>, cheaper for likely misspellings. A <paramref name="source"/> ending in
	/// <c>*</c> is a prefix of <paramref name="target"/>.
	/// </summary>
	private static int SpellfixEditDistance(string source, string target)
	{
		const int finalInsertCostDivisor = 4;
		static char At(string text, int index) => index < text.Length ? text[index] : '\0';

		var common = 0;
		var dc = '\0';
		while (common < source.Length && common < target.Length && source[common] == target[common])
		{
			dc = source[common];
			common++;
		}

		var a = source[common..];
		var b = target[common..];
		if (a.Length == 0 && b.Length == 0)
		{
			return 0;
		}

		if (a.Length == 0)
		{
			var total = 0;
			var previous = dc;
			for (var x = 0; x < b.Length; x++)
			{
				total += SpellfixInsertOrDeleteCost(previous, b[x], At(b, x + 1)) / finalInsertCostDivisor;
				previous = b[x];
			}

			return total;
		}

		if (b.Length == 0)
		{
			var total = 0;
			var previous = dc;
			for (var x = 0; x < a.Length; x++)
			{
				total += SpellfixInsertOrDeleteCost(previous, a[x], At(a, x + 1));
				previous = a[x];
			}

			return total;
		}

		if (a == "*")
		{
			return 0;
		}

		var m = new int[b.Length + 1];
		var cx = new char[b.Length + 1];
		cx[0] = dc;
		var cBprev = dc;
		for (var xB = 1; xB <= b.Length; xB++)
		{
			var cB = b[xB - 1];
			cx[xB] = cB;
			m[xB] = m[xB - 1] + SpellfixInsertOrDeleteCost(cBprev, cB, At(b, xB));
			cBprev = cB;
		}

		var cAprev = dc;
		var cA = '\0';
		for (var xA = 1; xA <= a.Length; xA++)
		{
			var lastA = xA == a.Length;
			cA = a[xA - 1];
			var cAnext = At(a, xA);
			if (cA == '*' && lastA)
			{
				break;
			}

			var d = m[0];
			m[0] = d + SpellfixInsertOrDeleteCost(cAprev, cA, cAnext);
			for (var xB = 1; xB <= b.Length; xB++)
			{
				var cB = b[xB - 1];
				var cBnext = At(b, xB);

				var insertCost = SpellfixInsertOrDeleteCost(cx[xB - 1], cB, cBnext);
				if (lastA)
				{
					insertCost /= finalInsertCostDivisor;
				}

				var deleteCost = SpellfixInsertOrDeleteCost(cx[xB], cA, cBnext);
				var substituteCost = SpellfixSubstituteCost(cx[xB - 1], cA, cB);

				var totalCost = insertCost + m[xB - 1];
				var ncx = cB;
				if (deleteCost + m[xB] < totalCost)
				{
					totalCost = deleteCost + m[xB];
					ncx = cA;
				}

				if (substituteCost + d < totalCost)
				{
					totalCost = substituteCost + d;
				}

				d = m[xB];
				m[xB] = totalCost;
				cx[xB] = ncx;
			}

			cAprev = cA;
		}

		return cA == '*' ? m[1..].Min() : m[^1];
	}
}
