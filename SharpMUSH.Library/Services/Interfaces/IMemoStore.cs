using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>A kept <c>memo()</c> answer and the attribute reads it depended on.</summary>
/// <param name="Value">The answer.</param>
/// <param name="Reads">The reads to repeat before using it: it is good while each finds what it found then.</param>
public sealed record MemoEntry(MString Value, IReadOnlyList<AttributeReadDependency> Reads);

/// <summary>
/// The answers <c>memo()</c> keeps, in memory, for one game. Bounded by <c>memo_max_bytes</c> and <c>memo_max_entries</c>
/// (least recently used dropped first) and <c>memo_window</c>; checking an entry's reads is the caller's part.
/// </summary>
public interface IMemoStore
{
	/// <summary>The entry kept under <paramref name="key"/>, unless there is none or its window has passed.</summary>
	Found<MemoEntry> Find(string key);

	/// <summary>Keeps <paramref name="entry"/> under <paramref name="key"/>, when it fits in the budget.</summary>
	void Keep(string key, MemoEntry entry);

	/// <summary>Drops the entry under <paramref name="key"/>, whose reads no longer hold.</summary>
	void Forget(string key);

	/// <summary>How many entries are kept, and the bytes they are estimated to take.</summary>
	(int Count, long Bytes) Usage { get; }
}
