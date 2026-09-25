using System.Collections.Concurrent;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// A live command or function table. <c>@command</c>, <c>@function</c> and plugin loads change it while
/// other connections are dispatching through it, so it is safe to read and enumerate during a change.
/// The table is held rather than inherited, so every change goes through a member here and moves
/// <see cref="Version"/>.
/// </summary>
public class LibraryService<TKey, TValue> : IReadOnlyDictionary<TKey, (TValue LibraryInformation, bool IsSystem)>
	where TKey : notnull
{
	/// <remarks>
	/// One lock stripe: changes are rare and already serialised by their callers. Reads and
	/// enumeration never lock.
	/// </remarks>
	private readonly ConcurrentDictionary<TKey, (TValue LibraryInformation, bool IsSystem)> _entries;
	private readonly HashSet<TKey> _systemNames;
	private long _version;

	/// <summary>
	/// Bumped after every add, replace and remove. A reader that notes it before enumerating knows the
	/// enumeration may have missed a change if it differs afterwards; the entry count cannot say that,
	/// since a remove and an add, or a replace, leave it where it was.
	/// </summary>
	public long Version => Interlocked.Read(ref _version);

	private void Changed() => Interlocked.Increment(ref _version);

	public LibraryService() : this(null)
	{
	}

	protected LibraryService(IEqualityComparer<TKey>? comparer)
	{
		_entries = new ConcurrentDictionary<TKey, (TValue LibraryInformation, bool IsSystem)>(concurrencyLevel: 1, capacity: 31, comparer);
		_systemNames = new HashSet<TKey>(_entries.Comparer);
	}

	public IEqualityComparer<TKey> Comparer => _entries.Comparer;

	public int Count => _entries.Count;

	public IEnumerable<TKey> Keys => _entries.Keys;

	public IEnumerable<(TValue LibraryInformation, bool IsSystem)> Values => _entries.Values;

	public bool ContainsKey(TKey key) => _entries.ContainsKey(key);

	public bool TryGetValue(TKey key, out (TValue LibraryInformation, bool IsSystem) value) => _entries.TryGetValue(key, out value);

	/// <summary>Looks an entry up by an alternate key the comparer understands, e.g. a span of a string key.</summary>
	public bool TryGetAlternateValue<TAlternate>(TAlternate key, out (TValue LibraryInformation, bool IsSystem) value)
		where TAlternate : notnull, allows ref struct
		=> _entries.GetAlternateLookup<TAlternate>().TryGetValue(key, out value);

	public IEnumerator<KeyValuePair<TKey, (TValue LibraryInformation, bool IsSystem)>> GetEnumerator() => _entries.GetEnumerator();

	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

	public (TValue LibraryInformation, bool IsSystem) this[TKey key]
	{
		get => _entries[key];
		set
		{
			_entries[key] = value;
			Changed();
		}
	}

	public bool TryAdd(TKey key, (TValue LibraryInformation, bool IsSystem) value)
	{
		if (!_entries.TryAdd(key, value)) return false;
		Changed();
		return true;
	}

	public bool TryRemove(TKey key, out (TValue LibraryInformation, bool IsSystem) value)
	{
		if (!_entries.TryRemove(key, out value)) return false;
		Changed();
		return true;
	}

	/// <summary>Removes exactly <paramref name="entry"/>: nothing if the name now holds something else.</summary>
	public bool TryRemove(KeyValuePair<TKey, (TValue LibraryInformation, bool IsSystem)> entry)
	{
		if (!_entries.TryRemove(entry)) return false;
		Changed();
		return true;
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