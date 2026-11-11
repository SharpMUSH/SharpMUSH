using System.Text.RegularExpressions;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;

namespace SharpMUSH.Library.Services.DatabaseConversion.Softcode;

/// <summary>
/// RhostMUSH's <c>@totem &lt;target&gt;=&lt;totem&gt; !&lt;totem&gt; ...</c>, written as SharpMUSH's <c>@role/assign</c> and
/// <c>@role/unassign</c>: a totem is a role here (<see cref="RhostDatabaseTranslator.TotemSlug"/>). A form with a
/// switch, without <c>=</c>, or with a totem name that is computed is left as written and noted.
/// </summary>
public static partial class TotemCommandRewriter
{
	/// <summary>Rewrites every <c>@totem</c> command in <paramref name="softcode"/>, with one note per change or doubt.</summary>
	public static string Rewrite(string softcode, List<string> notes)
		=> softcode.Contains("@totem", StringComparison.OrdinalIgnoreCase)
			? TotemCommand().Replace(softcode, match => RewriteOne(match, notes))
			: softcode;

	private static string RewriteOne(Match match, List<string> notes)
	{
		var lead = match.Groups["lead"].Value;
		var target = match.Groups["target"].Value.Trim();
		var words = match.Groups["totems"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (match.Groups["switches"].Value.Length > 0 || target.Length == 0 || words.Length == 0
			|| words.Any(word => word.AsSpan().IndexOfAny('%', '[', '(') >= 0))
		{
			notes.Add($"check by hand: {match.Value[lead.Length..].Trim()} is not an @totem the import can write with @role, as totems are roles here");
			return match.Value;
		}

		var commands = words
			.Select(word => word.StartsWith('!')
				? $"@role/unassign {target}={RhostDatabaseTranslator.TotemSlug(word[1..])}"
				: $"@role/assign {target}={RhostDatabaseTranslator.TotemSlug(word)}")
			.ToList();
		const string note = "@totem written as @role/assign and @role/unassign, as totems are roles here; " +
			"they run only for those who may assign the role, and tell the runner each change";
		if (!notes.Contains(note)) notes.Add(note);
		return commands.Count > 1 && lead.TrimEnd() is [.., ',' or '=']
			? $"{lead}{{{string.Join(";", commands)}}}"
			: lead + string.Join(";", commands);
	}

	/// <summary>
	/// <c>@totem</c> at the start of a command (of the attribute, of a $-command's or ^-listen's action, after <c>;</c>,
	/// <c>{</c>, or a <c>,</c> or <c>=</c> that starts an action), with its switches, target and totem list, up to the end
	/// of that command or a <c>,</c>, <c>)</c> or <c>]</c> that ends an action or a call around it (a totem name holds
	/// none).
	/// </summary>
	[GeneratedRegex(@"(?<lead>(?:^[$^][^:\r\n]*:|^|[;{,=])[ \t]*)@totem(?<switches>(?:/[A-Za-z]+)*)(?:[ \t]+(?<target>[^=;{}\r\n]*)(?:=(?<totems>[^;{},\r\n]*?))?)?(?=$|[;},)\]\r])",
		RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex TotemCommand();
}
