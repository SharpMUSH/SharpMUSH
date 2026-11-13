using System.Collections.Concurrent;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Parsing;

/// <summary>
/// Parse trees of softcode already parsed, so code run over and over — an attribute called through
/// <c>u()</c>, <c>filter()</c> or <c>iter()</c> once per item, a <c>$</c>-command's action list — is lexed
/// and parsed once rather than on every call. Lexing and parsing were about a third of what a
/// <c>filter()</c> over a room's occupants cost.
/// </summary>
/// <remarks>
/// A tree depends only on the text and the settings that change how it parses, which make up the key;
/// the markup of the text being evaluated is handed to the visitor separately. Trees are only read
/// once built: the visitor walks them and reads token text from the input they hold, so one tree can
/// be visited by any number of evaluations at once.
/// <para>
/// Stored attribute text (<see cref="AttributeBody"/>) longer than <see cref="MaxTextLength"/> is keyed instead on the
/// attribute and its modification time, which every write of it moves forward, so it is found without hashing it. That key
/// alone is not proof: an attribute deleted and set again, or an object's dbref reused, can bring back a time
/// another value had. A hit is therefore checked against the text it was parsed from, an ordinal compare that
/// costs far less than hashing (it stops at the first difference, and is vectorised when the two agree).
/// </para>
/// <para>
/// Bounded by the total length of the text it holds rather than by entry count, since a tree's size
/// follows its text. When full it starts again empty; dropping the trees costs only reparsing them.
/// </para>
/// </remarks>
public sealed class SoftcodeParseCache
{
	/// <summary>
	/// The longest text kept under its text. Longer text is not looked up by it: hashing it would cost the time it
	/// was looked up to save. Longer stored attribute text is kept under its <see cref="SourceKey"/>.
	/// </summary>
	public const int MaxTextLength = 4096;

	/// <summary>
	/// The longest stored attribute text kept, keyed on its <see cref="AttributeBody"/>: the most an attribute
	/// value holds.
	/// </summary>
	public const int MaxSourcedTextLength = 1 << 16;

	/// <summary>The total length of text held before the cache starts again.</summary>
	public const int CapacityInCharacters = 1 << 20;

	public readonly record struct Key(
		string Text,
		string EntryPoint,
		bool Lenient,
		bool ParenGroups,
		ParserPredictionMode PredictionMode);

	/// <summary>The key of stored attribute text: which version of which attribute, parsed how.</summary>
	public readonly record struct SourceKey(
		DBRef Object,
		string Attribute,
		long Modified,
		string EntryPoint,
		bool Lenient,
		bool ParenGroups,
		ParserPredictionMode PredictionMode);

	public sealed record Entry(object Context, ParserErrorListener Errors);

	/// <summary>A tree of stored attribute text, with the text, which a hit must match.</summary>
	private sealed record SourcedEntry(string Text, Entry Entry);

	private readonly ConcurrentDictionary<Key, Entry> _entries = new();
	private readonly ConcurrentDictionary<SourceKey, SourcedEntry> _sourced = new();

	/// <summary>
	/// The length of text <see cref="_entries"/> holds, kept by hand because a count over the
	/// dictionary locks every bucket. Races can leave it a little off; it only decides when to start
	/// again.
	/// </summary>
	private long _characters;

	public bool TryGet(in Key key, out Entry entry) => _entries.TryGetValue(key, out entry!);

	/// <summary>The tree of <paramref name="text"/>, stored under <paramref name="key"/> from that same text.</summary>
	public bool TryGet(in SourceKey key, string text, out Entry entry)
	{
		if (_sourced.TryGetValue(key, out var sourced) && string.Equals(sourced.Text, text, StringComparison.Ordinal))
		{
			entry = sourced.Entry;
			return true;
		}

		entry = null!;
		return false;
	}

	public void Add(in Key key, Entry entry)
	{
		if (key.Text.Length > MaxTextLength) return;

		MakeRoom(key.Text.Length);
		if (_entries.TryAdd(key, entry)) Interlocked.Add(ref _characters, key.Text.Length);
	}

	/// <summary>
	/// Keeps the tree of <paramref name="text"/> under <paramref name="key"/>, replacing a tree of other text that
	/// had the key.
	/// </summary>
	public void Add(in SourceKey key, string text, Entry entry)
	{
		if (text.Length > MaxSourcedTextLength) return;

		MakeRoom(text.Length);
		var sourced = new SourcedEntry(text, entry);
		var replaced = 0;
		_sourced.AddOrUpdate(key, sourced, (_, previous) =>
		{
			replaced = previous.Text.Length;
			return sourced;
		});
		Interlocked.Add(ref _characters, text.Length - replaced);
	}

	/// <summary>Starts again empty when <paramref name="length"/> more characters would pass the capacity.</summary>
	private void MakeRoom(int length)
	{
		if (Interlocked.Read(ref _characters) + length <= CapacityInCharacters) return;

		_entries.Clear();
		_sourced.Clear();
		Interlocked.Exchange(ref _characters, 0);
	}
}
