using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Utilities;

/// <summary>Child-first listen visibility and bounded traversal over complete local snapshots.</summary>
public sealed class ListenAttributeSearch
{
	private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _blocked = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<DBRef> _visited = [];

	/// <summary>Private inherited trees are invisible; visible NO_COMMAND trees remain blocked at farther levels.</summary>
	public IEnumerable<SharpAttribute> Visible(SharpAttribute[] attributes, bool inherited, CancellationToken cancellationToken = default)
	{
		var privateRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var attribute in attributes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (inherited && attribute.IsNoInherit()) privateRoots.Add(attribute.LongName);
		}
		foreach (var attribute in attributes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!InTree(privateRoots, attribute.LongName) && !InTree(_blocked, attribute.LongName)
				&& attribute.Flags.Any(flag => flag.Name.Equals("NO_COMMAND", StringComparison.OrdinalIgnoreCase)))
				_blocked.Add(attribute.LongName);
		}
		foreach (var attribute in attributes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!InTree(privateRoots, attribute.LongName) && !InTree(_blocked, attribute.LongName) && _seen.Add(attribute.LongName))
				yield return attribute;
		}
	}

	private static bool InTree(HashSet<string> roots, string name)
	{
		if (roots.Contains(name)) return true;
		for (var separator = name.IndexOf('`'); separator >= 0; separator = name.IndexOf('`', separator + 1))
			if (roots.Contains(name[..separator])) return true;
		return false;
	}

	/// <summary>Visits the root and at most maxParents parents; another phase shares visibility and cycle state.</summary>
	public async ValueTask<ListenAttributeCache[]> ReadPhaseAsync(IMediator mediator, DBRef root,
		uint maxParents, bool inherited, CancellationToken cancellationToken)
	{
		var result = new List<ListenAttributeCache>();
		var reference = root;
		uint depth = 0;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (await mediator.Send(new GetObjectNodeQuery(reference), cancellationToken) is not AnySharpObject current
				|| !_visited.Add(current.Object().DBRef)) break;
			var attributes = await mediator.Send(new GetListenAttributeSnapshotQuery(current.Object().DBRef), cancellationToken);
			result.AddRange(Compile(Visible(attributes, inherited || depth != 0, cancellationToken), cancellationToken));
			if (depth == maxParents) break;
			if (await current.Object().Parent.WithCancellation(cancellationToken) is not AnySharpObject parent) break;
			reference = parent.Object().DBRef;
			depth++;
		}
		return [.. result];
	}

	/// <summary>Compiles eligible definitions using the shared bounded regular-expression cache.</summary>
	public static ListenAttributeCache[] Compile(IEnumerable<SharpAttribute> attributes, CancellationToken cancellationToken = default)
	{
		var result = new List<ListenAttributeCache>();
		foreach (var attribute in attributes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var match = CommandDiscoveryService.ListenPatternRegex().Match(attribute.Value.ToPlainText());
			if (!match.Success) continue;
			var pattern = CommandDiscoveryService.UnescapePatternSeparator(match.Groups["pattern"].Value);
			var isRegex = attribute.IsRegexp();
			var behavior = attribute.Flags.Any(flag => flag.Name.Equals("AAHEAR", StringComparison.OrdinalIgnoreCase))
				? ListenBehavior.AAHear
				: attribute.Flags.Any(flag => flag.Name.Equals("AMHEAR", StringComparison.OrdinalIgnoreCase)) ? ListenBehavior.AMHear : ListenBehavior.AHear;
			try
			{
				var options = RegexOptions.Compiled | (attribute.IsCase() ? RegexOptions.None : RegexOptions.IgnoreCase);
				var regex = isRegex ? SoftcodeRegex.Create(pattern, options)
					: SoftcodeRegex.Wildcard(pattern, options, caseSensitive: attribute.IsCase());
				result.Add(new ListenAttributeCache(attribute, regex, isRegex, behavior));
			}
			catch (ArgumentException) { /* An invalid definition still shadows farther definitions. */ }
		}
		return [.. result];
	}
}
