using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Handler that builds command attribute cache by scanning object attributes once
/// and pre-compiling all regex patterns. Results are cached automatically by QueryCachingBehavior.
/// </summary>
public class GetCommandAttributesQueryHandler : IQueryHandler<GetCommandAttributesQuery, CommandAttributeCache[]>
{
	public async ValueTask<CommandAttributeCache[]> Handle(GetCommandAttributesQuery request, CancellationToken cancellationToken)
	{
		var sharpObj = request.SharpObject;
		var attributes = sharpObj.Object().AllAttributes.Value;
		var commandAttributes = new List<CommandAttributeCache>();

		await foreach (var attr in attributes.WithCancellation(cancellationToken))
		{
			// Skip attributes with NO_COMMAND flag
			if (attr.Flags.Any(flag => flag.Name == "NO_COMMAND"))
				continue;

			var plainValue = attr.Value.ToPlainText();
			var match = CommandDiscoveryService.CommandPatternRegex().Match(plainValue);

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

				commandAttributes.Add(new CommandAttributeCache(
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
}
