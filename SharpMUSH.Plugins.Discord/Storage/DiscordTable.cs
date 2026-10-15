using System.Buffers.Binary;
using System.Text.Json;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Plugins.Discord.Storage;

/// <summary>
/// One plugin-owned table keyed by a Discord id (eight big-endian bytes), each value a record as JSON. A record this
/// version cannot read is left out rather than stopping every other one.
/// </summary>
public sealed class DiscordTable<T> where T : class
{
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null };

	private readonly ILightningStorageAccessor _accessor;
	private readonly TableDef _table;

	public DiscordTable(ILightningStorageAccessor accessor, string name)
	{
		_accessor = accessor;
		// Opening a table must not happen inside a write job, so it happens here, once.
		_table = accessor.OpenTable(name, duplicates: false);
	}

	public IReadOnlyList<T> List()
		=> _accessor.Read(tx => tx.Range(_table, []).Select(entry => Deserialize(entry.Value)).OfType<T>().ToList());

	public T? Get(ulong id)
		=> _accessor.Read(tx => tx.TryGet(_table, Key(id), out var value) ? Deserialize(value) : null);

	public async ValueTask SaveAsync(ulong id, T record, CancellationToken cancellationToken = default)
	{
		var value = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
		await _accessor.WriteAsync(tx =>
		{
			tx.Put(_table, Key(id), value);
			return true;
		}, cancellationToken);
	}

	/// <returns>Whether there was a record to remove.</returns>
	public async ValueTask<bool> RemoveAsync(ulong id, CancellationToken cancellationToken = default)
		=> await _accessor.WriteAsync(tx => tx.Delete(_table, Key(id)), cancellationToken);

	/// <summary>Removes every record keyed below <paramref name="id"/>; a Discord id grows with time, so these are the older ones.</summary>
	/// <returns>How many went.</returns>
	public async ValueTask<int> RemoveBelowAsync(ulong id, CancellationToken cancellationToken = default)
	{
		var below = Key(id);
		return await _accessor.WriteAsync(tx =>
		{
			var keys = tx.Range(_table, [])
				.Select(entry => entry.Key)
				.TakeWhile(key => key.AsSpan().SequenceCompareTo(below) < 0)
				.ToList();
			foreach (var key in keys)
			{
				tx.Delete(_table, key);
			}

			return keys.Count;
		}, cancellationToken);
	}

	private static byte[] Key(ulong id)
	{
		var key = new byte[sizeof(ulong)];
		BinaryPrimitives.WriteUInt64BigEndian(key, id);
		return key;
	}

	private static T? Deserialize(byte[] value)
	{
		try
		{
			return JsonSerializer.Deserialize<T>(value, JsonOptions);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
