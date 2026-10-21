using System.Buffers.Binary;

namespace SharpMUSH.Database.Lightning.Migration;

internal static class WikiCategoryCheckpointCodec
{
	internal const int FixedBytes = 112;
	internal const int MaxBytes = 131072;
	internal const string MigrationId = "0004_wiki_category_value_v1";
	private const string InvalidCheckpointMessage = "Invalid wiki category migration checkpoint.";
	private const int CopyChunkBytes = 4096;

	internal static ReadOnlySpan<byte> StateKey => "core.wiki.cat.value-upgrade/state"u8;
	internal static ReadOnlySpan<byte> CompletionKey => "mig:0004_wiki_category_value_v1"u8;

	internal static WikiCategoryMigrationCheckpoint Decode(ReadOnlySpan<byte> bytes, int maxKeySize,
		CancellationToken token = default)
	{
		token.ThrowIfCancellationRequested();
		ValidateEnvironmentMaxKeySize(maxKeySize);
		if (bytes.Length < FixedBytes || bytes.Length > MaxBytes
			|| !bytes[..4].SequenceEqual("WCM1"u8)
			|| BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]) != 1
			|| BinaryPrimitives.ReadUInt16BigEndian(bytes[6..]) != 1
			|| (bytes[9] & ~7) != 0 || bytes[10] != 0 || bytes[11] != 0)
			throw InvalidCheckpoint();

		var rawClearLength = BinaryPrimitives.ReadUInt32BigEndian(bytes[44..]);
		if (rawClearLength > (uint)maxKeySize || rawClearLength > MaxBytes - FixedBytes)
			throw InvalidCheckpoint();
		var clearLength = checked((int)rawClearLength);
		if (bytes.Length != checked(FixedBytes + clearLength))
			throw InvalidCheckpoint();

		var flags = bytes[9];
		var hasClear = (flags & 1) != 0;
		var hasCompleted = (flags & 2) != 0;
		var hasCurrent = (flags & 4) != 0;
		if (hasClear != (clearLength != 0))
			throw InvalidCheckpoint();
		var completedSlot = bytes.Slice(checked(48 + clearLength), 8);
		var currentSlot = bytes.Slice(checked(56 + clearLength), 8);
		var hashSlot = bytes.Slice(checked(64 + clearLength), 32);
		// Verify stored absence before turning raw slots into empty borrowed spans.
		if (!hasCompleted) RequireZero(completedSlot, token);
		if (!hasCurrent)
		{
			RequireZero(currentSlot, token);
			RequireZero(hashSlot, token);
		}

		var checkpoint = new WikiCategoryMigrationCheckpoint(
			(WikiCategoryMigrationPhase)bytes[8],
			BinaryPrimitives.ReadUInt64BigEndian(bytes[12..]),
			BinaryPrimitives.ReadUInt64BigEndian(bytes[20..]),
			BinaryPrimitives.ReadUInt64BigEndian(bytes[28..]),
			BinaryPrimitives.ReadUInt64BigEndian(bytes[36..]),
			bytes.Slice(48, clearLength),
			hasCompleted ? completedSlot : ReadOnlySpan<byte>.Empty,
			hasCurrent ? currentSlot : ReadOnlySpan<byte>.Empty,
			hasCurrent ? hashSlot : ReadOnlySpan<byte>.Empty,
			BinaryPrimitives.ReadUInt64BigEndian(bytes[checked(96 + clearLength)..]),
			BinaryPrimitives.ReadInt64BigEndian(bytes[checked(104 + clearLength)..]));
		ValidateShape(checkpoint, maxKeySize);
		token.ThrowIfCancellationRequested();
		return checkpoint;
	}

	internal static byte[] Encode(WikiCategoryMigrationCheckpoint checkpoint, int maxKeySize,
		CancellationToken token = default)
	{
		token.ThrowIfCancellationRequested();
		var length = EncodedLength(checkpoint, maxKeySize);
		token.ThrowIfCancellationRequested();
		var bytes = new byte[length];
		var span = bytes.AsSpan();
		"WCM1"u8.CopyTo(span);
		BinaryPrimitives.WriteUInt16BigEndian(span[4..], 1);
		BinaryPrimitives.WriteUInt16BigEndian(span[6..], 1);
		span[8] = (byte)checkpoint.Phase;
		span[9] = (byte)((checkpoint.LastClearedKey.IsEmpty ? 0 : 1)
			| (checkpoint.LastCompletedSourceKey.IsEmpty ? 0 : 2)
			| (checkpoint.CurrentSourceKey.IsEmpty ? 0 : 4));
		BinaryPrimitives.WriteUInt64BigEndian(span[12..], checkpoint.Revision);
		BinaryPrimitives.WriteUInt64BigEndian(span[20..], checkpoint.ClearedRows);
		BinaryPrimitives.WriteUInt64BigEndian(span[28..], checkpoint.CompletedPages);
		BinaryPrimitives.WriteUInt64BigEndian(span[36..], checkpoint.CategoryPuts);
		var clearLength = checkpoint.LastClearedKey.Length;
		BinaryPrimitives.WriteUInt32BigEndian(span[44..], checked((uint)clearLength));
		CopyWithCancellation(checkpoint.LastClearedKey, span[48..], token);
		checkpoint.LastCompletedSourceKey.CopyTo(span[checked(48 + clearLength)..]);
		checkpoint.CurrentSourceKey.CopyTo(span[checked(56 + clearLength)..]);
		checkpoint.CurrentSourceHash.CopyTo(span[checked(64 + clearLength)..]);
		BinaryPrimitives.WriteUInt64BigEndian(span[checked(96 + clearLength)..], checkpoint.NextCategoryOrdinal);
		BinaryPrimitives.WriteInt64BigEndian(span[checked(104 + clearLength)..], checkpoint.CompletionUnixMs);
		token.ThrowIfCancellationRequested();
		return bytes;
	}

	internal static int EncodedLength(WikiCategoryMigrationCheckpoint checkpoint, int maxKeySize)
	{
		ValidateEnvironmentMaxKeySize(maxKeySize);
		ValidateShape(checkpoint, maxKeySize);
		return checked(FixedBytes + checkpoint.LastClearedKey.Length);
	}

	internal static void ValidateEnvironmentMaxKeySize(int maxKeySize)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxKeySize);
		var requiredBytes = checked((long)FixedBytes + maxKeySize);
		if (requiredBytes > MaxBytes)
			throw new NotSupportedException(FormattableString.Invariant(
				$"The native maximum key size requires a {requiredBytes}-byte checkpoint envelope; the hard envelope limit is {MaxBytes} bytes."));
	}

	private static void ValidateShape(WikiCategoryMigrationCheckpoint checkpoint, int maxKeySize)
	{
		if (checkpoint.Revision == 0 || checkpoint.LastClearedKey.Length > maxKeySize
			|| checkpoint.LastClearedKey.Length > MaxBytes - FixedBytes
			|| checkpoint.LastCompletedSourceKey.Length is not (0 or 8)
			|| checkpoint.CurrentSourceKey.Length is not (0 or 8))
			throw InvalidCheckpoint();

		var hasClear = !checkpoint.LastClearedKey.IsEmpty;
		var hasCompleted = !checkpoint.LastCompletedSourceKey.IsEmpty;
		var hasCurrent = !checkpoint.CurrentSourceKey.IsEmpty;
		switch (checkpoint.Phase)
		{
			case WikiCategoryMigrationPhase.Clearing:
				if (hasClear != (checkpoint.ClearedRows > 0) || hasCompleted || hasCurrent
					|| !checkpoint.CurrentSourceHash.IsEmpty || checkpoint.NextCategoryOrdinal != 0
					|| checkpoint.CompletedPages != 0 || checkpoint.CategoryPuts != 0
					|| checkpoint.CompletionUnixMs != 0)
					throw InvalidCheckpoint();
				break;
			case WikiCategoryMigrationPhase.Building:
			case WikiCategoryMigrationPhase.Complete:
				if (hasClear || hasCompleted != (checkpoint.CompletedPages > 0)
					|| (!hasCompleted && !hasCurrent && checkpoint.CategoryPuts != 0))
					throw InvalidCheckpoint();
				if (hasCurrent)
				{
					if (checkpoint.Phase != WikiCategoryMigrationPhase.Building
						|| checkpoint.CurrentSourceHash.Length != 32 || checkpoint.NextCategoryOrdinal == 0
						|| checkpoint.CategoryPuts < checkpoint.NextCategoryOrdinal
						|| (hasCompleted && checkpoint.CurrentSourceKey.SequenceCompareTo(checkpoint.LastCompletedSourceKey) <= 0))
						throw InvalidCheckpoint();
				}
				else if (!checkpoint.CurrentSourceHash.IsEmpty || checkpoint.NextCategoryOrdinal != 0)
					throw InvalidCheckpoint();
				if (checkpoint.Phase == WikiCategoryMigrationPhase.Building
					? checkpoint.CompletionUnixMs != 0
					: checkpoint.CompletionUnixMs <= 0)
					throw InvalidCheckpoint();
				break;
			default:
				throw InvalidCheckpoint();
		}
	}

	private static void RequireZero(ReadOnlySpan<byte> bytes, CancellationToken token)
	{
		for (var index = 0; index < bytes.Length; index++)
		{
			token.ThrowIfCancellationRequested();
			if (bytes[index] != 0) throw InvalidCheckpoint();
		}
	}

	private static void CopyWithCancellation(ReadOnlySpan<byte> source, Span<byte> destination,
		CancellationToken token)
	{
		for (var offset = 0; offset < source.Length;)
		{
			token.ThrowIfCancellationRequested();
			var length = Math.Min(CopyChunkBytes, checked(source.Length - offset));
			source.Slice(offset, length).CopyTo(destination.Slice(offset, length));
			offset = checked(offset + length);
		}
	}

	private static InvalidDataException InvalidCheckpoint() => new(InvalidCheckpointMessage);
}
