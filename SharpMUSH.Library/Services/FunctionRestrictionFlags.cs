using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// The restriction words that change how a function runs rather than who may call it: PennMUSH's
/// <c>func_restrictions</c> (<c>src/function.c</c>) other than the ones <c>check_func</c> tests.
/// <c>apply_restrictions</c> sets each word's bit, and a leading <c>!</c> clears it.
/// </summary>
public static class FunctionRestrictionFlags
{
	/// <summary>Penn's FN_NOSIDEFX: a side-effect function answers <c>#-1 FUNCTION DISABLED</c>.</summary>
	public const string NoSideEffects = "nosidefx";

	private static readonly Dictionary<string, FunctionFlags> Words = new(StringComparer.OrdinalIgnoreCase)
	{
		["logname"] = FunctionFlags.LogName,
		["logargs"] = FunctionFlags.LogArgs,
		["deprecated"] = FunctionFlags.Deprecated,
		["localize"] = FunctionFlags.Localize,
		["noparse"] = FunctionFlags.NoParse
	};

	/// <summary>Whether <paramref name="word"/>, without a leading <c>!</c>, is one of these words.</summary>
	public static bool IsFlagWord(string word)
		=> Words.ContainsKey(word) || word.Equals(NoSideEffects, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// <paramref name="flags"/> with the words of <paramref name="restriction"/> applied in order. PennMUSH
	/// logs only a built-in's calls (<c>src/parse.c:3025-3041</c>), and <c>noparse</c> is for @functions
	/// alone here, so a built-in keeps its own way of reading its arguments.
	/// </summary>
	public static FunctionFlags Apply(FunctionFlags flags, string? restriction, bool userFunction)
	{
		foreach (var (word, clear) in Terms(restriction))
		{
			if (!Words.TryGetValue(word, out var flag)
				|| (userFunction ? flag is FunctionFlags.LogName or FunctionFlags.LogArgs : flag is FunctionFlags.NoParse))
			{
				continue;
			}

			flags = clear ? flags & ~flag : flags | flag;
		}

		return flags;
	}

	/// <summary>Whether <paramref name="restriction"/> leaves <c>nosidefx</c> set.</summary>
	public static bool ForbidsSideEffects(string? restriction)
		=> Terms(restriction)
			.Where(term => term.Word.Equals(NoSideEffects, StringComparison.OrdinalIgnoreCase))
			.Select(term => !term.Clear)
			.LastOrDefault();

	private static IEnumerable<(string Word, bool Clear)> Terms(string? restriction)
		=> (restriction ?? string.Empty)
			.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries)
			.Select(term => term.StartsWith('!') ? (term[1..], true) : (term, false));
}
