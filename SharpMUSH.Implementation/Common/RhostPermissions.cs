namespace SharpMUSH.Implementation.Common;

/// <summary>
/// RhostMUSH's permission words for who may set, clear or see a toggle (<c>@toggledef</c>), as the flag permissions
/// SharpMUSH has: <c>god</c>, <c>wizard</c>, <c>royalty</c> and <c>disabled</c>, and <c>mdark</c> for a toggle only
/// staff see. RhostMUSH's levels between wizard and mortal are <c>royalty</c> here. A word may be shortened to its
/// shortest unambiguous form, as RhostMUSH allows; words SharpMUSH has nothing for are passed over.
/// </summary>
public static class RhostPermissions
{
	/// <summary>Each word, the fewest letters that name it, and the level it is here; null for mortal (no level).</summary>
	private static readonly (string Word, int Shortest, string? Level)[] Words =
	[
		("god", 2, "god"), ("immortal", 3, "wizard"), ("royalty", 3, "wizard"), ("wizard", 3, "wizard"),
		("councilor", 4, "royalty"), ("architect", 4, "royalty"), ("guildmaster", 5, "royalty"),
		("disabled", 4, "disabled"), ("ignore", 6, "disabled"), ("mortal", 1, null)
	];

	private static readonly string[] Levels = ["god", "wizard", "royalty", "disabled"];

	/// <summary>The level a word names (<c>Found</c> false for a word SharpMUSH has nothing for).</summary>
	private static (bool Found, string? Level) Find(string word)
	{
		foreach (var (name, shortest, level) in Words)
		{
			if (word.Length >= shortest && word.Length <= name.Length
				&& name.StartsWith(word, StringComparison.OrdinalIgnoreCase))
			{
				return (true, level);
			}
		}

		return (false, null);
	}

	/// <summary>
	/// <paramref name="current"/> changed by RhostMUSH's words: a level replaces the level, <c>mortal</c> clears it, and
	/// <c>!word</c> takes that level away. Null when no word changes anything.
	/// </summary>
	public static string[]? Apply(IEnumerable<string> current, string words)
	{
		var result = current.ToList();
		var applied = false;
		foreach (var word in words.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			var remove = word.StartsWith('!');
			if (Find(remove ? word[1..] : word) is not (true, var level))
			{
				continue;
			}

			applied = true;
			if (remove)
			{
				if (level is not null)
				{
					result.RemoveAll(existing => existing.Equals(level, StringComparison.OrdinalIgnoreCase));
				}

				continue;
			}

			result.RemoveAll(existing => Levels.Contains(existing, StringComparer.OrdinalIgnoreCase));
			if (level is not null)
			{
				result.Add(level);
			}
		}

		return applied ? [.. result] : null;
	}

	/// <summary>
	/// <c>/see</c>: a staff level hides the toggle from everyone who may not see all (<c>mdark</c>), and <c>mortal</c> or
	/// a level taken away shows it again. Null when no word is a level or <c>mortal</c>.
	/// </summary>
	public static string[]? ApplySee(IEnumerable<string> current, string words)
	{
		bool? hidden = null;
		foreach (var word in words.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			var remove = word.StartsWith('!');
			if (Find(remove ? word[1..] : word) is (true, var level))
			{
				hidden = !remove && level is not null;
			}
		}

		var rest = current.Where(existing => !existing.Equals("mdark", StringComparison.OrdinalIgnoreCase));
		return hidden switch
		{
			null => null,
			true => [.. rest, "mdark"],
			false => [.. rest]
		};
	}

	/// <summary>The permissions, or <c>anyone</c> when there are none.</summary>
	public static string Describe(IEnumerable<string> permissions)
		=> permissions.ToArray() is { Length: > 0 } listed ? string.Join(' ', listed) : "anyone";
}
