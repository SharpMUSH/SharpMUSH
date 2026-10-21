using System.Buffers.Binary;

namespace SharpMUSH.Database.Lightning.Semantics;

/// <summary>Whether a staged rebuild is still journaling, or its journal filled and it must be abandoned.</summary>
internal enum SemanticRebuildStagingState : byte { Staging = 1, Overflowed = 2 }

/// <summary>A staged rebuild as its record in the shared metadata table holds it.</summary>
internal sealed record SemanticRebuildStaging(ulong Generation, SemanticRebuildStagingState State, ulong Capacity);

/// <summary>
/// The after-image journal of a staged rebuild. Every projection write while a generation is staged appends
/// (commit sequence, subject) in its own job; replay re-copies that subject's current state, which is at least
/// that write's after-image. A full journal never refuses a write: it marks the staging overflowed, and the
/// rebuild abandons it.
/// </summary>
internal sealed class SemanticRebuildJournal(SemanticRebuildStaging staging)
{
	/// <summary>The default journal capacity in entries: 16 bytes each, well inside the spec's 64 MiB allowance.</summary>
	internal const ulong DefaultCapacity = 1UL << 20;
	internal const byte StagingKey = 40, CountKey = 41, CheckpointKey = 42, ConsumedKey = 43;

	internal static byte[] Encode(SemanticRebuildStaging staging)
	{
		var bytes = new byte[17];
		BinaryPrimitives.WriteUInt64BigEndian(bytes, staging.Generation);
		bytes[8] = (byte)staging.State;
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(9), staging.Capacity);
		return bytes;
	}

	internal static SemanticRebuildStaging? Decode(byte[]? bytes)
	{
		if (bytes is null) return null;
		if (bytes.Length != 17 || bytes[8] is not ((byte)SemanticRebuildStagingState.Staging or (byte)SemanticRebuildStagingState.Overflowed))
			throw new InvalidDataException("Malformed rebuild staging record.");
		var generation = BinaryPrimitives.ReadUInt64BigEndian(bytes);
		var capacity = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(9));
		if (generation < 2 || capacity == 0) throw new InvalidDataException("Malformed rebuild staging record.");
		return new(generation, (SemanticRebuildStagingState)bytes[8], capacity);
	}

	internal static SemanticRebuildStaging? Read(WikiSemanticWork work) => Decode(work.ReadBytes(work.Tables.Metadata, work.Key(StagingKey)));

	/// <summary>The journal a write must append to; null when no rebuild is staged or its journal already overflowed.</summary>
	internal static SemanticRebuildJournal? Open(WikiSemanticWork work)
		=> Read(work) is { State: SemanticRebuildStagingState.Staging } staging ? new(staging) : null;

	internal void Append(WikiSemanticWork work, long sequence, ulong subject)
	{
		var count = work.Counter(work.Key(CountKey));
		if (count >= staging.Capacity)
		{
			work.Delta.Put(work.Tables.Metadata, work.Key(StagingKey), work.Bytes(17, () => Encode(staging with { State = SemanticRebuildStagingState.Overflowed })));
			return;
		}
		work.Delta.Put(AtomicWikiLayout.Journal, work.Number(checked((ulong)sequence)), work.Number(subject));
		work.Delta.Put(work.Tables.Metadata, work.Key(CountKey), work.Number(count + 1));
	}
}
