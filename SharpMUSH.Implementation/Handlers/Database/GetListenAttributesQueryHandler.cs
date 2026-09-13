using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Handler that builds listen attribute cache by scanning object attributes
/// and pre-compiling all regex patterns. Results are cached automatically by QueryCachingBehavior.
/// </summary>
public class GetListenAttributesQueryHandler : IQueryHandler<GetListenAttributesQuery, ListenAttributeCache[]>
{
	public async ValueTask<ListenAttributeCache[]> Handle(GetListenAttributesQuery request, CancellationToken cancellationToken)
	{
		var sharpObj = request.SharpObject;
		var attributes = sharpObj.Object().AllAttributes.Value;
		var listenAttributes = new List<ListenAttributeCache>();

		await foreach (var attr in attributes.WithCancellation(cancellationToken))
		{
			// Skip attributes with NO_COMMAND flag (applies to listen patterns too)
			if (attr.Flags.Any(flag => flag.Name.Equals("NO_COMMAND", StringComparison.OrdinalIgnoreCase)))
				continue;

			var plainValue = attr.Value.ToPlainText();
			var match = CommandDiscoveryService.ListenPatternRegex().Match(plainValue);

			if (!match.Success)
				continue;

			// Same separator unescaping as CommandAttributeScanner — Penn runs one scan for both sigils.
			var pattern = CommandDiscoveryService.UnescapePatternSeparator(match.Groups["pattern"].Value);
			var isRegex = attr.IsRegexp();
			var behavior = ListenBehavior.AHear;
			if (attr.Flags.Any(flag => flag.Name.Equals("AAHEAR", StringComparison.OrdinalIgnoreCase)))
				behavior = ListenBehavior.AAHear;
			else if (attr.Flags.Any(flag => flag.Name.Equals("AMHEAR", StringComparison.OrdinalIgnoreCase)))
				behavior = ListenBehavior.AMHear;

			try
			{
				var options = RegexOptions.Compiled | (attr.IsCase() ? RegexOptions.None : RegexOptions.IgnoreCase);
				var regex = isRegex
					? SoftcodeRegex.Create(pattern, options)
					: SoftcodeRegex.Wildcard(pattern, options, caseSensitive: !options.HasFlag(RegexOptions.IgnoreCase));

				listenAttributes.Add(new ListenAttributeCache(
					attr,
					regex,
					isRegex,
					behavior));
			}
			catch (ArgumentException)
			{
				// Invalid regex pattern, skip this attribute
				continue;
			}
		}

		return [.. listenAttributes];
	}
}
