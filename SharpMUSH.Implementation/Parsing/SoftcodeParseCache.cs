using System.Collections.Concurrent;
using SharpMUSH.Configuration.Options;

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
/// Bounded by the total length of the text it holds rather than by entry count, since a tree's size
/// follows its text. When full it starts again empty; dropping the trees costs only reparsing them.
/// </para>
/// </remarks>
public sealed class SoftcodeParseCache
{
	/// <summary>
	/// The longest text kept. Longer text is parsed each time, and not looked up: hashing it would cost
	/// the time it was looked up to save.
	/// </summary>
	public const int MaxTextLength = 4096;

	/// <summary>The total length of text held before the cache starts again.</summary>
	public const int CapacityInCharacters = 1 << 20;

	public readonly record struct Key(
		string Text,
		string EntryPoint,
		bool Lenient,
		bool ParenGroups,
		ParserPredictionMode PredictionMode);

	public sealed record Entry(object Context, ParserErrorListener Errors);

	private readonly ConcurrentDictionary<Key, Entry> _entries = new();

	/// <summary>
	/// The length of text <see cref="_entries"/> holds, kept by hand because a count over the
	/// dictionary locks every bucket. Races can leave it a little off; it only decides when to start
	/// again.
	/// </summary>
	private long _characters;

	public bool TryGet(in Key key, out Entry entry) => _entries.TryGetValue(key, out entry!);

	public void Add(in Key key, Entry entry)
	{
		if (key.Text.Length > MaxTextLength) return;

		if (Interlocked.Read(ref _characters) + key.Text.Length > CapacityInCharacters)
		{
			_entries.Clear();
			Interlocked.Exchange(ref _characters, 0);
		}

		if (_entries.TryAdd(key, entry)) Interlocked.Add(ref _characters, key.Text.Length);
	}
}
