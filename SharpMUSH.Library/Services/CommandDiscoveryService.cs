using Mediator;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
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
	/// </summary>
	[GeneratedRegex(@"^\$.+?(?<!\\)(?:\\\\)*\:", RegexOptions.Singleline)]
	public static partial Regex CommandPatternRegex();

	/// <summary>
	/// The <c>^pattern:</c> prefix of a listen attribute — the same split as
	/// <see cref="CommandPatternRegex"/> for the <c>^</c> dialect, and the one
	/// <c>GetListenAttributesQueryHandler</c> compiles its listen regex from.
	/// <para>
	/// Lives here rather than beside that handler so the layout engine (which must know where an
	/// attribute's match data ends) and the handler share one definition. Note <c>[^:]+</c> excludes
	/// only <c>:</c>, so — like the <c>Singleline</c> <c>.</c> above — the pattern half may span a
	/// newline; anything reading this must agree with the handler about that, not narrow it.
	/// </para>
	/// </summary>
	[GeneratedRegex(@"^\^([^:]+):")]
	public static partial Regex ListenPatternRegex();
}