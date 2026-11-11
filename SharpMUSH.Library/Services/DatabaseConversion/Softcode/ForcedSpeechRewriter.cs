using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services.DatabaseConversion.Softcode;

/// <summary>
/// TinyMUX's and RhostMUSH's <c>@femit</c>, <c>@fpose</c> and <c>@fsay</c>, written as SharpMUSH's <c>@force</c>: the
/// object runs <c>@emit</c>, <c>@lemit</c>, <c>pose</c>, <c>semipose</c> or <c>say</c> with the message. Only the
/// command's head changes. <c>@force</c> evaluates the message as the one forcing, as TinyMUX's commands do, and
/// <c>/noeval</c> keeps the forced command from evaluating it a second time.
/// </summary>
public static partial class ForcedSpeechRewriter
{
	/// <summary>Rewrites every forced-speech command in <paramref name="softcode"/>.</summary>
	public static SoftcodeRewriteResult Rewrite(string softcode)
	{
		if (softcode.IndexOf("@f", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return new SoftcodeRewriteResult(softcode, []);
		}

		var notes = new List<string>();
		var text = ForcedSpeech().Replace(softcode, match => RewriteOne(match, notes));
		return new SoftcodeRewriteResult(text, notes) { Changed = text != softcode };
	}

	private static string RewriteOne(Match match, List<string> notes)
	{
		var command = match.Groups["command"].Value.ToLowerInvariant();
		var switches = match.Groups["switches"].Value.Split('/', StringSplitOptions.RemoveEmptyEntries)
			.Select(typed => typed.ToLowerInvariant())
			.ToArray();
		bool Has(string name) => switches.Any(typed => name.StartsWith(typed, StringComparison.Ordinal));

		string[] known = command is "@fpose" ? ["nospace", "default"] : command is "@fsay" ? Array.Empty<string>() : ["here", "room"];
		if (switches.Any(typed => !known.Any(name => name.StartsWith(typed, StringComparison.Ordinal))))
		{
			notes.Add($"check by hand: {match.Value.Trim()} has a switch SharpMUSH's @force cannot carry");
			return match.Value;
		}

		var runs = command switch
		{
			"@fpose" => Has("nospace") ? "semipose/noeval" : "pose/noeval",
			"@fsay" => "say/noeval",
			_ => Has("room") ? "@lemit/noeval" : "@emit/noeval"
		};
		if (command is "@fe" or "@femit" && Has("here") && Has("room"))
		{
			notes.Add($"{match.Value.Trim()} emits only in the outermost room now; it also emitted where the object stood");
		}

		notes.Add($"{match.Value.Trim()} written as @force ... ={runs}");
		return $"{match.Groups["lead"].Value}@force {match.Groups["object"].Value.Trim()}={runs} ";
	}

	/// <summary>
	/// <c>@femit</c> (or <c>@fe</c>), <c>@fpose</c> or <c>@fsay</c> at the start of a command, with its switches and the
	/// object up to the <c>=</c>. The lead is as <see cref="HookCommandRewriter"/> reads it. The object stops at a
	/// comma, so an <c>@switch</c> pattern or a function argument is not read as one, and holds no <c>&lt;</c> or
	/// <c>&gt;</c>, as help text writes a placeholder.
	/// </summary>
	[GeneratedRegex(@"(?<lead>(?:^[$^][^:\r\n]*:|^|[;{,=])[ \t]*)(?<command>@femit|@fe|@fpose|@fsay)(?<switches>(?:/[A-Za-z]+)*)[ \t]+(?<object>[^\s;{}=,<>][^;{}=,<>\r\n]*?)[ \t]*=[ \t]*",
		RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex ForcedSpeech();
}
