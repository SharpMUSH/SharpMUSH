using Mediator;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Every stored-password shape PennMUSH's <c>password_check</c> (src/player.c) accepts, in its order:
/// the formatted <c>V:ALGO:HASH:TIMESTAMP</c> string, an old SHA-0 digest, <c>crypt(3)</c> with salt
/// "XX", a TinyMUX password, and plaintext.
/// </summary>
/// <remarks>
/// Unless noted, each vector is for the password <c>hunter2</c> and was checked against PennMUSH
/// 1.8.8p0 (rev 80a1d5b) built against OpenSSL 3 and libxcrypt 4.5.2: the value was written into a
/// player's XYXXY in the database, <c>connect</c> succeeded with <c>hunter2</c> and failed with a wrong
/// password, and PennMUSH rewrote the legacy ones to <c>2:sha512:</c> on that login. The
/// <c>2:sha512:</c> vectors are hashes PennMUSH itself wrote.
/// </remarks>
public class PennMUSHLegacyPasswordTests
{
	private const string Password = "hunter2";
	private const string User = "#3:1789141638000";

	private static readonly PasswordService Service = new(Substitute.For<IMediator>(), new PasswordHasher<string>());

	private static bool Valid(string stored, string attempt = Password) => Service.PasswordIsValid(User, attempt, stored);

	// ---- the formatted string ---------------------------------------------------------------------

	/// <summary>PennMUSH 1.8.8's default: salted SHA-512 (<c>PASSWORD_HASH</c> in src/mycrypt.c).</summary>
	[Test]
	[Arguments("2:sha512:9K276ed5a8de5d5b3333cac32f33cd10b8136792c98af63dc8ab0369740e349491d6e345b20a35c2e89d5ccdaa85dfb52fea87b2288659756c9b1be860eb477e8c:1789141638")]
	[Arguments("2:sha512:3y77b2565d3d9d6e66f4baab42ec0f9cf9dd7f61e90aff18c6a82880b1f1f98675d7aae927439d34e1c1ccc3b8b8b04961a0551c509d95e3ebf3aff6a99c943248:1789141695")]
	[Arguments("2:sha512:od216dbb70fece64bf86291a57cde9b067c0c311b26cffdd54d9c74944422ef8e54d839f58f83cde0f0a8188ed4a8c6b3ebb13f57701c7d2b2e35ae268e0b854a0:1789141700")]
	public async Task AFormattedSha512PasswordWrittenByPennMUSHValidates(string stored)
	{
		await Assert.That(Valid(stored)).IsTrue();
		await Assert.That(Valid(stored, "wrongpass")).IsFalse();
		await Assert.That(Service.NeedsRehash(stored)).IsTrue();
	}

	/// <summary>Version 1 is unsalted: the whole hash field is the digest of the password alone.</summary>
	[Test]
	public async Task AnUnsaltedVersionOnePasswordValidates()
	{
		const string stored = "1:sha1:f3bbbd66a63d4bf1747940578ec3d0103530e21d:0";

		await Assert.That(Valid(stored)).IsTrue();
		await Assert.That(Valid(stored, "wrongpass")).IsFalse();
	}

	/// <summary>
	/// PennMUSH looks the digest up by name (case-insensitively, through OpenSSL), so a game configured
	/// with any of these wrote passwords in it. Computed as <c>password_hash</c> would, with salt "ab".
	/// </summary>
	[Test]
	[Arguments("md5")]
	[Arguments("sha1")]
	[Arguments("SHA256")]
	[Arguments("sha384")]
	public async Task EveryDigestPennMUSHCanNameValidates(string algorithm)
	{
		var stored = PennFormatted(algorithm, "ab", Password);

		await Assert.That(Valid(stored)).IsTrue();
		await Assert.That(Valid(stored, "wrongpass")).IsFalse();
	}

	// ---- SHA-0 ------------------------------------------------------------------------------------

	/// <summary>
	/// <c>XX%u%u</c> of the first two 32-bit words of SHA-0(password). Which byte order those words were
	/// read in depends on the source game's <c>reverse_shs</c>, which its database does not record, so
	/// both are accepted.
	/// </summary>
	/// <remarks>
	/// Not oracle-checked: PennMUSH built against OpenSSL 1.1+ has no SHA-0 (<c>HAVE_SHA</c> is undefined
	/// and <c>mush_crypt_sha0</c> returns ""), and the oracle rejected this value. The digest is checked
	/// against FIPS 180's SHA-0 vectors in <see cref="Sha0Tests"/>.
	/// </remarks>
	[Test]
	[Arguments("XX25278519322861399308")]
	[Arguments("XX2633345942209554858")]
	public async Task AnOldSha0PasswordValidatesInEitherByteOrder(string stored)
	{
		await Assert.That(Valid(stored)).IsTrue();
		await Assert.That(Valid(stored, "wrongpass")).IsFalse();
		await Assert.That(Service.NeedsRehash(stored)).IsTrue();
	}

