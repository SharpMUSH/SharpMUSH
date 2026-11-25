using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Models.PlayInput;

/// <summary>A key that runs a command line in Play: <see cref="Key"/> is a chord as <see cref="KeyChord"/> writes it.</summary>
public sealed record KeyShortcut(string Key, string Command);

/// <summary>A short lead that sends the rest of a typed line to <see cref="Channel"/>.</summary>
public sealed record ChannelLead(string Lead, string Channel);

/// <summary>
/// The player's Play input shortcuts: keys that run a command line, leads that send a line to a channel, and the
/// escape character that sends a line as typed.
/// </summary>
/// <remarks>
/// Leads change only what the player types; a key's command goes exactly as written. A line is matched on its
/// own, so a box of several lines may send some to a channel and others as typed. With no leads there is nothing
/// to skip, so the escape character is an ordinary character and every line goes as typed.
/// </remarks>
public sealed record InputShortcuts(IReadOnlyList<KeyShortcut> Keys, IReadOnlyList<ChannelLead> Leads, char Escape)
{
	/// <summary>The escape character a player has not changed.</summary>
	public const char DefaultEscape = '/';

	/// <summary>The most keys and the most leads a player may keep.</summary>
	public const int MaxKeys = 40, MaxLeads = 20;

	/// <summary>The longest a lead may be.</summary>
	public const int MaxLeadLength = 8;

	/// <summary>The longest a key's command may be.</summary>
	public const int MaxCommandLength = 2000;

	/// <summary>Characters MUSH reads at the start of a line already: a pose, a semipose and a say.</summary>
	public static readonly char[] ReservedStarts = [':', ';', '"'];

	/// <summary>
	/// Characters MUSH commands start with: <c>@</c>-commands, <c>&amp;</c> attribute sets, <c>+</c>-commands, <c>\</c>
	/// emits, <c>#</c> forces and <c>$</c>. The escape character cannot be one, or the player's own commands would lose
	/// it, and a lead that starts with one must end in a letter or a digit, so <c>+pub</c> works and <c>+</c> does not
	/// take every <c>+</c>-command.
	/// </summary>
	public static readonly char[] CommandStarts = ['@', '&', '+', '\\', '#', '$'];

	public static readonly InputShortcuts None = new([], [], DefaultEscape);

	/// <summary>
	/// The command a typed line sends: as typed after the escape character, <c>@chat</c> to a lead's channel, else
	/// unchanged. The longest lead that matches wins, so <c>,,</c> and <c>,</c> can both be leads.
	/// </summary>
	public string Expand(string line)
	{
		if (Leads.Count == 0) return line;
		if (line.Length > 1 && line[0] == Escape) return line[1..];

		// The longest lead the line starts with is the one it meant; with nothing after it, the line goes as typed.
		foreach (var lead in Leads.OrderByDescending(l => l.Lead.Length))
		{
			if (Matches(line, lead.Lead) is not string message) continue;
			return message.Length > 0 ? $"@chat {lead.Channel}={message}" : line;
		}

		return line;
	}

	/// <summary>
	/// The message after <paramref name="lead"/>, which may be empty, or nothing when the line does not start with it. A lead that ends
	/// in a letter or a digit needs a space after it, so <c>pub</c> leaves <c>publish</c> alone; one that ends in a
	/// symbol does not, so <c>,hi</c> works.
	/// </summary>
	private static Found<string> Matches(string line, string lead)
	{
		if (!line.StartsWith(lead, StringComparison.OrdinalIgnoreCase)) return new NotFound();
		var rest = line[lead.Length..];
		if (char.IsLetterOrDigit(lead[^1]) && rest.Length > 0 && !char.IsWhiteSpace(rest[0])) return new NotFound();
		return rest.TrimStart();
	}

	/// <summary>The command a key runs, if one is bound to <paramref name="chord"/>.</summary>
	public Found<string> CommandFor(string chord) =>
		Keys.FirstOrDefault(k => string.Equals(k.Key, chord, StringComparison.Ordinal)) is { } key
			? key.Command
			: new NotFound();

