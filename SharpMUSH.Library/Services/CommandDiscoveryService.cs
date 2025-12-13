using System.Text.RegularExpressions;
using Mediator;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public partial class CommandDiscoveryService(IMediator mediator) : ICommandDiscoveryService
{
	public void InvalidateCache(DBRef dbReference)
	{
		// No-op: Cache invalidation is now handled by the Mediator pipeline
		// via CacheInvalidationBehavior when attribute commands are executed
	}

	private async IAsyncEnumerable<(AnySharpObject Obj, SharpAttribute Attr, Regex Regex, bool IsRegex)> MatchUserDefinedCommandSelectMany(AnySharpObject sharpObj)
	{
		// Use Mediator query to get cached command attributes
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

		var plainCommandString = MModule.plainText(commandString);
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
				.SelectMany(x => x.Groups.Values)
				.Skip(!match.IsRegex ? 1 : 0) // Skip the first Group for Wildcard matches, which is the entire Match
				.SelectMany<Group, KeyValuePair<string, MString>>(x => [
					new KeyValuePair<string, MString>(x.Index.ToString(), MModule.substring(x.Index, x.Length, commandString)),
					new KeyValuePair<string, MString>(x.Name, MModule.substring(x.Index, x.Length, commandString))
					])
				.GroupBy(kv => kv.Key)
				.ToDictionary(kv => kv.Key, kv => new CallState(kv.First().Value, 0))
			));

		return Option<IEnumerable<(AnySharpObject SObject, SharpAttribute Attribute, Dictionary<string, CallState> Arguments)>>
			.FromOption(res);
	}

	[GeneratedRegex(@"^\$.+?(?<!\\)(?:\\\\)*\:", RegexOptions.Singleline)]
	public static partial Regex CommandPatternRegex();
}