	// ---- crypt(3) ---------------------------------------------------------------------------------

	[Test]
	public async Task ACryptPasswordValidates()
	{
		const string stored = "XXuOowDRD5rnE";

		await Assert.That(Valid(stored)).IsTrue();
		await Assert.That(Valid(stored, "wrongpass")).IsFalse();
		await Assert.That(Service.NeedsRehash(stored)).IsTrue();
	}

	/// <summary>descrypt reads eight characters and seven bits of each (crypt(5)).</summary>
	[Test]
	public async Task ACryptPasswordComparesOnlyItsFirstEightCharacters()
	{
		const string stored = "XXiQZcXgv6wJE"; // crypt("hunter23", "XX")

		await Assert.That(Valid(stored, "hunter23")).IsTrue();
		await Assert.That(Valid(stored, "hunter2345")).IsTrue();
		await Assert.That(Valid(stored, "hunter2")).IsFalse();
	}

	// ---- TinyMUX ----------------------------------------------------------------------------------

	/// <summary>
	/// <c>$ALGO$SALT$HASH</c>, both base64. PennMUSH's <c>check_mux_password</c> digests the salt as the
	/// base64 text it is stored as, then the password, and compares against the decoded hash.
	/// </summary>
	[Test]
	public async Task AMuxPasswordValidates()
	{
		const string stored = "$SHA1$b3JhY2xlISE=$ZpP9asZ4IHXYH2Xear7C9UcerSw=";

		await Assert.That(Valid(stored)).IsTrue();
		await Assert.That(Valid(stored, "wrongpass")).IsFalse();
		await Assert.That(Service.NeedsRehash(stored)).IsTrue();
	}

	// ---- plaintext --------------------------------------------------------------------------------

	[Test]
	public async Task APlaintextPasswordValidates()
	{
		await Assert.That(Valid(Password)).IsTrue();
		await Assert.That(Valid(Password, "wrongpass")).IsFalse();
		await Assert.That(Service.NeedsRehash(Password)).IsTrue();
	}

	/// <summary>
	/// PennMUSH refuses a plaintext match on an attempt shorter than four characters, or one starting
	/// with '$' or "XX", which look like the hashes above. The oracle rejected <c>abc</c> against a
	/// stored <c>abc</c>.
	/// </summary>
	[Test]
	[Arguments("abc")]
	[Arguments("$abcdef")]
	[Arguments("XXabcdef")]
	public async Task PlaintextRefusesAttemptsThatLookLikeHashes(string password)
	{
		await Assert.That(Valid(password, password)).IsFalse();
	}

	/// <summary>
	/// Deliberately stricter than PennMUSH, whose plaintext fallback compares against any stored value:
	/// the oracle let a player log in by typing their stored <c>1:sha1:...</c> string. Here a stored
	/// value shaped like any hash above never matches as plaintext, so knowing a hash is not knowing
	/// the password.
	/// </summary>
	[Test]
	[Arguments("1:sha1:f3bbbd66a63d4bf1747940578ec3d0103530e21d:0")]
	[Arguments("2:sha512:9K276ed5a8de5d5b3333cac32f33cd10b8136792c98af63dc8ab0369740e349491d6e345b20a35c2e89d5ccdaa85dfb52fea87b2288659756c9b1be860eb477e8c:1789141638")]
	public async Task TypingAStoredHashIsNotThePassword(string stored)
	{
		await Assert.That(Valid(stored, stored)).IsFalse();
	}

	/// <summary>Nor is typing a SharpMUSH hash, which the plaintext fallback must never see.</summary>
	[Test]
	public async Task TypingAModernHashIsNotThePassword()
	{
		var stored = Service.HashPassword(User, Password);

		await Assert.That(Valid(stored)).IsTrue();
		await Assert.That(Valid(stored, stored)).IsFalse();
		await Assert.That(Service.NeedsRehash(stored)).IsFalse();
	}

	private static string PennFormatted(string algorithm, string salt, string password)
	{
		var input = System.Text.Encoding.UTF8.GetBytes(salt + password);
		byte[] digest = algorithm.ToLowerInvariant() switch
		{
			"md5" => System.Security.Cryptography.MD5.HashData(input),
			"sha1" => System.Security.Cryptography.SHA1.HashData(input),
			"sha256" => System.Security.Cryptography.SHA256.HashData(input),
			"sha384" => System.Security.Cryptography.SHA384.HashData(input),
			_ => throw new ArgumentOutOfRangeException(nameof(algorithm))
		};
		return $"2:{algorithm}:{salt}{Convert.ToHexStringLower(digest)}:0";
	}
}
