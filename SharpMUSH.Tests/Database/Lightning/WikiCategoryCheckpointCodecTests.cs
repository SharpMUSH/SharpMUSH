using System.Buffers.Binary;
using System.Text;
using SharpMUSH.Database.Lightning.Migration;

namespace SharpMUSH.Tests.Database.Lightning;

public class WikiCategoryCheckpointCodecTests
{
	// The wire oracle is authored here, without calling the production encoder.
	private static byte[] Wire(byte phase = 2, byte flags = 6, byte[]? clear = null,
		ulong revision = 1, ulong cleared = 9, ulong completed = 3, ulong puts = 5,
		ulong ordinal = 2, long time = 0)
	{
		clear ??= [];
		var bytes = new byte[112 + clear.Length];
		new byte[] { 0x57, 0x43, 0x4d, 0x31, 0, 1, 0, 1, phase, flags, 0, 0 }.CopyTo(bytes, 0);
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(12), revision);
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(20), cleared);
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(28), completed);
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(36), puts);
		BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(44), (uint)clear.Length);
		clear.CopyTo(bytes, 48);
		if ((flags & 2) != 0) bytes[55 + clear.Length] = 1;
		if ((flags & 4) != 0)
		{
			bytes[63 + clear.Length] = 2;
			for (var index = 0; index < 32; index++) bytes[64 + clear.Length + index] = (byte)(index + 1);
		}
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(96 + clear.Length), ordinal);
		BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(104 + clear.Length), time);
		return bytes;
	}

	private static byte[] Begin() => Wire(1, 0, cleared: 0, completed: 0, puts: 0, ordinal: 0);
	private static byte[] Clearing(byte[] clear) => Wire(1, 1, clear, cleared: 1, completed: 0, puts: 0, ordinal: 0);
	private static byte[] Complete() => Wire(3, 2, ordinal: 0, time: 1);

	private sealed record Snapshot(byte Phase, ulong Revision, ulong Cleared, ulong Completed, ulong Puts,
		byte[] Clear, byte[] Last, byte[] Current, byte[] Hash, ulong Ordinal, long Time);

	private static Snapshot Read(byte[] bytes, int maxKey = 511)
	{
		var value = WikiCategoryCheckpointCodec.Decode(bytes, maxKey);
		return new((byte)value.Phase, value.Revision, value.ClearedRows, value.CompletedPages, value.CategoryPuts,
			value.LastClearedKey.ToArray(), value.LastCompletedSourceKey.ToArray(), value.CurrentSourceKey.ToArray(),
			value.CurrentSourceHash.ToArray(), value.NextCategoryOrdinal, value.CompletionUnixMs);
	}

	private static byte[] Reencode(byte[] bytes, int maxKey = 511)
	{
		var value = WikiCategoryCheckpointCodec.Decode(bytes, maxKey);
		return WikiCategoryCheckpointCodec.Encode(value, maxKey);
	}

	private static int Length(byte[] bytes, int maxKey = 511)
		=> WikiCategoryCheckpointCodec.EncodedLength(WikiCategoryCheckpointCodec.Decode(bytes, maxKey), maxKey);

	private static Exception? DecodeFailure(byte[] bytes, int maxKey = 511, CancellationToken token = default)
	{
		try { _ = WikiCategoryCheckpointCodec.Decode(bytes, maxKey, token); return null; }
		catch (Exception error) { return error; }
	}

	[Test]
	public async Task LiteralWireFixtureDecodesAllFieldsAndEncodesExactly()
	{
		var fixture = Wire(revision: 0x0102030405060708, cleared: 0x1112131415161718,
			completed: 0x2122232425262728, puts: 0x3132333435363738, ordinal: 0x0101010101010101);
		var decoded = Read(fixture);
		var exact = Reencode(fixture).AsSpan().SequenceEqual(fixture)
			&& EncodeLiteralModel().AsSpan().SequenceEqual(fixture);
		await Assert.That(exact).IsTrue();
		await Assert.That(decoded.Phase == 2 && decoded.Revision == 0x0102030405060708
			&& decoded.Cleared == 0x1112131415161718 && decoded.Completed == 0x2122232425262728
			&& decoded.Puts == 0x3132333435363738 && decoded.Ordinal == 0x0101010101010101
			&& decoded.Clear.Length == 0 && decoded.Last.Length == 8 && decoded.Last[7] == 1
			&& decoded.Current.Length == 8 && decoded.Current[7] == 2
			&& decoded.Hash.SequenceEqual(Enumerable.Range(1, 32).Select(x => (byte)x)) && decoded.Time == 0).IsTrue();
		await Assert.That(Length(fixture)).IsEqualTo(112);
		await Assert.That(WikiCategoryCheckpointCodec.MigrationId).IsEqualTo("0004_wiki_category_value_v1");
		var keys = WikiCategoryCheckpointCodec.StateKey.SequenceEqual(Encoding.UTF8.GetBytes("core.wiki.cat.value-upgrade/state"))
			&& WikiCategoryCheckpointCodec.CompletionKey.SequenceEqual(Encoding.UTF8.GetBytes("mig:0004_wiki_category_value_v1"));
		await Assert.That(keys).IsTrue();
	}

	private static byte[] EncodeLiteralModel()
	{
		byte[] last = [0, 0, 0, 0, 0, 0, 0, 1], current = [0, 0, 0, 0, 0, 0, 0, 2];
		byte[] hash = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
			17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32];
		var value = new WikiCategoryMigrationCheckpoint(WikiCategoryMigrationPhase.Building,
			0x0102030405060708, 0x1112131415161718, 0x2122232425262728, 0x3132333435363738,
			ReadOnlySpan<byte>.Empty, last, current, hash, 0x0101010101010101, 0);
		return WikiCategoryCheckpointCodec.Encode(value, 511);
	}

	[Test]
	[Arguments("begin")]
	[Arguments("clearing")]
	[Arguments("building-empty")]
	[Arguments("building-completed")]
	[Arguments("building-current")]
	[Arguments("complete-empty")]
	[Arguments("complete")]
	[Arguments("ulong-max")]
	public async Task ValidShapesNeedNoInventedRevisionHistory(string scenario)
	{
		var fixture = scenario switch
		{
			"begin" => Begin(),
			"clearing" => Clearing([0, 0xff, 0]),
			"building-empty" => Wire(2, 0, completed: 0, puts: 0, ordinal: 0),
			"building-completed" => Wire(2, 2, ordinal: 0),
			"building-current" => Wire(2, 4, completed: 0),
			"complete-empty" => Wire(3, 0, completed: 0, puts: 0, ordinal: 0, time: long.MaxValue),
			"complete" => Complete(),
			"ulong-max" => Wire(revision: ulong.MaxValue, cleared: ulong.MaxValue, completed: ulong.MaxValue,
				puts: ulong.MaxValue, ordinal: ulong.MaxValue),
			_ => throw new ArgumentOutOfRangeException(nameof(scenario))
		};
		var same = Reencode(fixture).AsSpan().SequenceEqual(fixture);
		await Assert.That(same).IsTrue();
	}

	[Test]
	[Arguments("magic")]
	[Arguments("schema")]
	[Arguments("target")]
	[Arguments("phase-zero")]
	[Arguments("phase-four")]
	[Arguments("flags")]
	[Arguments("reserved-ten")]
	[Arguments("reserved-eleven")]
	[Arguments("short")]
	[Arguments("trailing")]
	[Arguments("oversize")]
	[Arguments("m-over-int")]
	[Arguments("m-native-limit")]
	[Arguments("m-exact-length")]
	[Arguments("absent-last")]
	[Arguments("absent-current")]
	[Arguments("absent-hash")]
	[Arguments("revision-zero")]
	[Arguments("clearing-key-missing")]
	[Arguments("clearing-key-without-count")]
	[Arguments("clearing-completed")]
	[Arguments("clearing-current")]
	[Arguments("clearing-hash")]
	[Arguments("clearing-ordinal")]
	[Arguments("clearing-puts")]
	[Arguments("clearing-time")]
	[Arguments("building-clear")]
	[Arguments("completed-missing")]
	[Arguments("completed-without-count")]
	[Arguments("current-ordinal-zero")]
	[Arguments("current-puts-low")]
	[Arguments("current-equal")]
	[Arguments("current-before")]
	[Arguments("absent-current-ordinal")]
	[Arguments("unattributed-building-puts")]
	[Arguments("unattributed-complete-puts")]
	[Arguments("building-time")]
	[Arguments("complete-current")]
	[Arguments("complete-time-zero")]
	[Arguments("complete-time-negative")]
	public async Task CorruptWireAlwaysHasFixedInputFreeDiagnostic(string scenario)
	{
		var failure = DecodeFailure(Corrupt(scenario));
		var baseline = DecodeFailure([]);
		await Assert.That(failure is InvalidDataException).IsTrue();
		await Assert.That(failure?.Message).IsEqualTo(baseline?.Message);
	}

	private static byte[] Corrupt(string scenario)
	{
		var bytes = Wire();
		switch (scenario)
		{
			case "magic": bytes[0] ^= 1; break;
			case "schema": bytes[5] = 2; break;
			case "target": bytes[7] = 2; break;
			case "phase-zero": bytes[8] = 0; break;
			case "phase-four": bytes[8] = 4; break;
			case "flags": bytes[9] |= 8; break;
			case "reserved-ten": bytes[10] = 1; break;
			case "reserved-eleven": bytes[11] = 1; break;
			case "short": bytes = bytes[..111]; break;
			case "trailing": bytes = [.. bytes, 0]; break;
			case "oversize": bytes = new byte[131073]; break;
			case "m-over-int": BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(44), uint.MaxValue); break;
			case "m-native-limit": bytes = Clearing(new byte[512]); break;
			case "m-exact-length": BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(44), 1); break;
			case "absent-last": bytes = Begin(); bytes[55] = 1; break;
			case "absent-current": bytes = Complete(); bytes[63] = 1; break;
			case "absent-hash": bytes = Complete(); bytes[95] = 1; break;
			case "revision-zero": BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(12), 0); break;
			case "clearing-key-missing": bytes = Begin(); bytes[27] = 1; break;
			case "clearing-key-without-count": bytes = Clearing([1]); bytes[27] = 0; break;
			case "clearing-completed": bytes = Wire(1, 3, [1], cleared: 1, completed: 1, puts: 0, ordinal: 0); break;
			case "clearing-current": bytes = Wire(1, 5, [1], cleared: 1, completed: 0, puts: 0, ordinal: 0); break;
			case "clearing-hash": bytes = Clearing([1]); bytes[65] = 1; break;
			case "clearing-ordinal": bytes = Clearing([1]); bytes[104] = 1; break;
			case "clearing-puts": bytes = Clearing([1]); bytes[43] = 1; break;
			case "clearing-time": bytes = Clearing([1]); bytes[112] = 1; break;
			case "building-clear": bytes = Wire(2, 7, [1]); break;
			case "completed-missing": bytes = Wire(2, 4); break;
			case "completed-without-count": bytes = Wire(completed: 0); break;
			case "current-ordinal-zero": bytes = Wire(ordinal: 0); break;
			case "current-puts-low": bytes = Wire(puts: 1); break;
			case "current-equal": bytes[63] = 1; break;
			case "current-before": bytes[63] = 0; break;
			case "absent-current-ordinal": bytes = Wire(2, 2); break;
			case "unattributed-building-puts": bytes = Wire(2, 0, completed: 0, ordinal: 0); break;
			case "unattributed-complete-puts": bytes = Wire(3, 0, completed: 0, ordinal: 0, time: 1); break;
			case "building-time": bytes[111] = 1; break;
			case "complete-current": bytes = Wire(3, time: 1); break;
			case "complete-time-zero": bytes = Complete(); bytes[111] = 0; break;
			case "complete-time-negative": bytes = Complete(); BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(104), -1); break;
			default: throw new ArgumentOutOfRangeException(nameof(scenario));
		}
		return bytes;
	}

	[Test]
	[Arguments("revision")]
	[Arguments("phase")]
	[Arguments("clear-too-long")]
	[Arguments("clear-empty")]
	[Arguments("clear-unearned")]
	[Arguments("clear-on-building")]
	[Arguments("clearing-completed")]
	[Arguments("clearing-current")]
	[Arguments("clearing-hash")]
	[Arguments("clearing-ordinal")]
	[Arguments("clearing-puts")]
	[Arguments("clearing-time")]
	[Arguments("completed-seven")]
	[Arguments("completed-nine")]
	[Arguments("completed-missing")]
	[Arguments("completed-unearned")]
	[Arguments("current-seven")]
	[Arguments("current-nine")]
	[Arguments("hash-thirty-one")]
	[Arguments("hash-thirty-three")]
	[Arguments("hash-without-current")]
	[Arguments("ordinal-without-current")]
	[Arguments("ordinal-zero")]
	[Arguments("puts-low")]
	[Arguments("current-equal")]
	[Arguments("building-time")]
	[Arguments("complete-current")]
	[Arguments("complete-no-time")]
	[Arguments("complete-negative-time")]
	[Arguments("unattributed-puts")]
	public async Task MaterializationAndLengthRejectInvalidRepresentations(string scenario)
	{
		var encodeFailure = RepresentationFailure(scenario, false);
		var lengthFailure = RepresentationFailure(scenario, true);
		await Assert.That(encodeFailure is InvalidDataException && lengthFailure is InvalidDataException).IsTrue();
		var diagnostic = DecodeFailure([])?.Message;
		await Assert.That(encodeFailure?.Message).IsEqualTo(diagnostic);
		await Assert.That(lengthFailure?.Message).IsEqualTo(diagnostic);
	}

	private static Exception? RepresentationFailure(string scenario, bool lengthOnly)
	{
		var phase = WikiCategoryMigrationPhase.Building;
		ulong revision = 1, cleared = 0, completed = 1, puts = 2, ordinal = 1;
		long time = 0;
		byte[] clear = [], last = new byte[8], current = new byte[8], hash = new byte[32];
		current[7] = 1;
		if (scenario.StartsWith("clearing-", StringComparison.Ordinal))
		{
			phase = WikiCategoryMigrationPhase.Clearing;
			clear = [1]; cleared = 1; last = []; current = []; hash = [];
			completed = puts = ordinal = 0;
		}
		switch (scenario)
		{
			case "revision": revision = 0; break;
			case "phase": phase = (WikiCategoryMigrationPhase)255; break;
			case "clear-too-long": phase = WikiCategoryMigrationPhase.Clearing; clear = new byte[512]; cleared = 1; last = []; current = []; hash = []; completed = puts = ordinal = 0; break;
			case "clear-empty": phase = WikiCategoryMigrationPhase.Clearing; cleared = 1; last = []; current = []; hash = []; completed = puts = ordinal = 0; break;
			case "clear-unearned": phase = WikiCategoryMigrationPhase.Clearing; clear = [1]; last = []; current = []; hash = []; completed = puts = ordinal = 0; break;
			case "clear-on-building": clear = [1]; break;
			case "clearing-completed": last = new byte[8]; completed = 1; break;
			case "clearing-current": current = new byte[8]; hash = new byte[32]; break;
			case "clearing-hash": hash = new byte[32]; break;
			case "clearing-ordinal": ordinal = 1; break;
			case "clearing-puts": puts = 1; break;
			case "clearing-time": time = 1; break;
			case "completed-seven": last = new byte[7]; break;
			case "completed-nine": last = new byte[9]; break;
			case "completed-missing": last = []; break;
			case "completed-unearned": completed = 0; break;
			case "current-seven": current = new byte[7]; current[0] = 1; break;
			case "current-nine": current = new byte[9]; current[8] = 1; break;
			case "hash-thirty-one": hash = new byte[31]; break;
			case "hash-thirty-three": hash = new byte[33]; break;
			case "hash-without-current": current = []; ordinal = 0; break;
			case "ordinal-without-current": current = []; hash = []; break;
			case "ordinal-zero": ordinal = 0; break;
			case "puts-low": puts = 0; break;
			case "current-equal": current[7] = 0; break;
			case "building-time": time = 1; break;
			case "complete-current": phase = WikiCategoryMigrationPhase.Complete; time = 1; break;
			case "complete-no-time": phase = WikiCategoryMigrationPhase.Complete; current = []; hash = []; ordinal = 0; break;
			case "complete-negative-time": phase = WikiCategoryMigrationPhase.Complete; current = []; hash = []; ordinal = 0; time = -1; break;
			case "unattributed-puts": current = []; hash = []; last = []; ordinal = completed = 0; break;
			default: throw new ArgumentOutOfRangeException(nameof(scenario));
		}
		try
		{
			var value = new WikiCategoryMigrationCheckpoint(phase, revision, cleared, completed, puts,
				clear, last, current, hash, ordinal, time);
			if (lengthOnly) _ = WikiCategoryCheckpointCodec.EncodedLength(value, 511);
			else _ = WikiCategoryCheckpointCodec.Encode(value, 511);
			return null;
		}
		catch (Exception error) { return error; }
	}

	[Test]
	public async Task ZeroPageKeysAndZeroHashRemainPresent()
	{
		var currentFixture = Wire(2, 4, completed: 0);
		currentFixture.AsSpan(56, 40).Clear();
		var current = Read(currentFixture);
		var completeFixture = Complete();
		completeFixture.AsSpan(48, 8).Clear();
		var complete = Read(completeFixture);
		var same = Reencode(currentFixture).AsSpan().SequenceEqual(currentFixture)
			&& Reencode(completeFixture).AsSpan().SequenceEqual(completeFixture);
		await Assert.That(same && current.Current.Length == 8 && current.Hash.Length == 32
			&& current.Current.All(b => b == 0) && current.Hash.All(b => b == 0)
			&& complete.Last.Length == 8 && complete.Last.All(b => b == 0)).IsTrue();
	}

	[Test]
	public async Task SourceKeysUseUnsignedBytewiseOrdering()
	{
		var fixture = Wire();
		fixture[48] = 0x7f;
		fixture[56] = 0x80;
		var same = Reencode(fixture).AsSpan().SequenceEqual(fixture);
		var reversed = Wire();
		reversed[48] = 0x80;
		reversed[56] = 0x7f;
		await Assert.That(same && DecodeFailure(reversed) is InvalidDataException).IsTrue();
	}

	[Test]
	public async Task DecoderAndRepresentationBorrowTheirSpans()
	{
		var borrowed = Borrowed();
		await Assert.That(borrowed).IsTrue();
	}

	private static bool Borrowed()
	{
		var fixture = Wire();
		var decoded = WikiCategoryCheckpointCodec.Decode(fixture, 511);
		fixture[48] = 11;
		fixture[56] = 12;
		fixture[64] = 13;
		var clearFixture = Clearing([1, 2, 3]);
		var cleared = WikiCategoryCheckpointCodec.Decode(clearFixture, 511);
		clearFixture[49] = 14;
		byte[] clear = [1], last = new byte[8], current = new byte[8], hash = new byte[32];
		current[7] = 1;
		var represented = new WikiCategoryMigrationCheckpoint(WikiCategoryMigrationPhase.Building, 1, 0, 1, 1,
			ReadOnlySpan<byte>.Empty, last, current, hash, 1, 0);
		var representedClear = new WikiCategoryMigrationCheckpoint(WikiCategoryMigrationPhase.Clearing, 1, 1, 0, 0,
			clear, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, 0, 0);
		clear[0] = 21; last[0] = 22; current[0] = 23; hash[0] = 24;
		return decoded.LastCompletedSourceKey[0] == 11 && decoded.CurrentSourceKey[0] == 12
			&& decoded.CurrentSourceHash[0] == 13 && cleared.LastClearedKey[1] == 14
			&& representedClear.LastClearedKey[0] == 21 && represented.LastCompletedSourceKey[0] == 22
			&& represented.CurrentSourceKey[0] == 23 && represented.CurrentSourceHash[0] == 24;
	}

	[Test]
	[Arguments(257)]
	[Arguments(511)]
	[Arguments(4096)]
	[Arguments(130960)]
	public async Task NativeKeyCapacityAndExactHardEnvelopeBoundaryAreSupported(int maxKey)
	{
		var rawKey = new byte[maxKey];
		rawKey[0] = 0xff;
		rawKey[^1] = 0x7f;
		var fixture = Clearing(rawKey);
		WikiCategoryCheckpointCodec.ValidateEnvironmentMaxKeySize(maxKey);
		var snapshot = Read(fixture, maxKey);
		var same = Reencode(fixture, maxKey).AsSpan().SequenceEqual(fixture);
		await Assert.That(same && snapshot.Clear.AsSpan().SequenceEqual(rawKey)).IsTrue();
		await Assert.That(Length(fixture, maxKey)).IsEqualTo(112 + maxKey);
	}

	[Test]
	[Arguments(0)]
	[Arguments(-1)]
	[Arguments(-2147483648)]
	[Arguments(130961)]
	[Arguments(2147483647)]
	public async Task InvalidEnvironmentFailsEveryEntryPoint(int maxKey)
	{
		var failures = EnvironmentFailures(maxKey);
		var valid = maxKey <= 0
			? failures.All(error => error is ArgumentOutOfRangeException)
			: failures.All(error => error is NotSupportedException
				&& error.Message.Contains(((long)maxKey + 112).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
				&& error.Message.Contains("131072", StringComparison.Ordinal));
		await Assert.That(valid).IsTrue();
	}

	private static Exception?[] EnvironmentFailures(int maxKey)
	{
		var failures = new Exception?[4];
		try { WikiCategoryCheckpointCodec.ValidateEnvironmentMaxKeySize(maxKey); }
		catch (Exception error) { failures[0] = error; }
		failures[1] = DecodeFailure(Begin(), maxKey);
		var value = new WikiCategoryMigrationCheckpoint(WikiCategoryMigrationPhase.Clearing, 1, 0, 0, 0,
			ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, 0, 0);
		try { _ = WikiCategoryCheckpointCodec.Encode(value, maxKey); }
		catch (Exception error) { failures[2] = error; }
		try { _ = WikiCategoryCheckpointCodec.EncodedLength(value, maxKey); }
		catch (Exception error) { failures[3] = error; }
		return failures;
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task PreCancellationIsObservedWithCallerToken(bool encode)
	{
		using var source = new CancellationTokenSource();
		source.Cancel();
		var failure = CancellationFailure(encode, source.Token);
		await Assert.That(failure?.GetType() == typeof(OperationCanceledException)
			&& ((OperationCanceledException)failure).CancellationToken == source.Token).IsTrue();
	}

	private static Exception? CancellationFailure(bool encode, CancellationToken token)
	{
		try
		{
			if (encode)
			{
				var value = WikiCategoryCheckpointCodec.Decode(Wire(), 511);
				_ = WikiCategoryCheckpointCodec.Encode(value, 511, token);
			}
			else _ = WikiCategoryCheckpointCodec.Decode(Wire(), 511, token);
			return null;
		}
		catch (Exception error) { return error; }
	}
}
