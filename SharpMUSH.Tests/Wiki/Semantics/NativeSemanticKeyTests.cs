using System.Buffers.Binary;
using SharpMUSH.Tools.WikiSemantics.Native;

namespace SharpMUSH.Tests.Wiki.Semantics;

public class NativeSemanticKeyTests
{
	[Test]
	public async Task SignedOrderIncludesExtremesAndRandomValues()
	{
		var random = new Random(143);
		var values = new long[] { long.MinValue, -1, 0, 1, long.MaxValue }
			.Concat(Enumerable.Range(0, 500).Select(_ => random.NextInt64(long.MinValue, long.MaxValue))).Order().ToArray();
		byte[]? previous = null;
		foreach (var value in values)
		{
			var key = NativeKeys.EncodeInteger(value);
			await Assert.That(key.Length).IsEqualTo(8);
			await Assert.That(NativeKeys.DecodeInteger(key)).IsEqualTo(value);
			await Assert.That(BinaryPrimitives.ReadUInt64BigEndian(key)).IsEqualTo(unchecked((ulong)value) ^ 0x8000000000000000UL);
			if (previous is not null) await Assert.That(previous.AsSpan().SequenceCompareTo(key) <= 0).IsTrue();
			previous = key;
		}
	}
	[Test]
	public async Task PostingLayoutHasSubjectTieBreakAndDistinctScalarTags()
	{
		var one = NativeKeys.EncodeId(1);
		var first = NativeKeys.Posting(1, 2, 3, NativeScalarTag.Integer, one, 7);
		var second = NativeKeys.Posting(1, 2, 3, NativeScalarTag.Integer, one, 8);
		await Assert.That(first.Length).IsEqualTo(41);
		await Assert.That(first.AsSpan().SequenceCompareTo(second) < 0).IsTrue();
		await Assert.That(BinaryPrimitives.ReadUInt64BigEndian(first.AsSpan(33))).IsEqualTo(7UL);
		var keys = Enum.GetValues<NativeScalarTag>().Select(t => Convert.ToHexString(NativeKeys.Forward(1, 2, t, one))).Distinct().Count();
		await Assert.That(keys).IsEqualTo(4);
		await Assert.That(NativeKeys.Subject(1).Length).IsEqualTo(8);
		await Assert.That(NativeKeys.RegistryLookup(NativeRegistryKind.Page, "same").Length).IsEqualTo(33);
		await Assert.That(NativeKeys.RegistryReverse(NativeRegistryKind.Page, 1).Length).IsEqualTo(9);
		await Assert.That(NativeKeys.RegistryLookup(NativeRegistryKind.Page, "same").SequenceEqual(NativeKeys.RegistryLookup(NativeRegistryKind.Keyword, "same"))).IsFalse();
	}
	[Test]
	public async Task InvalidLengthsIdsTagsAndBooleanPayloadsReject()
	{
		await Assert.That(() => NativeKeys.DecodeInteger(new byte[7])).Throws<ArgumentException>();
		await Assert.That(() => NativeKeys.DecodeInteger(new byte[9])).Throws<ArgumentException>();
		await Assert.That(() => NativeKeys.EncodeId(0)).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => NativeKeys.Posting(0, 1, 1, NativeScalarTag.Integer, new byte[8], 1)).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => NativeKeys.Forward(1, 1, (NativeScalarTag)99, new byte[8])).Throws<ArgumentException>();
		await Assert.That(() => NativeKeys.Forward(1, 1, NativeScalarTag.Boolean, NativeKeys.EncodeId(2))).Throws<ArgumentException>();
	}
}
