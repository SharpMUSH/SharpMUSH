using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="IWikiReaderRoles"/>
public sealed class WikiReaderRoles(IMediator mediator) : IWikiReaderRoles
{
	public async ValueTask<IReadOnlySet<string>> RolesOfAsync(string dbref, CancellationToken ct = default)
	{
		if (!DBRef.TryParse(dbref, out var parsed) || parsed is not { } reference) return new HashSet<string>();
		return await mediator.Send(new GetObjectNodeQuery(reference), ct) is AnySharpObject found
			? WikiAccessService.RoleSlugs(await found.GrantsAsync(ct))
			: new HashSet<string>();
	}
}
