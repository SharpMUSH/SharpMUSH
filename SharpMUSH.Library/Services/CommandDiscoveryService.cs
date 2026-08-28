using Mediator;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;
using System.Text.RegularExpressions;

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
		var trimmedCommandString = MModule.trim(commandString, " ", global::MarkupString.TrimType.TrimBoth);
		var plainCommandString = MModule.plainText(trimmedCommandString);
		var matchedCommandPatternAttributes = await commandPatternAttributes
			.Where(x => x.Regex.IsMatch(plainCommandString))
			.ToArrayAsync();

		if (matchedCommandPatternAttributes.Length == 0)
		{
			return new None();
		}

		var res = matchedCommandPatternAttributes.Select(match =>
			(match.Obj,
			 match.Attr,
			 Arguments: match.Regex
				.Matches(plainCommandString)
				.SelectMany(matchResult => matchResult.Groups.Cast<Group>()
					.Select((group, groupIndex) => (group, groupIndex))
					.Skip(!match.IsRegex ? 1 : 0)) // Skip the first Group for Wildcard matches, which is the entire Match
				.SelectMany<(Group group, int groupIndex), KeyValuePair<string, MString>>(x =>
					match.IsRegex
						// For regex patterns: generate both numeric index key and named capture group key
						? [
							new KeyValuePair<string, MString>(x.groupIndex.ToString(), MModule.substring(x.group.Index, x.group.Length, trimmedCommandString)),
							new KeyValuePair<string, MString>(x.group.Name, MModule.substring(x.group.Index, x.group.Length, trimmedCommandString))
							]
						// For wildcard patterns: generate only numeric index key (0-based) to avoid key
						// collisions between a group's auto-generated name (e.g. "1") and the next
						// group's 0-based index key (also "1"), which would cause wrong %1, %2 values.
						: [
							new KeyValuePair<string, MString>((x.groupIndex - 1).ToString(), MModule.substring(x.group.Index, x.group.Length, trimmedCommandString))
							])
				.GroupBy(kv => kv.Key)
				.ToDictionary(kv => kv.Key, kv => new CallState(kv.First().Value, 0))
			));

		return Option<IEnumerable<(AnySharpObject SObject, SharpAttribute Attribute, Dictionary<string, CallState> Arguments)>>
			.FromOption(res);
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