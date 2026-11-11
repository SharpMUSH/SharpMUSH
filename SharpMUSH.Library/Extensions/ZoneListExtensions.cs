using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.Extensions;

public static class ZoneListExtensions
{
	/// <summary>The zones <paramref name="obj"/> is in, its zone first; empty when it has none. See <see cref="IZoneListStore"/>.</summary>
	/// <remarks>An object with no zone has no further ones, so the list is read only for one that has a zone.</remarks>
	public static async ValueTask<IReadOnlyList<AnySharpObject>> ZonesOfAsync(this IMediator mediator, AnySharpObject obj,
		CancellationToken cancellationToken = default)
		=> await obj.Object().Zone.WithCancellation(cancellationToken) is AnySharpObject
			? await mediator.Send(new GetZonesOfQuery(obj.Object().DBRef), cancellationToken)
			: [];
}
