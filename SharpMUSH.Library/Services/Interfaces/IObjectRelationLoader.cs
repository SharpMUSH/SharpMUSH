using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// How a database provider resolves, for an object it is building, the relations that live
/// outside the object's own document and point at other objects: its location, owner, parent and
/// zone. The provider asks and does not know the answer comes from the Mediator's cached,
/// tag-invalidated queries; the host supplies the implementation. This keeps caching policy in
/// the layer that owns it and the providers as pure storage mappers, and a loaded object never
/// memoises another object: each read resolves afresh and follows invalidation.
/// </summary>
public interface IObjectRelationLoader
{
	/// <summary>The container of the typed object <paramref name="typedId"/> whose base object is <paramref name="objectId"/>.</summary>
	Task<AnySharpContainer> LocationOf(string typedId, string objectId, CancellationToken cancellationToken);

	Task<SharpPlayer> OwnerOf(string objectId, int number, CancellationToken cancellationToken);

	Task<AnyOptionalSharpObject> ParentOf(string objectId, int number, CancellationToken cancellationToken);

	Task<AnyOptionalSharpObject> ZoneOf(string objectId, int number, CancellationToken cancellationToken);
}
