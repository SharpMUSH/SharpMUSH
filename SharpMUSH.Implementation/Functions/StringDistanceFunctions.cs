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
		return StringDistance.TryCalculate(arguments["0"].Message!, arguments["1"].Message!, out var distance)
			? ValueTask.FromResult<CallState>(distance)
			: ValueTask.FromResult<CallState>(StringDistance.WorkLimitExceeded);
	}

	[SharpFunction(Name = "soundex", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> SoundEx(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments.TryGetValue("1", out var val)
			? val.Message!.ToPlainText().ToLowerInvariant()
			: "soundex";

		return arg1 switch
		{
			"soundex" => ValueTask.FromResult<CallState>(ComputeSoundex(
				arg0.StartsWith("ph", StringComparison.OrdinalIgnoreCase) ? "f" + arg0[2..] : arg0)),
			"phone" => ValueTask.FromResult<CallState>(ComputePhoneticHash(arg0)),
			_ => ValueTask.FromResult<CallState>("#-1 INVALID HASH TYPE")
		};
	}

	[SharpFunction(Name = "soundslike", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> SoundLike(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments.TryGetValue("2", out var val)
			? val.Message!.ToPlainText().ToLowerInvariant()
			: "soundex";

		return arg2 switch
		{
			"soundex" => ValueTask.FromResult<CallState>(
				ComputeSoundex(arg0.StartsWith("ph", StringComparison.OrdinalIgnoreCase) ? "f" + arg0[2..] : arg0) ==
				ComputeSoundex(arg1.StartsWith("ph", StringComparison.OrdinalIgnoreCase) ? "f" + arg1[2..] : arg1)
					? "1" : "0"),
			"phone" => ValueTask.FromResult<CallState>(
				ComputePhoneticHash(arg0) == ComputePhoneticHash(arg1) ? "1" : "0"),
			_ => ValueTask.FromResult<CallState>("#-1 INVALID HASH TYPE")
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
		var category = args["0"].Message!.ToPlainText();
		var word = args["1"].Message!.ToPlainText();
		var separator = args.ContainsKey("2") ? args["2"].Message!.ToPlainText() : " ";
		var limit = 20;

		if (args.ContainsKey("3"))
		{
			if (!int.TryParse(args["3"].Message!.ToPlainText(), out limit) || limit < 1)
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

		// Calculate Levenshtein distance for each word and sort by distance
		var wordLower = word.ToLower();
		var suggestions = vocabulary
			.Select(v => (Word: v, Distance: CalculateLevenshteinDistance(wordLower, v.ToLower())))
			.OrderBy(x => x.Distance)
			.ThenBy(x => x.Word) // Secondary sort by word for consistency
			.Take(limit)
			.Select(x => x.Word);

		return new CallState(string.Join(separator, suggestions));
	}

	/// <summary>
	/// Calculates the Levenshtein distance between two strings.
	/// This is the minimum number of single-character edits (insertions, deletions, or substitutions)
	/// required to change one word into the other.
	/// </summary>
	private static int CalculateLevenshteinDistance(string source, string target)
	{
		if (string.IsNullOrEmpty(source))
		{
			return string.IsNullOrEmpty(target) ? 0 : target.Length;
		}

		if (string.IsNullOrEmpty(target))
		{
			return source.Length;
		}

		// Each row of the distance table depends only on the row before it, so two rows suffice.
		var width = target.Length + 1;
		Span<int> previous = width <= 128 ? stackalloc int[width] : new int[width];
		Span<int> current = width <= 128 ? stackalloc int[width] : new int[width];

		for (var j = 0; j < width; j++)
		{
			previous[j] = j;
		}

		for (var i = 1; i <= source.Length; i++)
		{
			current[0] = i;
			for (var j = 1; j <= target.Length; j++)
			{
				var cost = source[i - 1] == target[j - 1] ? 0 : 1;

				current[j] = Math.Min(
					Math.Min(
						previous[j] + 1,      // Deletion
						current[j - 1] + 1),  // Insertion
					previous[j - 1] + cost);  // Substitution
			}

			var finished = current;
			current = previous;
			previous = finished;
		}

		return previous[target.Length];
	}
}
