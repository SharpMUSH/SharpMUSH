using Core.Arango;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Plugins.Storage;

/// <summary>
/// Host-shared seam that exposes the ArangoDB provider's live connection and the small set of
/// primitive helpers a storage plugin needs, <b>without</b> leaking any subsystem-specific concept back
/// into core. A storage plugin (e.g. SharpMUSH.Plugins.Scene) takes this via constructor injection and
/// issues its own provider-native AQL against <see cref="Context"/>/<see cref="Handle"/>.
/// </summary>
/// <remarks>
/// This interface lives in <c>SharpMUSH.Library</c> (host-shared) so host and plugin unify on the same
/// <see cref="System.Type"/> across the plugin <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.
/// The Core.Arango client types it surfaces are shared from the host ALC by the plugin loader.
/// </remarks>
public interface IArangoStorageAccessor
{
	/// <summary>The live Arango context the provider built its connection on.</summary>
	IArangoContext Context { get; }

	/// <summary>The Arango database handle the provider operates against.</summary>
	ArangoHandle Handle { get; }

	/// <summary>
	/// Resolves a dbref to its live typed object node (name snapshot + vertex id). The generic
	/// object-resolution primitive a storage plugin needs to capture object-edge references.
	/// </summary>
	ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default);
}
