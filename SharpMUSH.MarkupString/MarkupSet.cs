using System.Collections;
using System.Collections.Concurrent;
namespace MarkupString;

/// <summary>
/// Immutable ordered list of <see cref="IMarkup"/>, innermost first. Value-equal. Interned via
/// <see cref="Of(IMarkup)"/> and friends so equal sets share one instance (a bounded
/// lock-free intern table; interning is an optimisation only, equality never depends on it).
/// </summary>
public sealed class MarkupSet : IEquatable<MarkupSet>, IReadOnlyList<IMarkup>
{
	private const int InternCapacity = 4096;
	private static readonly ConcurrentDictionary<MarkupSet, MarkupSet> Intern = new();
	private readonly IMarkup[] _items;
	private readonly int _hash;

	private MarkupSet(IMarkup[] items)
	{
		_items = items;
		var h = new HashCode();
		foreach (var m in items) h.Add(m);
		_hash = h.ToHashCode();
	}

	public static MarkupSet Of(IMarkup markup) => Canonical(new MarkupSet([markup]));
	public static MarkupSet Of(ReadOnlySpan<IMarkup> markups) => Canonical(new MarkupSet(markups.ToArray()));
	public static MarkupSet Of(IEnumerable<IMarkup> markups) => Canonical(new MarkupSet(markups.ToArray()));

	/// <summary>Returns a new set with <paramref name="outer"/> added as the new outermost layer.</summary>
	public MarkupSet Append(IMarkup outer)
	{
		var items = new IMarkup[_items.Length + 1];
		_items.CopyTo(items, 0);
		items[^1] = outer;
		return Canonical(new MarkupSet(items));
	}

	private static MarkupSet Canonical(MarkupSet candidate)
	{
		if (candidate._items.Length == 0) throw new ArgumentException("A MarkupSet must contain at least one markup.");
		if (Intern.TryGetValue(candidate, out var existing)) return existing;
		if (Intern.Count >= InternCapacity) Intern.Clear();
		return Intern.GetOrAdd(candidate, candidate);
	}

	public int Count => _items.Length;
	public IMarkup this[int index] => _items[index];
	public IMarkup Innermost => _items[0];
	public IMarkup Outermost => _items[^1];
	public IEnumerator<IMarkup> GetEnumerator() => ((IEnumerable<IMarkup>)_items).GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public bool Equals(MarkupSet? other)
	{
		if (other is null || other._items.Length != _items.Length || other._hash != _hash) return false;
		for (var i = 0; i < _items.Length; i++) if (!_items[i].Equals(other._items[i])) return false;
		return true;
	}

	public override bool Equals(object? obj) => obj is MarkupSet s && Equals(s);
	public override int GetHashCode() => _hash;
}
