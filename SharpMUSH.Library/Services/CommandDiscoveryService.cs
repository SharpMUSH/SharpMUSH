using Microsoft.Extensions.Caching.Memory;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services;

public partial class CommandDiscoveryService : ICommandDiscoveryService
{
	private static readonly MemoryCache cache = new(new MemoryCacheOptions());

	public void InvalidateCache(DBRef DBReference) 
		=> cache.Remove(DBReference);

	// TODO: Severe optimization needed. We can't keep scanning all attributes each time we want to do a command match, and do conversions.
	// We need to cache the results of the conversion and where that object & attribute live.
	// We don't need to care for the Cache Building if that command was used, we can immediately cache all commands.
	// CONSIDERATION: Do we also need a possible Database-Scan for all commands, and cache them?
	public Option<IEnumerable<(AnySharpObject SObject, SharpAttribute Attribute, Dictionary<string, CallState> Arguments)>> MatchUserDefinedCommand(
		IMUSHCodeParser parser,
		IEnumerable<AnySharpObject> objects,
		MString commandString)
	{
		var commandPatternAttributes = objects
			.Where(x => !x.HasFlag("NO_COMMAND"))
			.SelectMany(sharpObj =>
				cache.GetOrCreate(
					sharpObj.Object().DBRef,
					_ => sharpObj.Object().AllAttributes()
						.Where(attr =>
							attr.Flags.All(flag => flag.Name != "NO_COMMAND")
							&& CommandPatternRegex().IsMatch(MModule.plainText(attr.Value)))
						.Select(attr =>
							{
								var match = CommandPatternRegex().Match(MModule.plainText(attr.Value));
								attr.CommandListIndex = match.Length;
								return (Obj: sharpObj, Attr: attr, Pattern: match);
							})
						) ?? []
					);

		var convertedCommandPatternAttributes = commandPatternAttributes
			.Select(x =>
				x.Attr.Flags.Any(flag => flag.Name == "REGEX") ?
					(SObject: x.Obj, Attribute: x.Attr, Reg: new Regex(x.Pattern.Value.Remove(x.Pattern.Length - 1, 1).Remove(0, 1))) :
					(SObject: x.Obj, Attribute: x.Attr, Reg: new Regex(MModule.getWildcardMatchAsRegex(
							MModule.single(x.Pattern.Value.Remove(x.Pattern.Length - 1, 1).Remove(0, 1))))));

		var matchedCommandPatternAttributes = convertedCommandPatternAttributes
			.Where(x => x.Reg.IsMatch(MModule.plainText(commandString)));

		if (!matchedCommandPatternAttributes.Any())
		{
			return new None();
		}

		var res = matchedCommandPatternAttributes.Select(match =>
			(match.SObject,
			 match.Attribute,
			 Arguments: match.Reg
				.Matches(MModule.plainText(commandString))
				.SelectMany(x => x.Groups.Values)
				.Skip(!match.Attribute.Flags.Any(x => x.Name == "REGEX") ? 1 : 0) // Skip the first Group for Wildcard matches, which is the entire Match
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