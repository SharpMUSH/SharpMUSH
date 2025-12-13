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
	/// <summary>
	/// Duration for which command attribute cache entries remain valid.
	/// After this time, the cache will be rebuilt on next access.
	/// </summary>
	private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

	/// <summary>
	/// Cache entry for command attributes with pre-compiled regex patterns.
	/// </summary>
	private record CachedCommandAttribute(
		SharpAttribute Attribute,
		Regex CompiledRegex,
		bool IsRegexFlag);

	public void InvalidateCache(DBRef dbReference)
		=> cache.Remove(GetCacheKey(dbReference));

	private static string GetCacheKey(DBRef dbReference)
		=> $"commands:{dbReference}";

	/// <summary>
	/// Gets cached command attributes for an object, building the cache if necessary.
	/// This replaces the O(n) scan with O(1) cache lookup.
	/// </summary>
	private async ValueTask<CachedCommandAttribute[]> GetCachedCommandAttributesAsync(AnySharpObject sharpObj)
	{
		var cacheKey = GetCacheKey(sharpObj.Object().DBRef);
		
		var cached = await cache.GetOrSetAsync(
			cacheKey,
			async _ => await BuildCommandCacheAsync(sharpObj),
			options => options.SetDuration(CacheDuration));

		return cached ?? [];
	}

	/// <summary>
	/// Builds the command cache for an object by scanning attributes once
	/// and pre-compiling all regex patterns.
	/// </summary>
	private async ValueTask<CachedCommandAttribute[]> BuildCommandCacheAsync(AnySharpObject sharpObj)
	{
		var attributes = sharpObj.Object().AllAttributes.Value;
		var commandAttributes = new List<CachedCommandAttribute>();

		await foreach (var attr in attributes)
		{
			// Skip attributes with NO_COMMAND flag
			if (attr.Flags.Any(flag => flag.Name == "NO_COMMAND"))
				continue;

			var plainValue = attr.Value.ToPlainText();
			var match = CommandPatternRegex().Match(plainValue);
			
			if (!match.Success)
				continue;

			// Extract command pattern and determine if it's REGEX or wildcard
			var pattern = match.Value.Remove(match.Length - 1, 1).Remove(0, 1);
			var isRegex = attr.Flags.Any(flag => flag.Name == "REGEX");
			
			try
			{
				// Pre-compile the regex pattern
				var regex = isRegex
					? new Regex(pattern, RegexOptions.Compiled)
					: new Regex(MModule.getWildcardMatchAsRegex(MModule.single(pattern)), RegexOptions.Compiled);

				commandAttributes.Add(new CachedCommandAttribute(
					attr with { CommandListIndex = match.Length },
					regex,
					isRegex));
			}
			catch (ArgumentException)
			{
				// Invalid regex pattern, skip this attribute
				continue;
			}
		}

		return [.. commandAttributes];
	}

	private async IAsyncEnumerable<(AnySharpObject Obj, SharpAttribute Attr, Regex Regex, bool IsRegex)> MatchUserDefinedCommandSelectMany(AnySharpObject sharpObj)
	{
		// Use cached command attributes instead of scanning all attributes
		var cachedCommands = await GetCachedCommandAttributesAsync(sharpObj);

		foreach (var cached in cachedCommands)
		{
			yield return (sharpObj, cached.Attribute, cached.CompiledRegex, cached.IsRegexFlag);
		}
	}

	/// <summary>
	/// Matches user-defined commands with optimized caching.
	/// Uses pre-compiled regex patterns and avoids repeated attribute scanning.
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