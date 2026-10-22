namespace SharpMUSH.Configuration;

/// <summary>
/// Where the game colours a name with its <c>@moniker</c>, and for which types of object: PennMUSH's <c>AN_*</c>
/// bits (<c>hdrs/dbdefs.h</c>), which its <c>monikers</c> option holds.
/// </summary>
[Flags]
public enum MonikerPlace
{
	None = 0,
	Chat = 1 << 0,
	Say = 1 << 1,
	Movement = 1 << 2,
	Look = 1 << 3,
	Unparse = 1 << 4,
	Who = 1 << 5,
	System = 1 << 6,
	Announcements = 1 << 7,
	Players = 1 << 8,
	Things = 1 << 9,
	Rooms = 1 << 10,
	Exits = 1 << 11,
	Everywhere = Chat | Say | Movement | Look | Unparse | Who | System | Announcements,
	AllTypes = Players | Things | Rooms | Exits
}

/// <summary>Reads and writes the <c>monikers</c> option, a space-separated list of <see cref="Words"/>.</summary>
public static class MonikerPlaces
{
	/// <summary>The option's words, in the order PennMUSH lists them (<c>cf_priv_monikers</c>, <c>src/conf.c</c>).</summary>
	public static readonly IReadOnlyList<(string Word, MonikerPlace Place)> Words =
	[
		("alltypes", MonikerPlace.AllTypes),
		("players", MonikerPlace.Players),
		("things", MonikerPlace.Things),
		("rooms", MonikerPlace.Rooms),
		("exits", MonikerPlace.Exits),
		("everywhere", MonikerPlace.Everywhere),
		("chat", MonikerPlace.Chat),
		("say", MonikerPlace.Say),
		("movement", MonikerPlace.Movement),
		("look", MonikerPlace.Look),
		("unparse", MonikerPlace.Unparse),
		("who", MonikerPlace.Who),
		("system", MonikerPlace.System),
		("announcements", MonikerPlace.Announcements)
	];

	/// <summary>Speech, for every type of object: what the option meant before it was a list.</summary>
	public const string Default = "alltypes say";

	/// <summary>
	/// The places and types <paramref name="value"/> names, as PennMUSH's <c>string_to_privs</c> reads them: a word
	/// may be shortened to any prefix (the first word in <see cref="Words"/> it starts wins), <c>!</c> before a word
	/// takes it away, and the removals apply after everything named. An unknown word is skipped. Null is
	/// <see cref="Default"/>, the value a world stored before the option existed reads as.
	/// </summary>
	public static MonikerPlace Parse(string? value)
	{
		var (named, removed) = (MonikerPlace.None, MonikerPlace.None);
		foreach (var token in (value ?? Default).Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			var not = token.StartsWith('!');
			if (Find(not ? token[1..] : token) is not { } place) continue;
			if (not) removed |= place;
			else named |= place;
		}

		return named & ~removed;
	}

	/// <summary>The words that name <paramref name="places"/>, <c>everywhere</c> and <c>alltypes</c> standing for
	/// their parts.</summary>
	public static string Format(MonikerPlace places)
	{
		var (words, covered) = (new List<string>(), MonikerPlace.None);
		foreach (var (word, place) in Words)
		{
			if ((places & place) != place || (covered & place) == place) continue;
			words.Add(word);
			covered |= place;
		}

		return string.Join(' ', words);
	}

	/// <summary><paramref name="value"/> written with whole words, the form the option is stored in.</summary>
	public static string Normalize(string? value) => Format(Parse(value ?? string.Empty));

	private static MonikerPlace? Find(string word) =>
		word.Length == 0
			? null
			: Words.FirstOrDefault(w => w.Word.StartsWith(word, StringComparison.OrdinalIgnoreCase)) is ({ }, var place)
				? place
				: null;
}
