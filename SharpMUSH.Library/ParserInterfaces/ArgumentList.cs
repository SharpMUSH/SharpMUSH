using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>
/// The arguments of a call or command: <c>%0</c>, <c>%1</c>, … and any named ones, what
/// <see cref="ParserState.ArgumentList"/> holds. It reads exactly as the dictionary
/// <see cref="ParserState.Arguments"/> does, key for key, and adds lookups by position.
/// </summary>
/// <remarks>
/// Nearly every call binds exactly <c>"0"</c>…<c>"n-1"</c>; such a list holds the values alone and builds the
/// dictionary only when something asks for <see cref="AsDictionary"/>. A list made from a dictionary
/// (<see cref="From"/>) reads through that dictionary. A list does not change once made: writing into the
/// dictionary <see cref="AsDictionary"/> returns does not change what the list's other members read.
/// </remarks>
public sealed class ArgumentList : IReadOnlyDictionary<string, CallState>
{
	/// <summary>
	/// A list with no arguments, shared. Its <see cref="AsDictionary"/> is a new dictionary each time, so one
	/// state writing into it cannot reach another; a state that starts an evaluation takes <see cref="NewEmpty"/>.
	/// </summary>
	public static readonly ArgumentList Empty = new(Array.Empty<CallState>());

	/// <summary>A list with no arguments, of its own: its dictionary is built once and kept, like any other list's.</summary>
	public static ArgumentList NewEmpty() => new(Array.Empty<CallState>());

	/// <summary>The values of <c>"0"</c>…<c>"n-1"</c>; null when the list reads through a dictionary.</summary>
	private readonly CallState[]? _values;

	/// <summary>The dictionary form: given, or built on first request.</summary>
	private Dictionary<string, CallState>? _dictionary;

	private OrderedArguments? _ordered;

	private ArgumentList(CallState[] values) => _values = values;

	private ArgumentList(Dictionary<string, CallState> dictionary) => _dictionary = dictionary;

	/// <summary>The values as <c>%0</c>, <c>%1</c>, …, in order. The list keeps the array: do not change it afterwards.</summary>
	public static ArgumentList Of(CallState[] values) => values.Length == 0 ? Empty : new ArgumentList(values);

	/// <summary>The values as <c>%0</c>, <c>%1</c>, …, in order.</summary>
	public static ArgumentList Of(IReadOnlyList<CallState> values)
	{
		if (values.Count == 0) return Empty;
		var array = new CallState[values.Count];
		for (var position = 0; position < array.Length; position++) array[position] = values[position];
		return new ArgumentList(array);
	}

	/// <summary>One value, as <c>%0</c>.</summary>
	public static ArgumentList Of(CallState value) => new([value]);

	/// <summary>Two values, as <c>%0</c> and <c>%1</c>.</summary>
	public static ArgumentList Of(CallState first, CallState second) => new([first, second]);

	/// <summary>
	/// A list that reads through <paramref name="dictionary"/>; <see cref="AsDictionary"/> returns that same
	/// instance.
	/// </summary>
	public static ArgumentList From(Dictionary<string, CallState> dictionary) => new(dictionary);

	/// <summary>
	/// Whether this list holds only <c>"0"</c>…<c>"n-1"</c> as values alone. A list made from a dictionary
	/// answers false even when its keys are those.
	/// </summary>
	public bool IsPositional => _values is not null;

	/// <summary>How many arguments, named ones included: what the dictionary's <c>Count</c> answers.</summary>
	public int Count => _values?.Length ?? _dictionary!.Count;

	/// <summary>The argument bound to <c>%<paramref name="position"/></c>.</summary>
	/// <exception cref="KeyNotFoundException">There is none.</exception>
	public CallState this[int position]
		=> TryGet(position, out var value)
			? value
			: throw new KeyNotFoundException($"There is no argument {position}.");

	/// <summary>The argument bound under <paramref name="key"/>.</summary>
	/// <exception cref="KeyNotFoundException">There is none.</exception>
	public CallState this[string key]
		=> TryGetValue(key, out var value)
			? value
			: throw new KeyNotFoundException($"There is no argument {key}.");

	/// <summary>The argument bound to <c>%<paramref name="position"/></c>, if there is one.</summary>
	public bool TryGet(int position, [MaybeNullWhen(false)] out CallState value)
	{
		if (_values is not null)
		{
			var found = (uint)position < (uint)_values.Length;
			value = found ? _values[position] : null;
			return found;
		}

		return _dictionary!.TryGetValue(ParserState.ArgumentKey(position), out value);
	}

