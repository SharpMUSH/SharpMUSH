using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Library.Services;

public partial class CommandDiscoveryService(IMediator mediator) : ICommandDiscoveryService
{
	private async IAsyncEnumerable<(AnySharpObject Obj, SharpAttribute Attr, Regex Regex, bool IsRegex)> MatchUserDefinedCommandSelectMany(AnySharpObject sharpObj)
	{
		var cachedCommands = await mediator.Send(new GetCommandAttributesQuery(sharpObj));

		foreach (var cached in cachedCommands)
		{
			yield return (sharpObj, cached.Attribute, cached.CompiledRegex, cached.IsRegexFlag);
		}
	}

	/// <summary>
	/// Matches user-defined commands with optimized caching.
	/// Uses pre-compiled regex patterns via Mediator query pipeline.
	/// </summary>
	public async ValueTask<Option<IEnumerable<(AnySharpObject SObject, SharpAttribute Attribute, Dictionary<string, CallState> Arguments)>>> MatchUserDefinedCommand(
		IMUSHCodeParser parser,
		IAsyncEnumerable<AnySharpObject> objects,
		MString commandString)
	{
		var commandPatternAttributes = objects
			.Where(async (x, _) => !await x.HasFlag("NO_COMMAND"))
			.SelectMany(MatchUserDefinedCommandSelectMany);

		// Strip leading/trailing spaces before matching: the compiled $command patterns are anchored
		// at both ends (^...$), so a command typed (or queued) with surrounding whitespace — e.g.
		// " test" — would otherwise fail to match and produce a "Huh?". PennMUSH strips this whitespace
		// before command matching. Trim the MString itself (not just the plain text) so the argument
		// capture indices below stay aligned with the string the regex actually matched against.
		var trimmedCommandString = commandString.Trim(TrimType.TrimBoth);
		var plainCommandString = trimmedCommandString.ToPlainText();
		var matchedCommandPatternAttributes = await commandPatternAttributes
			.Where(x => SoftcodeRegex.IsMatch(x.Regex, plainCommandString))
			.ToArrayAsync();

		if (matchedCommandPatternAttributes.Length == 0)
		{
			return new None();
		}

		var res = matchedCommandPatternAttributes.Select(match =>
			(match.Obj,
			 match.Attr,
			 Arguments: CaptureArguments(match.Regex, match.IsRegex, plainCommandString, trimmedCommandString)));

		return Option<IEnumerable<(AnySharpObject SObject, SharpAttribute Attribute, Dictionary<string, CallState> Arguments)>>
			.FromOption(res);
	}

	/// <summary>
	/// The registers a matched pattern binds, cut from the styled command text at the offsets the
	/// plain-text match reported. A regexp pattern binds every group by index and by name; a
	/// wildcard pattern binds its stars from %0 upward and skips group 0, the whole match, so that
	/// a group's auto-generated name (e.g. "1") can never collide with the next star's index. The
	/// first binding of a key wins, as an unnamed group's name is its own index.
	/// </summary>
	private static Dictionary<string, CallState> CaptureArguments(Regex regex, bool isRegex, string plain,
		MString trimmed)
	{
		var arguments = new Dictionary<string, CallState>();
		if (SoftcodeRegex.Match(regex, plain) is not { Success: true } match)
		{
			return arguments;
		}

		foreach (var (index, group) in match.Groups.Values.Index().Skip(isRegex ? 0 : 1))
		{
			var captured = trimmed.Substring(group.Index, group.Length);

			if (isRegex)
			{
				arguments.TryAdd(index.ToString(), new CallState(captured, 0));
				arguments.TryAdd(group.Name, new CallState(captured, 0));
			}
			else
			{
				arguments.TryAdd((index - 1).ToString(), new CallState(captured, 0));
			}
		}

		return arguments;
	}

