using System.Text.RegularExpressions;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Services;

public partial class CommandDiscoveryService(IFusionCache cache) : ICommandDiscoveryService
{
	public void InvalidateCache(DBRef dbReference)
		=> cache.Remove(dbReference.ToString());

	private async IAsyncEnumerable<(AnySharpObject Obj, SharpAttribute Attr, Match Pattern)> MatchUserDefinedCommandSelectMany(AnySharpObject sharpObj)
	{
		var attributes = await sharpObj.Object().AllAttributes
			.WithCancellation(CancellationToken.None);
					
		var hasNoCommandFlag = attributes.Where(attr =>
			attr.Flags.All(flag => flag.Name != "NO_COMMAND")
			&& CommandPatternRegex().IsMatch(attr.Value.ToPlainText()));
					
		var result = hasNoCommandFlag.Select(attr =>
		{
			var match = CommandPatternRegex().Match(attr.Value.ToPlainText());
			return (Obj: sharpObj, Attr: attr with { CommandListIndex = match.Length }, Pattern: match);
		});
		
		await foreach (var a in result)
		{
			yield return a;
		}
	}
	
	// TODO: Severe optimization needed. We can't keep scanning all attributes each time we want to do a command match, and do conversions.
	// We need to cache the results of the conversion and where that object & attribute live.
	// We don't need to care for the Cache Building if that command was used, we can immediately cache all commands.
	// CONSIDERATION: Do we also need a possible Database-Scan for all commands, and cache them?
	public async ValueTask<Option<IEnumerable<(AnySharpObject SObject, SharpAttribute Attribute, Dictionary<string, CallState> Arguments)>>> MatchUserDefinedCommand(
		IMUSHCodeParser parser,
		IEnumerable<AnySharpObject> objects,
		MString commandString)
	{
		var commandPatternAttributes = await objects
			.ToAsyncEnumerable()
			.Where(async (x,_) => !await x.HasFlag("NO_COMMAND"))
			.SelectMany(MatchUserDefinedCommandSelectMany)
			.ToArrayAsync();

		var convertedCommandPatternAttributes = commandPatternAttributes
			.Select(x =>
				x.Attr.Flags.Any(flag => flag.Name == "REGEX") ?
					(SObject: x.Obj, Attribute: x.Attr, Reg: new Regex(x.Pattern.Value.Remove(x.Pattern.Length - 1, 1).Remove(0, 1))) :
					(SObject: x.Obj, Attribute: x.Attr, Reg: new Regex(MModule.getWildcardMatchAsRegex(
							MModule.single(x.Pattern.Value.Remove(x.Pattern.Length - 1, 1).Remove(0, 1))))));

		var matchedCommandPatternAttributes = convertedCommandPatternAttributes
			.Where(x => x.Reg.IsMatch(MModule.plainText(commandString))).ToList();

		if (matchedCommandPatternAttributes.Count == 0)
		{
			return new None();
		}

		var res = matchedCommandPatternAttributes.Select(match =>
			(match.SObject,
			 match.Attribute,
			 Arguments: match.Reg
				.Matches(MModule.plainText(commandString))
				.SelectMany(x => x.Groups.Values)
				.Skip(match.Attribute.Flags.All(x => x.Name != "REGEX") ? 1 : 0) // Skip the first Group for Wildcard matches, which is the entire Match
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