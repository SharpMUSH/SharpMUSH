using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Handler that builds command attribute cache by scanning object attributes once
/// and pre-compiling all regex patterns. Results are cached automatically by QueryCachingBehavior.
/// Traverses parent chain for inherited $commands (respecting no_inherit and tree-level no_command).
/// </summary>
public class GetCommandAttributesQueryHandler : IQueryHandler<GetCommandAttributesQuery, CommandAttributeCache[]>
{
	public async ValueTask<CommandAttributeCache[]> Handle(GetCommandAttributesQuery request, CancellationToken cancellationToken)
	{
		var sharpObj = request.SharpObject;
		var commandAttributes = new List<CommandAttributeCache>();
		var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var noCommandPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// Scan local attributes first
		await ScanAttributes(sharpObj.Object().AllAttributes.Value, commandAttributes, seenNames,
			noCommandPrefixes, isLocal: true, cancellationToken);

		// Walk parent chain for inherited commands
		var current = sharpObj.Object();
		while (true)
		{
			var parent = await current.Parent.WithCancellation(cancellationToken);
			if (parent.IsNone) break;

			var parentObj = parent.Known.Object();
			await ScanAttributes(parentObj.AllAttributes.Value, commandAttributes, seenNames,
				noCommandPrefixes, isLocal: false, cancellationToken);

			current = parentObj;
		}

		return [.. commandAttributes];
	}

	private static async ValueTask ScanAttributes(
		IAsyncEnumerable<SharpAttribute> attributes,
		List<CommandAttributeCache> commandAttributes,
		HashSet<string> seenNames,
		HashSet<string> noCommandPrefixes,
		bool isLocal,
		CancellationToken cancellationToken)
	{
		await foreach (var attr in attributes.WithCancellation(cancellationToken))
		{
			var longName = attr.LongName ?? "";

			// Skip if no_inherit and we're looking at parent attributes
			if (!isLocal && attr.Flags.Any(f => f.Name == "no_inherit"))
				continue;

			// Track names we've already processed (child overrides parent)
			if (!seenNames.Add(longName))
				continue;

			// Check if this attribute has no_command flag
			if (attr.Flags.Any(flag => flag.Name == "no_command"))
			{
				// Block this attribute AND all tree descendants
				noCommandPrefixes.Add(longName + "`");
				continue;
			}

			// Check if blocked by ancestor's no_command (tree-level blocking)
			if (noCommandPrefixes.Any(prefix => longName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
				continue;

			var plainValue = attr.Value.ToPlainText();
			var match = CommandDiscoveryService.CommandPatternRegex().Match(plainValue);

			if (!match.Success)
				continue;

			// Extract command pattern and determine if it's REGEX or wildcard
			var pattern = match.Value.Remove(match.Length - 1, 1).Remove(0, 1);
			var isRegex = attr.Flags.Any(flag => flag.Name.Equals("REGEXP", StringComparison.OrdinalIgnoreCase));
			// Skip any optional leading whitespace so that "$cmd: @pemit" and "$cmd:@pemit" are
			// both handled correctly — a leading space would otherwise cause an empty command name
			// when EvaluateCommands strips the first token at its space boundary.
			var commandBodyStart = match.Length;
			while (commandBodyStart < plainValue.Length && plainValue[commandBodyStart] == ' ')
				commandBodyStart++;

			try
			{
				// Pre-compile the regex pattern
				var regex = isRegex
					? new Regex(pattern, RegexOptions.Compiled)
					: new Regex(MModule.getWildcardMatchAsRegex(MModule.single(pattern)), RegexOptions.Compiled);

				commandAttributes.Add(new CommandAttributeCache(
					attr with { CommandListIndex = commandBodyStart },
					regex,
					isRegex));
			}
			catch (ArgumentException)
			{
				// Invalid regex pattern, skip this attribute
				continue;
			}
		}
	}
}
