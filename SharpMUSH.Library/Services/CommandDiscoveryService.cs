using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services;

public partial class CommandDiscoveryService: ICommandDiscoveryService
{
	// TODO: Severe optimization. We can't keep scanning all attributes each time we want to do a command match, and do conversions.
	// We need to cache the results of the conversion and where that object & attribute live.
	// We don't need to care for the Cache Building if that command was used, we can immediately cache all commands.
	// CONSIDERATION: Do we also need a possible Database-Scan for all commands, and cache them?
	public ValueTask<Option<Func<IMUSHCodeParser, ValueTask<Option<CallState>>>>> MatchUserDefinedCommand(IMUSHCodeParser parser, AnySharpObject[] objects, MString commandString)
	{
		var filteredObjects = objects.Where(x => !x.HasFlag("NO_COMMAND"));

		var commandPatternAttributes = filteredObjects
			.SelectMany(sharpObj => sharpObj.Object().Attributes()
				.Where(attr =>
					attr.Flags().All(flag => flag.Name != "NO_COMMAND")
					&& CommandPatternRegex().IsMatch(MModule.plainText(attr.Value)))
				.Select(attr => 
					(Obj: sharpObj, Attr: attr, Pattern: CommandPatternRegex().Match(MModule.plainText(attr.Value)))));

		var convertedCommandPatternAttributes = commandPatternAttributes
			.Select(x =>
				x.Attr.Flags().Any(flag => flag.Name == "REGEX") ?
					(x.Obj, x.Attr, Reg: new Regex(x.Pattern.Value)) :
					(x.Obj, x.Attr, Reg: new Regex(MModule.getWildcardMatchAsRegex(MModule.single(x.Pattern.Value)))));

		var matchedCommandPatternAttributes = convertedCommandPatternAttributes
			.Where(x => x.Reg.IsMatch(MModule.plainText(commandString)));

		if (!matchedCommandPatternAttributes.Any())
		{
			// return ValueTask.FromResult(new None()); ???
		}

		foreach (var (Obj, Attr, Reg) in matchedCommandPatternAttributes)
		{
			// ???
		}

		throw new NotImplementedException();
	}

	[GeneratedRegex(@"^\$.+?(?<!\\)(?:\\\\)*\:", RegexOptions.Singleline)]
	public static partial Regex CommandPatternRegex();
}