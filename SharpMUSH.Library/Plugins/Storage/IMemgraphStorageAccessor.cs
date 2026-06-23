using Neo4j.Driver;

namespace SharpMUSH.Library.Plugins.Storage;

/// <summary>
/// Host-shared seam that exposes the Memgraph (Neo4j Bolt) provider's live driver to a storage plugin,
/// which opens its own sessions and runs provider-native Cypher. Generic — carries no subsystem concept.
/// </summary>
/// <remarks>
/// This interface lives in <c>SharpMUSH.Library</c> (host-shared) so host and plugin unify on the same
/// <see cref="System.Type"/> across the plugin <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.
/// The Neo4j.Driver client types it surfaces are shared from the host ALC by the plugin loader.
/// </remarks>
public interface IMemgraphStorageAccessor
{
	/// <summary>The live Neo4j driver the provider built its connection on.</summary>
	IDriver Driver { get; }
}
