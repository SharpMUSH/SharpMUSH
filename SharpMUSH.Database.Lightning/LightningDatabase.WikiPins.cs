using System.Buffers.Binary;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// The categories pinned to the wiki home: one row per category key in <see cref="Tables.WikiPin"/>. The row's
/// presence is the pin; its value is the pin's position on the wiki home, a big-endian 32-bit number. A value of
/// any other length (the seeded pins, and every pin a build without ordering wrote) has position 0, so those
/// come first, in key order. A build that reads only presence still reads every row this one writes.
/// </summary>
public partial class LightningDatabase
{
	private static readonly byte[] WikiPinValue = [1];

	private static byte[] WikiPinKey(string category) => Keys.Str(category);

	private static byte[] WikiPinPosition(int position)
	{
		var value = new byte[sizeof(int)];
		BinaryPrimitives.WriteInt32BigEndian(value, position);
		return value;
	}

	private static int ReadWikiPinPosition(ReadOnlySpan<byte> value)
		=> value.Length == sizeof(int) ? BinaryPrimitives.ReadInt32BigEndian(value) : 0;

	private static List<(string Key, int Position)> ReadWikiPins(ITx tx)
		=> tx.Range(Tables.WikiPin, [])
			.Select(entry => (Key: Keys.ReadStr(entry.Key), Position: ReadWikiPinPosition(entry.Value)))
			.OrderBy(pin => pin.Position)
			.ThenBy(pin => pin.Key, StringComparer.Ordinal)
			.ToList();

	public Task<IReadOnlyList<string>> GetPinnedCategoriesAsync()
		=> Task.FromResult<IReadOnlyList<string>>(Store.Read(tx => ReadWikiPins(tx).Select(pin => pin.Key).ToList()));

	public async Task<bool> SetCategoryPinnedAsync(string category, bool pinned)
	{
		return await Store.WriteAsync(tx =>
		{
			var key = WikiPinKey(category);
			if (tx.TryGet(Tables.WikiPin, key, out _) == pinned) return false;
			if (pinned)
			{
				// A new pin goes last.
				var pins = ReadWikiPins(tx);
				tx.Put(Tables.WikiPin, key, WikiPinPosition(pins.Count == 0 ? 0 : pins[^1].Position + 1));
			}
			else tx.Delete(Tables.WikiPin, key);
			return true;
		});
	}

	public async Task<bool> SetPinnedCategoryOrderAsync(IReadOnlyList<string> categories)
	{
		return await Store.WriteAsync(tx =>
		{
			var pins = ReadWikiPins(tx);
			var current = pins.Select(pin => pin.Key).ToList();
			var order = WikiPinOrder.Arrange(current, categories);
			if (order.SequenceEqual(current, StringComparer.Ordinal)) return false;
			for (var position = 0; position < order.Count; position++)
				tx.Put(Tables.WikiPin, WikiPinKey(order[position]), WikiPinPosition(position));
			return true;
		});
	}
}
