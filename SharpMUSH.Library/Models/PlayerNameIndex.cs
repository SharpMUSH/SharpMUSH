namespace SharpMUSH.Library.Models;

/// <summary>A player's name and the aliases it answers to.</summary>
public sealed record PlayerNameEntry(DBRef DBRef, string Name, string[] Aliases);

/// <summary>
/// The names <c>renderplayers()</c> looks for in a text, keyed without regard to case: full names and,
/// when asked, aliases first; when asked, the first word of a name with spaces where nothing else holds
/// it. A name two players answer to at the same rank stands for neither.
/// </summary>
public sealed class PlayerNameIndex
{
	/// <summary>The player a name stands for, or none when two players answer to it.</summary>
	private sealed record Candidate(DBRef? Player, int Tier);

	private readonly Dictionary<string, Candidate> _names;
	private readonly Dictionary<string, Candidate>.AlternateLookup<ReadOnlySpan<char>> _lookup;
	private readonly int[] _lengths;

	private PlayerNameIndex(Dictionary<string, Candidate> names)
	{
		_names = names;
		_lookup = names.GetAlternateLookup<ReadOnlySpan<char>>();
		_lengths = [.. names.Keys.Select(key => key.Length).Distinct().OrderDescending()];
	}

	/// <summary>Whether there is no name to look for.</summary>
	public bool IsEmpty => _names.Count == 0;

	public static PlayerNameIndex Build(IReadOnlyCollection<PlayerNameEntry> players, bool aliases, bool firstNames)
	{
		var names = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);

		void Add(string name, DBRef player, int tier)
		{
			name = name.Trim();
			if (name.Length == 0) return;

			if (!names.TryGetValue(name, out var held) || held.Tier > tier)
			{
				names[name] = new Candidate(player, tier);
			}
			else if (held.Tier == tier && held.Player is { } other && other.Number != player.Number)
			{
				names[name] = held with { Player = null };
			}
		}

		foreach (var player in players)
		{
			Add(player.Name, player.DBRef, 0);
			if (!aliases) continue;
			foreach (var alias in player.Aliases) Add(alias, player.DBRef, 0);
		}

		if (firstNames)
		{
			foreach (var player in players)
			{
				var name = player.Name.Trim();
				var space = name.IndexOf(' ');
				if (space > 0) Add(name[..space], player.DBRef, 1);
			}
		}

		return new PlayerNameIndex(names);
	}

	/// <summary>
	/// The longest name that one player answers to starting at <paramref name="start"/> of
	/// <paramref name="plain"/> and ending on a word boundary, with its length; length 0 when there is none.
	/// </summary>
	public (DBRef Player, int Length) MatchAt(string plain, int start)
	{
		foreach (var length in _lengths)
		{
			var end = start + length;
			if (end > plain.Length || end < plain.Length && IsNameCharacter(plain[end])) continue;
			if (_lookup.TryGetValue(plain.AsSpan(start, length), out var candidate) && candidate.Player is { } player)
			{
				return (player, length);
			}
		}

		return (default, 0);
	}

	/// <summary>A character that continues a word, so a name may not start right after it or end right before it.</summary>
	public static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';
}
