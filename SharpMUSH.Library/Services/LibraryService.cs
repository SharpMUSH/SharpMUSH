using System.Collections.Concurrent;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// A live command or function table. <c>@command</c>, <c>@function</c> and plugin loads change it while
/// other connections are dispatching through it, so it is safe to read and enumerate during a change.
/// </summary>
public class LibraryService<TKey, TValue> : ConcurrentDictionary<TKey, (TValue LibraryInformation, bool IsSystem)>
	where TKey : notnull
{
	private readonly HashSet<TKey> _systemNames;

	public LibraryService() : this(null)
	{
	}

	/// <remarks>
	/// One lock stripe: changes are rare and already serialised by their callers, and
	/// <see cref="ConcurrentDictionary{TKey,TValue}.Count"/>, which the command trie reads on every
	/// lookup, takes every stripe. Reads and enumeration never lock.
	/// </remarks>
	protected LibraryService(IEqualityComparer<TKey>? comparer) : base(concurrencyLevel: 1, capacity: 31, comparer)
	{
		_systemNames = new HashSet<TKey>(Comparer);
	}

	/// <summary>Adds an entry, throwing if the name is taken, as <see cref="Dictionary{TKey,TValue}.Add"/> does.</summary>
	public void Add(TKey key, (TValue LibraryInformation, bool IsSystem) value)
	{
		if (!TryAdd(key, value))
		{
			throw new ArgumentException($"An entry named '{key}' already exists.", nameof(key));
		}
	}

	public bool Remove(TKey key) => TryRemove(key, out _);

	public bool Remove(TKey key, out (TValue LibraryInformation, bool IsSystem) value) => TryRemove(key, out value);

	/// <summary>Reserve a compiled contribution's name for this library's lifetime, including softcode deletion.</summary>
	public void ReserveSystemName(TKey name) { lock (_systemNames) _systemNames.Add(name); }

	/// <summary>Whether a retained compiled contribution or a live system entry owns this name.</summary>
	public bool IsSystemNameReserved(TKey name)
	{
		lock (_systemNames)
			if (_systemNames.Contains(name)) return true;
		return TryGetValue(name, out var entry) && entry.IsSystem;
	}

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