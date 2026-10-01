using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Server.Services;

/// <summary>Resolves the dbref strings the wiki stores for authors and editors to player names.</summary>
public interface IWikiNameResolver
{
	/// <summary>The object's current name, or null when the dbref is blank, malformed or no longer exists.</summary>
	Task<string?> NameOfAsync(string? dbref, CancellationToken cancellationToken = default);
}

/// <summary>
/// One lookup per distinct dbref per request: a page list repeats the same few editors, so the
/// resolver is scoped and remembers what it has already asked the engine.
/// </summary>
public sealed class WikiNameResolver(IMediator mediator) : IWikiNameResolver
{
	private readonly Dictionary<string, string?> _known = new(StringComparer.Ordinal);

	public async Task<string?> NameOfAsync(string? dbref, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(dbref)) return null;
		if (_known.TryGetValue(dbref, out var cached)) return cached;

		var name = await LookupAsync(dbref, cancellationToken);
		_known[dbref] = name;
		return name;
	}

	private async Task<string?> LookupAsync(string dbref, CancellationToken cancellationToken)
	{
		if (!DBRef.TryParse(dbref, out var parsed) || parsed is null) return null;
		return await mediator.Send(new GetObjectNodeQuery(parsed.Value), cancellationToken) is AnySharpObject found
			? found.Object().Name
			: null;
	}
}
