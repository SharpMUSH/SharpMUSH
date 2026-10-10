using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>
/// The numbered arguments of a call or command — <c>%0</c>, <c>%1</c>, … — in numeric order, without the
/// named ones; what <see cref="ParserState.ArgumentsOrdered"/> answers.
/// </summary>
/// <remarks>
/// A key is read as a number, so <c>"1"</c> and <c>"1 "</c> name the same argument, and <c>%10</c> comes
/// after <c>%2</c>. Nearly every binding is exactly <c>"0"</c>…<c>"n-1"</c>; that one is held as the
/// values alone, so building the view and reading <see cref="Values"/> allocate nothing beyond the array.
/// </remarks>
public sealed class OrderedArguments : IReadOnlyDictionary<string, CallState>
{
	public static readonly OrderedArguments Empty = new([], null, null);

	private readonly CallState[] _values;

	/// <summary>Each value's number, ascending; null when they are 0…n-1.</summary>
	private readonly int[]? _numbers;

	/// <summary>Each value's key as bound; null when they are the canonical <c>"0"</c>…<c>"n-1"</c>.</summary>
	private readonly string[]? _keys;

	private OrderedArguments(CallState[] values, int[]? numbers, string[]? keys)
	{
		_values = values;
		_numbers = numbers;
		_keys = keys;
	}

	/// <summary>The numbered entries of <paramref name="arguments"/>, in numeric order.</summary>
	public static OrderedArguments From(Dictionary<string, CallState> arguments)
	{
		if (arguments.Count == 0) return Empty;

		var values = new CallState[arguments.Count];
		for (var position = 0; position < values.Length; position++)
		{
			if (!arguments.TryGetValue(ParserState.ArgumentKey(position), out var value))
				return Sorted(arguments);
			values[position] = value;
		}

		return new OrderedArguments(values, null, null);
	}

	/// <summary>Any other binding: named entries dropped, the rest sorted by number.</summary>
	private static OrderedArguments Sorted(Dictionary<string, CallState> arguments)
	{
		var numbered = arguments
			.Select(pair => (Number: int.TryParse(pair.Key, out var number) ? number : (int?)null, pair.Key, pair.Value))
			.Where(entry => entry.Number is not null)
			.OrderBy(entry => entry.Number)
			.ThenBy(entry => entry.Key, StringComparer.Ordinal)
			.ToArray();

		return numbered.Length == 0
			? Empty
			: new OrderedArguments(
				[.. numbered.Select(entry => entry.Value)],
				[.. numbered.Select(entry => entry.Number!.Value)],
				[.. numbered.Select(entry => entry.Key)]);
	}

	public int Count => _values.Length;

	public bool IsEmpty => _values.Length == 0;

	/// <summary>The values in argument order.</summary>
	public IReadOnlyList<CallState> Values => _values;

	/// <summary>The keys as they were bound, in argument order.</summary>
	public IEnumerable<string> Keys => _keys ?? Enumerable.Range(0, _values.Length).Select(ParserState.ArgumentKey);

	public CallState this[string key]
		=> TryGetValue(key, out var value)
			? value
			: throw new KeyNotFoundException($"There is no argument {key}.");

	public bool ContainsKey(string key) => IndexOf(key) >= 0;

	public bool TryGetValue(string key, [MaybeNullWhen(false)] out CallState value)
	{
		var index = IndexOf(key);
		value = index >= 0 ? _values[index] : null;
		return index >= 0;
	}

	private int IndexOf(string key)
	{
		if (!int.TryParse(key, out var number)) return -1;
		if (_numbers is null) return (uint)number < (uint)_values.Length ? number : -1;
		var index = Array.BinarySearch(_numbers, number);
		return index >= 0 ? index : -1;
	}

	private string KeyAt(int index) => _keys?[index] ?? ParserState.ArgumentKey(index);

	public Enumerator GetEnumerator() => new(this);

	IEnumerator<KeyValuePair<string, CallState>> IEnumerable<KeyValuePair<string, CallState>>.GetEnumerator()
		=> GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	IEnumerable<CallState> IReadOnlyDictionary<string, CallState>.Values => _values;

	/// <summary>Walks the arguments in order without allocating.</summary>
	public struct Enumerator(OrderedArguments arguments) : IEnumerator<KeyValuePair<string, CallState>>
	{
		private int _index = -1;

		public readonly KeyValuePair<string, CallState> Current
			=> new(arguments.KeyAt(_index), arguments._values[_index]);

		readonly object IEnumerator.Current => Current;

		public bool MoveNext() => ++_index < arguments._values.Length;

		public void Reset() => _index = -1;

		public readonly void Dispose() { }
	}
}