	/// <summary>The argument bound to <c>%<paramref name="position"/></c>, or null.</summary>
	public CallState? GetValueOrDefault(int position)
		=> TryGet(position, out var value) ? value : null;

	/// <summary>The argument bound to <c>%<paramref name="position"/></c>, or <paramref name="defaultValue"/>.</summary>
	public CallState GetValueOrDefault(int position, CallState defaultValue)
		=> TryGet(position, out var value) ? value : defaultValue;

	/// <summary>Whether an argument is bound to <c>%<paramref name="position"/></c>.</summary>
	public bool Contains(int position) => TryGet(position, out _);

	/// <inheritdoc/>
	public bool TryGetValue(string key, [MaybeNullWhen(false)] out CallState value)
	{
		if (_values is null) return _dictionary!.TryGetValue(key, out value);

		var position = CanonicalPosition(key);
		var found = (uint)position < (uint)_values.Length;
		value = found ? _values[position] : null;
		return found;
	}

	/// <inheritdoc/>
	public bool ContainsKey(string key) => TryGetValue(key, out _);

	/// <summary>
	/// The number <paramref name="key"/> spells as <see cref="ParserState.ArgumentKey"/> would write it —
	/// digits, no sign, no leading zero — or -1, so a lookup matches the keys a dictionary would hold.
	/// </summary>
	private static int CanonicalPosition(string key)
	{
		if (key.Length is 0 or > 9 || (key.Length > 1 && key[0] == '0')) return -1;
		var position = 0;
		foreach (var character in key)
		{
			if (!char.IsAsciiDigit(character)) return -1;
			position = position * 10 + (character - '0');
		}

		return position;
	}

	/// <summary>
	/// The arguments as the dictionary <see cref="ParserState.Arguments"/> answers: the one this list was made
	/// from, or one built the first time it is asked for and kept.
	/// </summary>
	public Dictionary<string, CallState> AsDictionary()
	{
		if (_dictionary is not null) return _dictionary;
		if (ReferenceEquals(this, Empty)) return [];

		var values = _values!;
		var dictionary = new Dictionary<string, CallState>(values.Length);
		for (var position = 0; position < values.Length; position++)
		{
			dictionary[ParserState.ArgumentKey(position)] = values[position];
		}

		// Two threads may race to build it: the first copy published is the one every reader gets.
		return Interlocked.CompareExchange(ref _dictionary, dictionary, null) ?? dictionary;
	}

	/// <summary>The numbered arguments in numeric order, named ones left out: <see cref="ParserState.ArgumentsOrdered"/>.</summary>
	public OrderedArguments Ordered
		=> _ordered ??= _values is not null ? OrderedArguments.Of(_values) : OrderedArguments.From(_dictionary!);

	/// <summary>A copy holding each value as <paramref name="map"/> turns it.</summary>
	public ArgumentList Select(Func<CallState, CallState> map)
	{
		if (_values is null)
		{
			return new ArgumentList(_dictionary!.ToDictionary(pair => pair.Key, pair => map(pair.Value), _dictionary!.Comparer));
		}

		if (_values.Length == 0) return Empty;
		var mapped = new CallState[_values.Length];
		for (var position = 0; position < mapped.Length; position++) mapped[position] = map(_values[position]);
		return new ArgumentList(mapped);
	}

	/// <summary>The keys in the order they were bound.</summary>
	public IEnumerable<string> Keys
		=> _values is not null ? Enumerable.Range(0, _values.Length).Select(ParserState.ArgumentKey) : _dictionary!.Keys;

	/// <summary>The values in the order they were bound.</summary>
	public IEnumerable<CallState> Values => _values ?? (IEnumerable<CallState>)_dictionary!.Values;

	/// <summary>Walks the arguments in the order they were bound.</summary>
	public IEnumerator<KeyValuePair<string, CallState>> GetEnumerator()
	{
		if (_values is null)
		{
			foreach (var pair in _dictionary!) yield return pair;
			yield break;
		}

		for (var position = 0; position < _values.Length; position++)
		{
			yield return new KeyValuePair<string, CallState>(ParserState.ArgumentKey(position), _values[position]);
		}
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
