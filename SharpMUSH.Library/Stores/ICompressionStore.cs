using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// The zstd dictionaries the store compresses feed lines, attribute values and mail with (<c>help @storage</c>). Each
/// kind of value has its own: one that ships with the server, and the ones the world trains on its own values.
/// </summary>
public interface ICompressionStore
{
	/// <summary>
	/// Keeps <paramref name="values"/> small: trains a dictionary on them once there are enough, and a new one when they
	/// have grown well past what the current one saw, then rewrites the values made without the current one. Run by
	/// the upkeep, never on a write. Each training run is logged at <paramref name="at"/>, kept or not.
	/// </summary>
	ValueTask<DictionaryCompaction> CompactAsync(CompressedValues values, DateTimeOffset at,
		CancellationToken cancellationToken = default);

	/// <summary>Every dictionary the world keeps for <paramref name="values"/>, how many values use each, and the log of training runs.</summary>
	ValueTask<StoredDictionaries> GetDictionariesAsync(CompressedValues values, CancellationToken cancellationToken = default);

	/// <summary>
	/// Makes dictionary <paramref name="id"/> the one new <paramref name="values"/> are compressed with and rewrites
	/// every value made with another, returning how many were rewritten; NotFound when the world has no such dictionary.
	/// </summary>
	ValueTask<Found<long>> UseDictionaryAsync(CompressedValues values, int id, CancellationToken cancellationToken = default);
}