	/// <summary>
	/// What is wrong with <paramref name="lead"/> as one more lead, given the escape character and the leads kept
	/// already (<paramref name="others"/>, without the one being edited).
	/// </summary>
	public static LeadProblem Check(string lead, char escape, IEnumerable<ChannelLead> others)
	{
		if (lead.Length == 0) return LeadProblem.Empty;
		if (lead.Length > MaxLeadLength) return LeadProblem.TooLong;
		if (lead.Any(char.IsWhiteSpace)) return LeadProblem.Space;
		if (ReservedStarts.Contains(lead[0])) return LeadProblem.Reserved;
		if (lead[0] == escape) return LeadProblem.Escape;
		if (CommandStarts.Contains(lead[0]) && !char.IsLetterOrDigit(lead[^1])) return LeadProblem.CommandStart;
		return others.Any(o => string.Equals(o.Lead, lead, StringComparison.OrdinalIgnoreCase))
			? LeadProblem.Duplicate
			: LeadProblem.None;
	}

	/// <summary>What is wrong with <paramref name="escape"/> as the escape character, given the leads kept.</summary>
	public static EscapeProblem CheckEscape(char escape, IEnumerable<ChannelLead> leads)
	{
		if (char.IsLetterOrDigit(escape) || char.IsWhiteSpace(escape) || char.IsControl(escape)) return EscapeProblem.NotASymbol;
		if (ReservedStarts.Contains(escape)) return EscapeProblem.Reserved;
		if (CommandStarts.Contains(escape)) return EscapeProblem.CommandStart;
		return leads.Any(l => l.Lead.Length > 0 && l.Lead[0] == escape) ? EscapeProblem.StartsALead : EscapeProblem.None;
	}

	/// <summary>
	/// The shortcuts as kept, with anything a player could not have saved dropped: an unknown or reserved key, a
	/// second binding of a key, an empty command, a lead the rules refuse, past the limits. Stored values are read
	/// this way, so a value written by hand or by a later version never breaks the input.
	/// </summary>
	public static InputShortcuts Sanitize(IEnumerable<KeyShortcut>? keys, IEnumerable<ChannelLead>? leads, string? escape)
	{
		var esc = escape is { Length: 1 } e && CheckEscape(e[0], []) == EscapeProblem.None ? e[0] : DefaultEscape;

		List<KeyShortcut> keptKeys = [];
		foreach (var key in keys ?? [])
		{
			if (key?.Key is null || key.Command is null) continue;
			if (!KeyChord.Allowed(key.Key) || keptKeys.Any(k => k.Key == key.Key)) continue;
			if (string.IsNullOrWhiteSpace(key.Command) || key.Command.Length > MaxCommandLength) continue;
			keptKeys.Add(key);
			if (keptKeys.Count == MaxKeys) break;
		}

		List<ChannelLead> keptLeads = [];
		foreach (var lead in leads ?? [])
		{
			if (lead?.Lead is null || string.IsNullOrWhiteSpace(lead.Channel) || lead.Channel.Contains('=')) continue;
			if (Check(lead.Lead, esc, keptLeads) != LeadProblem.None) continue;
			keptLeads.Add(lead with { Channel = lead.Channel.Trim() });
			if (keptLeads.Count == MaxLeads) break;
		}

		return new InputShortcuts(keptKeys, keptLeads, esc);
	}
}

/// <summary>Why a lead cannot be kept.</summary>
public enum LeadProblem
{
	None,
	Empty,
	TooLong,
	Space,

	/// <summary>Starts with <c>:</c>, <c>;</c> or <c>"</c>, which pose and say.</summary>
	Reserved,

	/// <summary>Starts with the escape character, which would send the line as typed instead.</summary>
	Escape,

	/// <summary>Starts with a character MUSH commands start with and ends in a symbol, so it would take those commands.</summary>
	CommandStart,
	Duplicate,
}

/// <summary>Why a character cannot be the escape character.</summary>
public enum EscapeProblem
{
	None,

	/// <summary>A letter, a digit, a space or a control character: it would take ordinary commands.</summary>
	NotASymbol,

	/// <summary><c>:</c>, <c>;</c> or <c>"</c>, which pose and say.</summary>
	Reserved,

	/// <summary>A character MUSH commands start with, such as <c>@</c>: those commands would lose it.</summary>
	CommandStart,

	/// <summary>A lead starts with it, so that lead could never be typed.</summary>
	StartsALead,
}
