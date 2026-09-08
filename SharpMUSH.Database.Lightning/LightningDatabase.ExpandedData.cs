using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using System.Text.Json;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IExpandedDataStore"/>: typed JSON side-documents attached to an object or the server.
/// Reflection-based <see cref="JsonSerializer"/> is used here rather than <see cref="Codec"/>'s
/// source-generated context, because the stored type is whatever caller-supplied <c>T</c> or
/// <c>dynamic</c> value comes through the interface, not one of <see cref="LightningJsonContext"/>'s
/// fixed set of provider record types.
/// </summary>
public partial class LightningDatabase
{
	private static readonly JsonSerializerOptions ExpandedDataJsonOptions = new() { PropertyNamingPolicy = null };

	/// <summary>Merges <paramref name="newBytes"/> over <paramref name="existingBytes"/>: every property
	/// present in the new document with a non-null value overrides the existing one; everything else
	/// (including properties the new document omits, or sets to null) is kept from the existing document.</summary>
	private static byte[] MergeExpandedData(byte[] existingBytes, byte[] newBytes)
	{
		var existingDoc = JsonSerializer.Deserialize<JsonElement>(existingBytes, ExpandedDataJsonOptions);
		var newDoc = JsonSerializer.Deserialize<JsonElement>(newBytes, ExpandedDataJsonOptions);

		var merged = new Dictionary<string, JsonElement>();
		foreach (var prop in existingDoc.EnumerateObject())
		{
			merged[prop.Name] = prop.Value;
		}
		foreach (var prop in newDoc.EnumerateObject())
		{
			if (prop.Value.ValueKind != JsonValueKind.Null)
			{
				merged[prop.Name] = prop.Value;
			}
		}

		return JsonSerializer.SerializeToUtf8Bytes(merged, ExpandedDataJsonOptions);
	}

	public async ValueTask SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data, CancellationToken cancellationToken = default)
	{
		var key = Keys.Composite(ParseDbref(sharpObjectId), dataType);
		var newBytes = JsonSerializer.SerializeToUtf8Bytes((object)data, ExpandedDataJsonOptions);

		await Store.WriteAsync(tx =>
		{
			var bytes = tx.TryGet(Tables.ExpandedObj, key, out var existing) ? MergeExpandedData(existing, newBytes) : newBytes;
			tx.Put(Tables.ExpandedObj, key, bytes);
		}, cancellationToken);
	}

	public ValueTask<T?> GetExpandedObjectData<T>(string sharpObjectId, string dataType, CancellationToken cancellationToken = default)
	{
		var key = Keys.Composite(ParseDbref(sharpObjectId), dataType);
		var bytes = Store.Read(tx => tx.TryGet(Tables.ExpandedObj, key, out var v) ? v : null);
		return ValueTask.FromResult(bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, ExpandedDataJsonOptions));
	}

	/// <summary>
	/// Unlike <see cref="SetExpandedObjectData"/> this replaces the stored document rather than merging
	/// over it, so a null property clears the stored one — the write @motd uses to clear a single message,
	/// document).
	/// </summary>
	public async ValueTask SetExpandedServerData(string dataType, dynamic data, CancellationToken cancellationToken = default)
	{
		var key = Keys.Str(dataType);
		var newBytes = JsonSerializer.SerializeToUtf8Bytes((object)data, ExpandedDataJsonOptions);

		await Store.WriteAsync(tx => tx.Put(Tables.ExpandedSrv, key, newBytes), cancellationToken);
	}

	public ValueTask<T?> GetExpandedServerData<T>(string dataType, CancellationToken cancellationToken = default)
	{
		var key = Keys.Str(dataType);
		var bytes = Store.Read(tx => tx.TryGet(Tables.ExpandedSrv, key, out var v) ? v : null);
		return ValueTask.FromResult(bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, ExpandedDataJsonOptions));
	}
}
