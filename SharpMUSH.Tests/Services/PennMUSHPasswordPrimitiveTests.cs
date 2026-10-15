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

/// <summary>
/// SHA-256-crypt and SHA-512-crypt, from Ulrich Drepper's specification "Unix crypt using SHA-256 and
/// SHA-512": its own test vectors, then values from <c>openssl passwd -5/-6</c> (OpenSSL 3).
/// </summary>
public class ShaCryptTests
{
	/// <summary>Longer than a SHA-512 digest, so the password spans more than one block of B.</summary>
	private const string HundredXs =
		"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";

	/// <summary>The specification's vectors, including a salt cut to 16 characters and rounds raised to 1000.</summary>
	[Test]
	[Arguments("Hello world!", "saltstring", null,
		"$6$saltstring$svn8UoSVapNtMuq1ukKS4tPQd8iKwSMHWjl/O817G3uBnIFNjnQJuesI68u4OTLiBFdcbYEdFCoEOfaS35inz1")]
	[Arguments("Hello world!", "saltstringsaltst", 10000,
		"$6$rounds=10000$saltstringsaltst$OW1/O6BYHV6BcXZu8QVeXbDWra3Oeqh0sbHbbMCVNSnCM/UrjmM0Dp8vOuZeHBy/YTBmSK6H9qs/y3RnOaw5v.")]
	[Arguments("This is just a test", "toolongsaltstrin", 5000,
		"$6$rounds=5000$toolongsaltstrin$lQ8jolhgVRVhY4b5pZKaysCLi0QBxGoNeKQzQ3glMhwllF7oGDZxUhx1yxdYcz/e1JSbq3y6JMxxl8audkUEm0")]
	[Arguments("the minimum number is still observed", "roundstoolow", 1000,
		"$6$rounds=1000$roundstoolow$kUMsbe306n21p9R.FRkW3IGn.S9NPN0x50YhH1xhLsPuWGsUSklZt58jaTfF4ZEQpyUNGc0dqbpBYYBaHHrsX.")]
	[Arguments(HundredXs, "abcdefghijklmnop", null,
		"$6$abcdefghijklmnop$r/HtRGuCzMNedONV3I8Xz8Qm4Qy85pQ/RigX0agmbAzWsa3BCc1B564NfCD/Giyliby36KbTMkrVLsmFYtbTu1")]
	[Arguments("Pässwörd", "utf8", null,
		"$6$utf8$ZpofdsFxm77zz7Q8QPs5tq8mbKLW6kJzUJo4oEx61H6QKpovxbG21BBHp2dDIG2X2JEuNO29ts65OJG5nRPRy.")]
	[Arguments("hunter2", "saltstring", null,
		"$6$saltstring$q2.778Y7vt0Ij2OIl01VlxEE6SEh8ZCtgFbyJX8fYkl5S7gx32QO24FVg.rs4DkoAs9t6R19x4z8g69teXFxA0")]
	public async Task Sha512MatchesTheVectors(string password, string salt, int? rounds, string expected)
	{
		await Assert.That(ShaCrypt.Crypt(System.Security.Cryptography.HashAlgorithmName.SHA512,
			Encoding.UTF8.GetBytes(password), Encoding.ASCII.GetBytes(salt), rounds)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("Hello world!", "saltstring", null, "$5$saltstring$5B8vYYiY.CVt1RlTTf8KbXBH3hsxY/GNooZaBBGWEc5")]
	[Arguments("Hello world!", "saltstringsaltst", 10000,
		"$5$rounds=10000$saltstringsaltst$3xv.VbSHBb41AL9AvLeujZkZRBAwqFMz2.opqey6IcA")]
	[Arguments("This is just a test", "toolongsaltstrin", 5000,
		"$5$rounds=5000$toolongsaltstrin$Un/5jzAHMgOGZ5.mWJpuVolil07guHPvOW8mGRcvxa5")]
	[Arguments(HundredXs, "abcdefghijklmnop", null, "$5$abcdefghijklmnop$eq0z.PIuAZvCCSUXhAcrIS5rtAk4zxY4tlzXoWXdQO5")]
	[Arguments("hunter2", "x", 1000, "$5$rounds=1000$x$pgi2F.tEkz1M7RxxpBu9HS8hxAsjsRCvgjiI66p7SH9")]
	public async Task Sha256MatchesTheVectors(string password, string salt, int? rounds, string expected)
	{
		await Assert.That(ShaCrypt.Crypt(System.Security.Cryptography.HashAlgorithmName.SHA256,
			Encoding.UTF8.GetBytes(password), Encoding.ASCII.GetBytes(salt), rounds)).IsEqualTo(expected);
	}
}