	/// <summary>
	/// The <c>$pattern:</c> prefix of a <c>$</c>-command attribute. Everything the match covers is
	/// match data compiled to a wildcard/regex by <c>CommandAttributeScanner</c>; only the text after
	/// it is ever parsed as a command list. The lookbehind is what makes <c>\:</c> an escaped colon
	/// and <c>\\:</c> a real terminator, so no caller may substitute a plain <c>IndexOf(':')</c>.
	/// <para>
	/// The <c>pattern</c> group is the match half exactly as stored, backslashes and all. Anything
	/// that compiles it must first put it through <see cref="UnescapePatternSeparator"/>; anything
	/// that only needs to know where the code starts wants <c>Match.Length</c> instead.
	/// </para>
	/// <para>
	/// <c>.*?</c> rather than <c>.+?</c>: <c>set_cmd_flags</c> scans from the sigil itself
	/// (<c>src/attrib.c:844-856</c>), so <c>$:action</c> is a command whose pattern is empty — it
	/// matches only empty input, but it is a command, and Penn's own matcher compiles it.
	/// </para>
	/// </summary>
	[GeneratedRegex(@"^\$(?<pattern>.*?(?<!\\)(?:\\\\)*)\:", RegexOptions.Singleline)]
	public static partial Regex CommandPatternRegex();

	/// <summary>
	/// The <c>^pattern:</c> prefix of a listen attribute — the same split as
	/// <see cref="CommandPatternRegex"/> for the <c>^</c> dialect, and the one
	/// <c>GetListenAttributesQueryHandler</c> compiles its listen regex from.
	/// <para>
	/// Lives here rather than beside that handler so the layout engine (which must know where an
	/// attribute's match data ends) and the handler share one definition. Like the <c>Singleline</c>
	/// <c>.</c> above, the pattern half may span a newline; anything reading this must agree with the
	/// handler about that, not narrow it.
	/// </para>
	/// <para>
	/// Escape handling is not an analogy with the <c>$</c> dialect, it is the same code:
	/// <c>set_cmd_flags</c> falls <c>^</c> through into <c>$</c> and runs one escape-aware scan for
	/// both (<c>src/attrib.c:844-856</c>), and <c>atr_single_match_r</c> takes the terminator as a
	/// parameter. A naive <c>[^:]+</c> here — which is what this was — cut
	/// <c>^&lt;pattern with \: in it&gt;:</c> at the escaped colon, so every listen using one compiled
	/// the wrong pattern or failed to compile at all.
	/// </para>
	/// </summary>
	[GeneratedRegex(@"^\^(?<pattern>.*?(?<!\\)(?:\\\\)*)\:", RegexOptions.Singleline)]
	public static partial Regex ListenPatternRegex();

	/// <summary>
	/// Turns the raw <c>pattern</c> group of <see cref="CommandPatternRegex"/> or
	/// <see cref="ListenPatternRegex"/> into the string PennMUSH actually compiles: <c>\:</c> collapses
	/// to a literal <c>:</c>, and every other backslash survives verbatim — including both halves of
	/// <c>\\</c>, and a trailing lone one (<c>atr_single_match_r</c>, <c>src/attrib.c:1786-1798</c>).
	/// <para>
	/// This is what lets a regexp <c>$</c>-command contain a non-capturing group. The <c>:</c> in
	/// <c>(?:...)</c> would otherwise terminate the pattern, so it is written <c>(?\:...)</c> — and
	/// unless it is unescaped again, .NET is handed <c>(?\:</c>, which is not a valid construct, and
	/// the attribute is silently dropped as an uncompilable pattern. Wildcard patterns break more
	/// quietly still: the backslash survives <c>Regex.Escape</c> and becomes a character the typed
	/// input has to contain.
	/// </para>
	/// </summary>
	public static string UnescapePatternSeparator(string pattern)
	{
		if (!pattern.Contains('\\'))
		{
			return pattern;
		}

		var builder = new StringBuilder(pattern.Length);
		for (var i = 0; i < pattern.Length; i++)
		{
			if (pattern[i] != '\\' || i + 1 == pattern.Length)
			{
				builder.Append(pattern[i]);
				continue;
			}

			if (pattern[i + 1] == ':')
			{
				builder.Append(':');
			}
			else
			{
				// Not ours to interpret: hand the escape on to the wildcard or regex compiler intact.
				builder.Append(pattern[i]).Append(pattern[i + 1]);
			}

			i++;
		}

		return builder.ToString();
	}
}