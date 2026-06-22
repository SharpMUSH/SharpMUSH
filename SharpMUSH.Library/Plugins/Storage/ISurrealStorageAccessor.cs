using SurrealDb.Net.Models.Response;

namespace SharpMUSH.Library.Plugins.Storage;

/// <summary>
/// Host-shared seam that exposes the SurrealDB provider's query entry points and id helper to a storage
/// plugin. The provider's <c>ExecuteAsync</c> inlines parameters (the embedded CBOR serializer cannot bind
/// a <c>Dictionary&lt;string, object?&gt;</c>) and escapes string values; surfacing it keeps that escaping
/// authoritative on the core side. Generic — carries no subsystem concept.
/// </summary>
/// <remarks>
/// This interface lives in <c>SharpMUSH.Library</c> (host-shared) so host and plugin unify on the same
/// <see cref="System.Type"/> across the plugin <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.
/// The SurrealDb.Net client types it surfaces are shared from the host ALC by the plugin loader.
/// </remarks>
public interface ISurrealStorageAccessor
{
	/// <summary>Executes a parameterless SurrealQL query and returns the raw response.</summary>
	ValueTask<SurrealDbResponse> ExecuteAsync(string query, CancellationToken ct = default);

	/// <summary>
	/// Executes a parameterized SurrealQL query. Parameter values are inlined and string-escaped by the
	/// provider (the embedded CBOR serializer cannot bind a mixed-type dictionary directly).
	/// </summary>
	ValueTask<SurrealDbResponse> ExecuteAsync(string query, IReadOnlyDictionary<string, object?> parameters,
		CancellationToken ct = default);

	/// <summary>
	/// Normalizes an id to a SurrealDB record id (<c>table:key</c>): pass-through if it already contains a
	/// <c>:</c>, otherwise prefixes <paramref name="table"/> and strips any <c>collection/</c> prefix.
	/// </summary>
	string NormalizeId(string id, string table);
}
