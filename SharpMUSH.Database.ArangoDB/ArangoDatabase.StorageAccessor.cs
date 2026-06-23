using Core.Arango;
using SharpMUSH.Library.Plugins.Storage;

namespace SharpMUSH.Database.ArangoDB;

/// <summary>
/// Surfaces the ArangoDB provider's connection (and the object-resolution primitive) through the
/// host-shared <see cref="IArangoStorageAccessor"/> seam, so a storage plugin can run its own
/// provider-native AQL without the provider knowing anything about the plugin's subsystem.
/// <see cref="GetObjectNodeAsync"/> is already public on another partial; the context/handle are the
/// constructor-captured fields.
/// </summary>
public partial class ArangoDatabase : IArangoStorageAccessor
{
	IArangoContext IArangoStorageAccessor.Context => arangoDb;

	ArangoHandle IArangoStorageAccessor.Handle => handle;
}
