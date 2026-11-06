namespace SharpMUSH.Implementation.Common;

/// <summary>
/// SQLite's spellfix1 virtual table as PennMUSH uses it, without a cost table: the vocabulary
/// suggestions of <c>suggest()</c> and of an unknown function's "DID YOU MEAN" hint.
/// </summary>
public static class Spellfix
{
	/// <summary>
	/// Compute the phonetic hash using SQLite's spellfix1 algorithm (used by PennMUSH).
	/// Maps characters to phonetic classes, omits vowels beside R/L, deduplicates.
	/// </summary>
	public static string PhoneticHash(string input)
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

	/// <summary>
	/// What PennMUSH's <c>SELECT upper(word) FROM suggest WHERE word MATCH ?1 AND top=?3</c> returns
	/// (src/help.c:1853-1878): SQLite's spellfix1 table, which PennMUSH builds without a cost table.
	/// Only words whose phonetic hash starts with the first few characters of the query's are candidates
	/// (spellfix1RunQuery, src/spellfix.c:2441-2573); they are ranked by editdist1 and the closest
	/// <paramref name="limit"/> are kept. <paramref name="scope"/> is the query's <c>scope</c>, how many
	/// characters of the hash must agree: spellfix1's default of 3 for <c>suggest()</c>, 5 for
	/// <c>suggest_name</c> (src/function.c:189). Words are stored lower-cased and returned upper-cased,
	/// ASCII only, as SQLite's lower() and upper() do.
	/// </summary>
	public static IEnumerable<string> Match(IEnumerable<string> vocabulary, string word, int limit, int scope = 3)
	{
		const int maxHash = 32; // SPELLFIX_MX_HASH

		var pattern = SpellfixTransliterate(word);
		var hash = PhoneticHash(pattern.EndsWith('*') ? pattern[..^1] : pattern);
		if (hash.Length > maxHash - 2)
		{
			hash = hash[..(maxHash - 2)];
		}

		var prefix = hash.Length switch
		{
			var length when length > scope => scope,
			> 2 => hash.Length - 1,
			_ => hash.Length
		};
		var low = hash[..prefix];
		var high = low + "Z";

		// The shadow table is read in (langid, k2) index order, then rowid: the order words were added.
		var candidates = vocabulary
			.Select(AsciiLower)
			.Distinct(StringComparer.Ordinal)
			.Select(stored => (Stored: stored, K1: SpellfixTransliterate(stored)))
			.Select(entry => (entry.Stored, entry.K1, K2: PhoneticHash(entry.K1)))
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
