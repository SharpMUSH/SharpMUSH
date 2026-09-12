using System.Text;
using SharpMUSH.Library.Services.Passwords;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// SHA-0, the 1993 FIPS 180 digest SHA-1 replaced, which .NET does not ship. PennMUSH's old
/// passwords are built from it.
/// </summary>
public class Sha0Tests
{
	/// <summary>FIPS PUB 180 (May 1993) appendices A and B, and the digest of the empty message.</summary>
	[Test]
	[Arguments("abc", "0164b8a914cd2a5e74c4f7ff082c4d97f1edf880")]
	[Arguments("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", "d2516ee1acfa5baf33dfc1c471e438449ef134c8")]
	[Arguments("", "f96cea198ad1dd5617ac084a3d92c6107708c0ef")]
	public async Task MatchesThePublishedVectors(string message, string expected)
	{
		await Assert.That(Convert.ToHexStringLower(Sha0.HashData(Encoding.ASCII.GetBytes(message)))).IsEqualTo(expected);
	}

	/// <summary>FIPS PUB 180 appendix C: a million repetitions of "a", which spans many blocks.</summary>
	[Test]
	public async Task MatchesTheMillionCharacterVector()
	{
		var message = Enumerable.Repeat((byte)'a', 1_000_000).ToArray();

		await Assert.That(Convert.ToHexStringLower(Sha0.HashData(message))).IsEqualTo("3232affa48628a26653b5aaa44541fd90d690603");
	}
}

/// <summary>
/// Traditional DES-based <c>crypt(3)</c>, the "descrypt" of crypt(5): eight characters of seven bits,
/// a 12-bit salt that perturbs DES's expansion, 25 encryptions of a zero block.
/// </summary>
/// <remarks>
/// Every vector is <c>perl -e 'print crypt($ARGV[0], $ARGV[1])'</c> against libxcrypt 4.5.2, the
/// <c>crypt</c> the PennMUSH oracle links; <c>XXuOowDRD5rnE</c> was also accepted by the oracle itself.
/// </remarks>
public class DesCryptTests
{
	[Test]
	[Arguments("hunter2", "XX", "XXuOowDRD5rnE")]
	[Arguments("", "XX", "XXUp2ozpdysrQ")]
	[Arguments("a", "XX", "XXvN4TwmfU6vk")]
	[Arguments("hunter23", "XX", "XXiQZcXgv6wJE")]
	[Arguments("hunter2345", "XX", "XXiQZcXgv6wJE")]
	[Arguments("Pässwörd", "XX", "XXQFR5McKImrM")]
	[Arguments("p@ss w0rd!", "XX", "XXfk9fWh1wYAc")]
	[Arguments("~~~~~~~~", "XX", "XXuOLdP1GGkA6")]
	[Arguments("password", "ab", "abJnggxhB/yWI")]
	[Arguments("password", "./", "./xZjzHv5vzVE")]
	[Arguments("password", "zz", "zzXUHfURnGg8I")]
	[Arguments("password", "9A", "9AYl9JK28Uy02")]
	public async Task MatchesLibxcrypt(string password, string salt, string expected)
	{
		await Assert.That(DesCrypt.Crypt(Encoding.UTF8.GetBytes(password), salt)).IsEqualTo(expected);
	}
}
