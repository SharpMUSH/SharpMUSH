using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Handler that builds listen attribute cache by scanning object attributes
/// and pre-compiling all regex patterns. Results are cached automatically by QueryCachingBehavior.
/// </summary>
public class GetListenAttributesQueryHandler : IQueryHandler<GetListenAttributesQuery, ListenAttributeCache[]>
{
	/// <summary>
	/// Regex to match ^-listen patterns at start of attribute value
	/// </summary>
	private static readonly Regex ListenPatternRegex = new(@"^\^([^:]+):", RegexOptions.Compiled);

	public async ValueTask<ListenAttributeCache[]> Handle(GetListenAttributesQuery request, CancellationToken cancellationToken)
	{
		var sharpObj = request.SharpObject;
		var attributes = sharpObj.Object().AllAttributes.Value;
		var listenAttributes = new List<ListenAttributeCache>();

		await foreach (var attr in attributes.WithCancellation(cancellationToken))
		{
			// Skip attributes with NO_COMMAND flag (applies to listen patterns too)
			if (attr.Flags.Any(flag => flag.Name == "NO_COMMAND"))
				continue;

			var plainValue = attr.Value.ToPlainText();
			var match = ListenPatternRegex.Match(plainValue);
			
			if (!match.Success)
				continue;

			// Extract listen pattern
			var pattern = match.Groups[1].Value;
			var isRegex = attr.Flags.Any(flag => flag.Name == "REGEX");
			
			// Determine behavior based on attribute flags
			var behavior = ListenBehavior.AHear; // Default
			if (attr.Flags.Any(flag => flag.Name == "AAHEAR"))
				behavior = ListenBehavior.AAHear;
			else if (attr.Flags.Any(flag => flag.Name == "AMHEAR"))
				behavior = ListenBehavior.AMHear;
			
			try
			{
				// Pre-compile the regex pattern
				var regex = isRegex
					? new Regex(pattern, RegexOptions.Compiled)
					: new Regex(MModule.getWildcardMatchAsRegex(MModule.single(pattern)), RegexOptions.Compiled);

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
