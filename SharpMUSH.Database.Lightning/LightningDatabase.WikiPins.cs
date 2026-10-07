using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// The categories pinned to the wiki home: one row per category key in <see cref="Tables.WikiPin"/>. The row's
/// presence is the pin; its value carries nothing.
/// </summary>
public partial class LightningDatabase
{
	private static readonly byte[] WikiPinValue = [1];

	private static byte[] WikiPinKey(string category) => Keys.Str(category);

	public Task<IReadOnlyList<string>> GetPinnedCategoriesAsync()
		=> Task.FromResult<IReadOnlyList<string>>(Store.Read(tx =>
			tx.Range(Tables.WikiPin, [])
				.Select(entry => Keys.ReadStr(entry.Key))
				.ToList()));

	public async Task<bool> SetCategoryPinnedAsync(string category, bool pinned)
		=> await Store.WriteAsync(tx =>
		{
			var key = WikiPinKey(category);
			if (tx.TryGet(Tables.WikiPin, key, out _) == pinned) return false;
			if (pinned) tx.Put(Tables.WikiPin, key, WikiPinValue);
			else tx.Delete(Tables.WikiPin, key);
			return true;
		});
}
