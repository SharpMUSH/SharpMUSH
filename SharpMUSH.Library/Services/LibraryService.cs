using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Services;

public class LibraryService<TKey, TValue> : Dictionary<TKey, (TValue LibraryInformation, bool IsSystem)>
	where TKey : notnull
{
	private readonly HashSet<TKey> _systemNames;

	public LibraryService() : this(null)
	{
	}

	protected LibraryService(IEqualityComparer<TKey>? comparer) : base(comparer)
	{
		_systemNames = new HashSet<TKey>(Comparer);
	}

	/// <summary>Reserve a compiled contribution's name for this library's lifetime, including softcode deletion.</summary>
	public void ReserveSystemName(TKey name) { lock (_systemNames) _systemNames.Add(name); }

	/// <summary>Whether a compiled contribution registered this name, independent of mutable lookup entries.</summary>
	public bool IsSystemNameReserved(TKey name) { lock (_systemNames) return _systemNames.Contains(name); }

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

public class FunctionLibraryService :
	LibraryService<string, FunctionDefinition>
{
	public FunctionLibraryService() : base(StringComparer.OrdinalIgnoreCase)
	{
	}
}


public class CommandLibraryService :
	LibraryService<string, CommandDefinition>
{
	public CommandLibraryService() : base(StringComparer.OrdinalIgnoreCase)
	{
	}
}