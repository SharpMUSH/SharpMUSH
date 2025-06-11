namespace SharpMUSH.Library.Services;

public class LibraryService<TKey, TValue> : Dictionary<TKey, (TValue LibraryInformation, bool IsSystem)>
	where TKey : notnull
{
	public static LibraryService<TKey, TValue> FromDictionary(Dictionary<TKey, TValue> dictionary)
	{
		var newDict = new LibraryService<TKey, TValue>();

		foreach (var kvp in dictionary)
		{
			newDict.Add(kvp.Key, (kvp.Value, true));
		}

		return newDict;
	}
}