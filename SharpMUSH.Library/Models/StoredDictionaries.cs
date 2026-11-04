namespace SharpMUSH.Library.Models;

/// <summary>What the store compresses with zstd dictionaries; each has dictionaries and a training log of its own.</summary>
public enum CompressedValues
{
	FeedLines,
	Attributes,
	Mail
}

/// <summary>What a pass of <see cref="ICompressionStore.CompactAsync"/> did.</summary>
/// <param name="Trained">Whether it trained a new dictionary.</param>
/// <param name="Rewritten">How many values it rewrote with the current dictionary.</param>
public sealed record DictionaryCompaction(bool Trained, long Rewritten);

/// <summary>One kind's dictionaries and its training runs, oldest first. See <see cref="ICompressionStore.GetDictionariesAsync"/>.</summary>
/// <param name="Generations">Every dictionary the world has kept, the bundled one first.</param>
/// <param name="Runs">Every training run, kept or not.</param>
/// <param name="Uncompressed">Values stored as they are: short, written before compression, or not smaller compressed.</param>
public sealed record StoredDictionaries(IReadOnlyList<DictionaryGeneration> Generations, IReadOnlyList<DictionaryRun> Runs,
	long Uncompressed);

/// <summary>One dictionary.</summary>
/// <param name="Id">Its id; 0 is the one that ships with the server.</param>
/// <param name="TrainedAt">When the world trained it; null for the bundled one.</param>
/// <param name="Bytes">Its size.</param>
/// <param name="Values">How many stored values it compresses.</param>
/// <param name="Current">Whether new values are compressed with it.</param>
public sealed record DictionaryGeneration(int Id, DateTimeOffset? TrainedAt, int Bytes, long Values, bool Current);

/// <summary>One training run.</summary>
/// <param name="At">When it ran.</param>
/// <param name="Rows">The values the world held.</param>
/// <param name="SampleRows">The values it read, those it trained on and those held out to judge it on.</param>
/// <param name="HeldOutRows">The values held out.</param>
/// <param name="CurrentBytes">The held-out values' size with the dictionary in use then.</param>
/// <param name="CandidateBytes">Their size with the new dictionary; -1 when zstd could not train one.</param>
/// <param name="From">The dictionary in use then.</param>
/// <param name="Kept">The id the new dictionary was kept under, or null when it was not kept.</param>
public sealed record DictionaryRun(DateTimeOffset At, long Rows, int SampleRows, int HeldOutRows, long CurrentBytes,
	long CandidateBytes, int From, int? Kept);
