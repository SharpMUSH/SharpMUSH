using Neo4j.Driver;
using SharpMUSH.Library.Plugins.Storage;

namespace SharpMUSH.Database.Memgraph;

/// <summary>
/// Surfaces the Memgraph provider's live Neo4j driver through the host-shared
/// <see cref="IMemgraphStorageAccessor"/> seam, so a storage plugin can open its own sessions and run
/// provider-native Cypher without the provider knowing anything about the plugin's subsystem.
/// </summary>
public partial class MemgraphDatabase : IMemgraphStorageAccessor
{
	IDriver IMemgraphStorageAccessor.Driver => driver;
}
