using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Shared $command attribute scan used by both the per-object command-attribute handler and the
/// type-ancestor contribution handler. Collects all attributes of one object, applies the
/// no_inherit / no_command tree gating, and pre-compiles the $command regex patterns — appending the
/// survivors to <paramref name="commandAttributes"/> while threading the cross-object
/// <c>seenNames</c> / <c>noCommandPrefixes</c> accumulators so child attributes shadow parents and
/// no_command prefixes block tree descendants across objects.
/// </summary>
public static class CommandAttributeScanner
{
	public static async ValueTask ScanAttributes(
		IAsyncEnumerable<SharpAttribute> attributes,
		List<CommandAttributeCache> commandAttributes,
		HashSet<string> seenNames,
		HashSet<string> noCommandPrefixes,
		bool isLocal,
		CancellationToken cancellationToken)
	{
		// Collect all attributes first so we can do proper tree-level flag checks
		// regardless of enumeration order.
		var attrList = new List<SharpAttribute>();
		await foreach (var attr in attributes.WithCancellation(cancellationToken))
			attrList.Add(attr);

		// Build no_inherit prefixes (only matters for parent attrs)
		var noInheritPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!isLocal)
		{
			foreach (var attr in attrList)
			{
				if (attr.Flags.Any(f => f.Name == "no_inherit"))
					noInheritPrefixes.Add((attr.LongName ?? "") + "`");
			}
		}

		// Build no_command prefixes from this object's attrs
		var localNoCommandPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var attr in attrList)
		{
			if (attr.Flags.Any(flag => flag.Name == "no_command"))
				localNoCommandPrefixes.Add((attr.LongName ?? "") + "`");
		}

		foreach (var attr in attrList)
		{
			var longName = attr.LongName ?? "";

			// Skip if no_inherit (or descendant of no_inherit) and we're looking at parent attributes
			if (!isLocal)
			{
				if (attr.Flags.Any(f => f.Name == "no_inherit"))
					continue;
				if (noInheritPrefixes.Any(prefix => longName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
					continue;
			}

			// Track names we've already processed (child overrides parent)
			if (!seenNames.Add(longName))
				continue;

			if (attr.Flags.Any(flag => flag.Name == "no_command"))
			{
				// Block this attribute AND all tree descendants (propagate to cross-object noCommandPrefixes)
				noCommandPrefixes.Add(longName + "`");
				continue;
			}

			// Check if blocked by ancestor's no_command (tree-level blocking) — both local and cross-object
			if (noCommandPrefixes.Any(prefix => longName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
				continue;
			if (localNoCommandPrefixes.Any(prefix => longName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
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
