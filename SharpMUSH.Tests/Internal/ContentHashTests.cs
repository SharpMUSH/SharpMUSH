using System.Security.Cryptography;
using System.Text;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Tests.Internal;

/// <summary>
/// The engine fingerprints serialized values in two independent places - package baselines and
/// object snapshots - and those two used to hash the same input to two different strings, because
/// one of the private helpers lowercased its hex and the other did not. These tests pin the single
/// shared helper: one input, one output, in the casing that persisted snapshot digests were written
/// with and are re-verified against.
/// </summary>
public class ContentHashTests
{
	[Test]
	[Arguments("")]
	[Arguments("a")]
	[Arguments("CMD_+BBREAD")]
	[Arguments("{\"Digest\":\"\",\"Name\":\"Widget\"}")]
	[Arguments("mixed CASE and Ünïcödé ☃")]
	public async ValueTask Sha256Hex_MatchesUppercaseHexOfSha256OverUtf8(string value)
	{
		var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

		await Assert.That(ContentHash.Sha256Hex(value)).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask Sha256Hex_IsStableForKnownInput()
	{
		// The canonical SHA-256 of "abc", uppercase - the casing snapshot digests are stored in.
		await Assert.That(ContentHash.Sha256Hex("abc"))
			.IsEqualTo("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");
	}

	[Test]
	public async ValueTask Sha256Hex_GivesOneAnswerPerInput()
	{
		// Package baselines and snapshot digests now agree: same input, byte-identical string.
		const string value = "the value both call sites hash";

		await Assert.That(ContentHash.Sha256Hex(value)).IsEqualTo(ContentHash.Sha256Hex(value));
		await Assert.That(ContentHash.Sha256Hex(value)).IsNotEqualTo(ContentHash.Sha256Hex(value + "!"));
	}
}
