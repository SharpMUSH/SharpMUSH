using SharpMUSH.Library.Plugins.Storage;
using SurrealDb.Net.Models.Response;

namespace SharpMUSH.Database.SurrealDB;

/// <summary>
/// Surfaces the SurrealDB provider's query entry points and id helper through the host-shared
/// <see cref="ISurrealStorageAccessor"/> seam, so a storage plugin can run its own provider-native
/// SurrealQL while the parameter-inlining/escaping in <c>ExecuteAsync</c> stays authoritative on the
/// core side. The explicit implementations forward to the existing private helpers.
/// </summary>
public partial class SurrealDatabase : ISurrealStorageAccessor
{
	ValueTask<SurrealDbResponse> ISurrealStorageAccessor.ExecuteAsync(string query, CancellationToken ct) =>
		ExecuteAsync(query, ct);

	ValueTask<SurrealDbResponse> ISurrealStorageAccessor.ExecuteAsync(string query,
		IReadOnlyDictionary<string, object?> parameters, CancellationToken ct) =>
		ExecuteAsync(query, parameters, ct);

	string ISurrealStorageAccessor.NormalizeId(string id, string table) =>
		NormalizeSurrealId(id, table);
}
