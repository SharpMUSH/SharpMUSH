using System.Buffers.Binary;
using System.Text;
using SharpMUSH.Database.Lightning.Migration;

namespace SharpMUSH.Tests.Database.Lightning;

public class WikiCategoryAuthorityCodecTests
{
	private const string Marker = "{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17}";

	// Independently authored WCM1 wire bytes; no production encoder is an oracle.
	private static byte[] State(byte phase = 3, byte flags = 0, int clearLength = 0,
		ulong revision = 1, ulong cleared = 0, ulong completed = 0, ulong puts = 0,
		ulong ordinal = 0, long timestamp = 17)
	{
		var bytes = new byte[112 + clearLength];
		new byte[] { 0x57, 0x43, 0x4d, 0x31, 0, 1, 0, 1, phase, flags, 0, 0 }.CopyTo(bytes, 0);
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(12), revision);
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(20), cleared);
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(28), completed);
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(36), puts);
		BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(44), (uint)clearLength);
		if (clearLength != 0) bytes[48] = 0xff;
		// A completed page ID of zero is present when flag 2 is set.
		if ((flags & 4) != 0) bytes[63 + clearLength] = 1;
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(96 + clearLength), ordinal);
		BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(104 + clearLength), timestamp);
		return bytes;
	}

	private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

	// Keep borrowed spans entirely within synchronous calls, outside async state machines/lambdas.
	private static Exception? Failure(bool hasState, byte[] state, bool hasCompletion, byte[] marker,
		int maxKey = 511, CancellationToken token = default)
	{
		try
		{
			_ = WikiCategoryAuthorityCodec.Validate(hasState, state, hasCompletion, marker, maxKey, token);
			return null;
		}
		catch (Exception error) { return error; }
	}

	[Test]
	public async Task AbsentAuthorityReturnsFalse()
	{
		var result = WikiCategoryAuthorityCodec.Validate(false, [], false, [], 511);
		await Assert.That(result).IsFalse();
	}

	[Test]
	[Arguments("clearing-empty")]
	[Arguments("clearing-key")]
	[Arguments("building-empty")]
	[Arguments("building-completed")]
	[Arguments("building-current")]
	[Arguments("building-both")]
	public async Task ValidIncompleteAuthorityReturnsFalse(string scenario)
	{
		var state = Incomplete(scenario);
		var result = WikiCategoryAuthorityCodec.Validate(true, state, false, [], 511);
		await Assert.That(result).IsFalse();
	}

	private static byte[] Incomplete(string scenario) => scenario switch
	{
		"clearing-empty" => State(1, timestamp: 0),
		"clearing-key" => State(1, 1, 3, cleared: 1, timestamp: 0),
		"building-empty" => State(2, timestamp: 0),
		"building-completed" => State(2, 2, completed: 1, puts: 3, timestamp: 0),
		"building-current" => State(2, 4, puts: 2, ordinal: 2, timestamp: 0),
		"building-both" => State(2, 6, completed: 1, puts: 2, ordinal: 2, timestamp: 0),
		_ => throw new ArgumentOutOfRangeException(nameof(scenario))
	};

	[Test]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17}")]
	[Arguments("{\"AppliedUnixMs\":17,\"Id\":\"0004_wiki_category_value_v1\"}")]
	[Arguments(" \r\n{ \"Id\" : \"0004_wiki_category_value_v1\", \"AppliedUnixMs\" : 17 }\t\r\n")]
	[Arguments("{\"\\u0049d\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17}")]
	[Arguments("{\"Id\":\"\\u0030\\u0030\\u0030\\u0034_wiki_category_value_v1\",\"AppliedUnixMs\":17}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnix\\u004ds\":17}")]
	public async Task LiteralCompletePairsGrantAuthority(string marker)
	{
		var result = WikiCategoryAuthorityCodec.Validate(true, State(), true, Utf8(marker), 511);
		await Assert.That(result).IsTrue();
	}

	[Test]
	[Arguments(1L)]
	[Arguments(17L)]
	[Arguments(9223372036854775807L)]
	public async Task PositiveInt64TimestampBoundariesMatchExactly(long timestamp)
	{
		var marker = Utf8("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":"
			+ timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
		var result = WikiCategoryAuthorityCodec.Validate(true, State(timestamp: timestamp), true, marker, 511);
		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task FullUnsignedCountersAndZeroCompletedPageIdDoNotInventHistoryRequirements()
	{
		var state = State(flags: 2, revision: ulong.MaxValue, cleared: ulong.MaxValue,
			completed: ulong.MaxValue, puts: ulong.MaxValue);
		var result = WikiCategoryAuthorityCodec.Validate(true, state, true, Utf8(Marker), 511);
		await Assert.That(result).IsTrue();
	}

	[Test]
	[Arguments("absent-state-bytes")]
	[Arguments("absent-marker-bytes")]
	[Arguments("both-absent-with-bytes")]
	[Arguments("marker-only")]
	[Arguments("empty-marker-only")]
	[Arguments("present-empty-state")]
	[Arguments("complete-no-marker")]
	[Arguments("complete-empty-marker")]
	[Arguments("clearing-marker")]
	[Arguments("clearing-empty-marker")]
	[Arguments("building-marker")]
	[Arguments("building-empty-marker")]
	public async Task ConflictingPresenceAndPairsFailClosed(string scenario)
	{
		var hasState = true;
		var state = State();
		var hasCompletion = true;
		var marker = Utf8(Marker);
		switch (scenario)
		{
			case "absent-state-bytes": hasState = false; hasCompletion = false; marker = []; break;
			case "absent-marker-bytes": hasCompletion = false; break;
			case "both-absent-with-bytes": hasState = hasCompletion = false; break;
			case "marker-only": hasState = false; state = []; break;
			case "empty-marker-only": hasState = false; state = []; marker = []; break;
			case "present-empty-state": state = []; hasCompletion = false; marker = []; break;
			case "complete-no-marker": hasCompletion = false; marker = []; break;
			case "complete-empty-marker": marker = []; break;
			case "clearing-marker": state = Incomplete("clearing-key"); break;
			case "clearing-empty-marker": state = Incomplete("clearing-key"); marker = []; break;
			case "building-marker": state = Incomplete("building-current"); break;
			case "building-empty-marker": state = Incomplete("building-current"); marker = []; break;
			default: throw new ArgumentOutOfRangeException(nameof(scenario));
		}
		var failure = Failure(hasState, state, hasCompletion, marker);
		await Assert.That(failure is InvalidDataException && failure.InnerException is null).IsTrue();
	}

	[Test]
	[Arguments("")]
	[Arguments("{}")]
	[Arguments("[]")]
	[Arguments("null")]
	[Arguments("17")]
	[Arguments("true")]
	[Arguments("\"SECRET\"")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\"}")]
	[Arguments("{\"AppliedUnixMs\":17}")]
	[Arguments("{\"Id\":\"SECRET\",\"AppliedUnixMs\":17}")]
	[Arguments("{\"id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17}")]
	[Arguments("{\"Id\":\"0004_WIKI_category_value_v1\",\"AppliedUnixMs\":17}")]
	[Arguments("{\"Id\":null,\"AppliedUnixMs\":17}")]
	[Arguments("{\"Id\":17,\"AppliedUnixMs\":17}")]
	[Arguments("{\"Id\":[],\"AppliedUnixMs\":17}")]
	[Arguments("{\"Id\":{},\"AppliedUnixMs\":17}")]
	[Arguments("{\"Id\":true,\"AppliedUnixMs\":17}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":0}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":-1}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":16}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":18}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17.0}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17e0}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":\"17\"}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":null}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":false}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":[]}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":{}}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":9223372036854775808}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":-9223372036854775809}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":18446744073709551615}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17,\"SECRET\":1}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"\\u0049d\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17,\"AppliedUnixMs\":17}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17,\"AppliedUnix\\u004ds\":17}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17,}")]
	[Arguments("{/*SECRET*/\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17}//SECRET")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17} {}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17} null")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17}SECRET")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":017}")]
	[Arguments("{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17")]
	public async Task StrictMarkerSchemaHasFixedInputFreeDiagnostic(string marker)
	{
		var failure = Failure(true, State(), true, Utf8(marker));
		var baseline = Failure(true, State(), true, Utf8("{}"));
		await Assert.That(failure is InvalidDataException && failure.InnerException is null).IsTrue();
		await Assert.That(failure?.Message).IsEqualTo(baseline?.Message);
		await Assert.That(failure?.Message.Contains("SECRET", StringComparison.Ordinal) == false).IsTrue();
	}


	[Test]
	[Arguments("invalid-utf8")]
	[Arguments("utf8-bom")]
	[Arguments("trailing-nul")]
	public async Task NonJsonByteSequencesAreRejectedWithoutRawDiagnostics(string scenario)
	{
		byte[] marker = scenario switch
		{
			"invalid-utf8" => new byte[] { (byte)'{', (byte)'"', 0xff, (byte)'"', (byte)':', (byte)'1', (byte)'}' },
			"utf8-bom" => [0xef, 0xbb, 0xbf, .. Utf8(Marker)],
			"trailing-nul" => [.. Utf8(Marker), 0],
			_ => throw new ArgumentOutOfRangeException(nameof(scenario))
		};
		var failure = Failure(true, State(), true, marker);
		var baseline = Failure(true, State(), true, Utf8("{}"));
		await Assert.That(failure is InvalidDataException && failure.InnerException is null).IsTrue();
		await Assert.That(failure?.Message).IsEqualTo(baseline?.Message);
	}

	[Test]
	[Arguments(1023, true)]
	[Arguments(1024, true)]
	[Arguments(1025, false)]
	[Arguments(131072, false)]
	public async Task MarkerByteCapIncludesOtherwiseValidTrailingWhitespace(int length, bool accepted)
	{
		var marker = Utf8(Marker.PadRight(length));
		if (accepted)
		{
			var result = WikiCategoryAuthorityCodec.Validate(true, State(), true, marker, 511);
			await Assert.That(result).IsTrue();
		}
		else
		{
			var failure = Failure(true, State(), true, marker);
			await Assert.That(failure is InvalidDataException).IsTrue();
		}
		var maxMarkerBytes = WikiCategoryAuthorityCodec.MaxMarkerBytes;
		await Assert.That(maxMarkerBytes).IsEqualTo(1024);
	}

	[Test]
	[Arguments("magic")]
	[Arguments("revision-zero")]
	[Arguments("unknown-phase")]
	[Arguments("complete-zero-time")]
	[Arguments("complete-negative-time")]
	[Arguments("complete-current")]
	[Arguments("short")]
	[Arguments("trailing")]
	[Arguments("over-cap")]
	public async Task AuthorityUsesStrictCheckpointDecoder(string scenario)
	{
		var state = State();
		switch (scenario)
		{
			case "magic": state[0] ^= 1; break;
			case "revision-zero": state.AsSpan(12, 8).Clear(); break;
			case "unknown-phase": state[8] = 4; break;
			case "complete-zero-time": state = State(timestamp: 0); break;
			case "complete-negative-time": state = State(timestamp: -1); break;
			case "complete-current": state = State(flags: 4, puts: 1, ordinal: 1); break;
			case "short": state = state[..111]; break;
			case "trailing": state = [.. state, 0]; break;
			case "over-cap": state = new byte[131073]; break;
			default: throw new ArgumentOutOfRangeException(nameof(scenario));
		}
		var failure = Failure(true, state, true, Utf8(Marker));
		await Assert.That(failure is InvalidDataException && failure.InnerException is null).IsTrue();
	}


	[Test]
	[Arguments("clearing-revision-zero")]
	[Arguments("building-revision-zero")]
	[Arguments("clearing-completion-time")]
	[Arguments("building-completion-time")]
	[Arguments("clearing-completed-source")]
	[Arguments("clearing-current-source")]
	[Arguments("building-unattributed-puts")]
	[Arguments("building-current-ordinal-zero")]
	[Arguments("building-absent-completed-slot")]
	[Arguments("building-absent-current-slot")]
	[Arguments("building-absent-hash-slot")]
	[Arguments("clearing-schema-version")]
	[Arguments("building-target-version")]
	[Arguments("clearing-reserved-byte")]
	[Arguments("building-reserved-byte")]
	[Arguments("clearing-short-envelope")]
	[Arguments("building-trailing-envelope")]
	public async Task CorruptIncompleteStateWithoutMarkerCannotReturnFalse(string scenario)
	{
		var state = scenario.StartsWith("clearing-", StringComparison.Ordinal)
			? Incomplete("clearing-key") : Incomplete("building-empty");
		switch (scenario)
		{
			case "clearing-revision-zero":
			case "building-revision-zero": state.AsSpan(12, 8).Clear(); break;
			case "clearing-completion-time":
			case "building-completion-time": state[^1] = 1; break;
			case "clearing-completed-source": state[9] |= 2; state[35] = 1; break;
			case "clearing-current-source": state[9] |= 4; break;
			case "building-unattributed-puts": state[43] = 1; break;
			case "building-current-ordinal-zero":
				state = State(2, 4, puts: 1, ordinal: 0, timestamp: 0); break;
			case "building-absent-completed-slot": state[55] = 1; break;
			case "building-absent-current-slot": state[63] = 1; break;
			case "building-absent-hash-slot": state[95] = 1; break;
			case "clearing-schema-version": state[5] = 2; break;
			case "building-target-version": state[7] = 2; break;
			case "clearing-reserved-byte": state[10] = 1; break;
			case "building-reserved-byte": state[11] = 1; break;
			case "clearing-short-envelope": state = state[..^1]; break;
			case "building-trailing-envelope": state = [.. state, 0]; break;
			default: throw new ArgumentOutOfRangeException(nameof(scenario));
		}
		var failure = Failure(true, state, false, []);
		var baselineState = Incomplete("building-empty");
		baselineState.AsSpan(12, 8).Clear();
		var baseline = Failure(true, baselineState, false, []);
		await Assert.That(failure is InvalidDataException && failure.InnerException is null).IsTrue();
		await Assert.That(failure?.Message).IsEqualTo(baseline?.Message);
	}

	[Test]
	[Arguments(1)]
	[Arguments(511)]
	[Arguments(4096)]
	[Arguments(130960)]
	public async Task ValidNativeKeyEnvelopeIsRespectedForIncompleteState(int maxKey)
	{
		var state = State(1, 1, maxKey, cleared: 1, timestamp: 0);
		var result = WikiCategoryAuthorityCodec.Validate(true, state, false, [], maxKey);
		var tooLong = State(1, 1, maxKey + 1, cleared: 1, timestamp: 0);
		var failure = Failure(true, tooLong, false, [], maxKey);
		await Assert.That(result).IsFalse();
		await Assert.That(failure is InvalidDataException).IsTrue();
	}

	[Test]
	[Arguments(0, false)]
	[Arguments(-1, false)]
	[Arguments(-2147483648, false)]
	[Arguments(130961, true)]
	[Arguments(2147483647, true)]
	public async Task AbsentAuthorityStillValidatesEnvironmentCapacity(int maxKey, bool unsupported)
	{
		var failure = Failure(false, [], false, [], maxKey);
		var expected = unsupported ? failure is NotSupportedException : failure is ArgumentOutOfRangeException;
		await Assert.That(expected).IsTrue();
	}

	[Test]
	[Arguments("absent")]
	[Arguments("incomplete")]
	[Arguments("complete")]
	[Arguments("corrupt")]
	[Arguments("bad-environment")]
	public async Task CancellationAtEntryUsesCallerToken(string scenario)
	{
		using var source = new CancellationTokenSource();
		source.Cancel();
		var hasState = scenario != "absent";
		byte[] state = scenario switch
		{
			"absent" => [],
			"incomplete" => Incomplete("building-empty"),
			"corrupt" => new byte[] { 0xff },
			_ => State()
		};
		var hasCompletion = scenario is "complete" or "bad-environment";
		var failure = Failure(hasState, state, hasCompletion, hasCompletion ? Utf8(Marker) : [],
			scenario == "bad-environment" ? 0 : 511, source.Token);
		await Assert.That(failure is OperationCanceledException canceled
			&& canceled.CancellationToken == source.Token).IsTrue();
	}

	[Test]
	public async Task ValidationBorrowsSlicesWithoutMutatingCallerBuffers()
	{
		var result = ValidateSlices();
		await Assert.That(result).IsTrue();
	}

	private static bool ValidateSlices()
	{
		var state = State();
		var marker = Utf8(Marker);
		byte[] stateBacking = [0xff, .. state, 0xfe];
		byte[] markerBacking = [0xfd, .. marker, 0xfc];
		var granted = WikiCategoryAuthorityCodec.Validate(true, stateBacking.AsSpan(1, state.Length),
			true, markerBacking.AsSpan(1, marker.Length), 511);
		return granted && stateBacking[0] == 0xff && stateBacking[^1] == 0xfe
			&& markerBacking[0] == 0xfd && markerBacking[^1] == 0xfc
			&& stateBacking.AsSpan(1, state.Length).SequenceEqual(state)
			&& markerBacking.AsSpan(1, marker.Length).SequenceEqual(marker);
	}
}
