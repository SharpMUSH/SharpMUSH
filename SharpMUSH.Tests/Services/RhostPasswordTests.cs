using System.Text;
using Mediator;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Passwords;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// MD5-crypt, <c>$1$</c>: the vectors are <c>openssl passwd -1 -salt &lt;salt&gt; &lt;password&gt;</c> (OpenSSL 3).
/// </summary>
public class Md5CryptTests
{
	[Test]
	[Arguments("password", "saltsalt", "$1$saltsalt$qjXMvbEw8oaL.CzflDtaK/")]
	[Arguments("hunter2", "abc", "$1$abc$9dV135Rc3U8Xbm2DxadgF0")]
	[Arguments("", "12345678", "$1$12345678$xek.CpjQUVgdf/P2N9KQf/")]
	public async Task MatchesOpenSsl(string password, string salt, string expected)
	{
		await Assert.That(Md5Crypt.Crypt(Encoding.UTF8.GetBytes(password), Encoding.ASCII.GetBytes(salt))).IsEqualTo(expected);
		await Assert.That(Md5Crypt.Verify(expected, Encoding.UTF8.GetBytes(password))).IsTrue();
		await Assert.That(Md5Crypt.Verify(expected, "wrong"u8)).IsFalse();
	}
}

/// <summary>
/// Every password form a RhostMUSH flatfile can hold: the import keeps it as stored, it opens the
/// character, and the first login rehashes it.
/// </summary>
public class RhostPasswordTests
{
	private static readonly PasswordService Service = new(Substitute.For<IMediator>(), new PasswordHasher<string>());

	/// <summary>Each is the password <c>hunter2</c>.</summary>
	[Test]
	[Arguments("$6$rhostsalt$REIwg5d9u9G3VHywf7a6fsI5ld6Ie3DrAxT/0MXAeS6SK65XAP2BpqsTi0DQXGDCtn.K/DnFoKPql4aialxnG0", "SHA-512-crypt")]
	[Arguments("$1$abc$9dV135Rc3U8Xbm2DxadgF0", "MD5-crypt")]
	[Arguments("XXuOowDRD5rnE", "DES crypt with RhostMUSH's fixed salt")]
	[Arguments("hunter2", "plaintext")]
	[Arguments("$2a$04$QA5jKFwHjzY9RyNrJR0uIOYeVNJpmGBiVJn2Q2AgFQNRq2QFP4.Va", "bcrypt $2a$")]
	[Arguments("$2b$04$z/R2OUmvmWagltsnP81p8e8LHDckcUaw/TlG411CTfxZNC0do.De2", "bcrypt $2b$")]
	[Arguments("$2y$04$z/R2OUmvmWagltsnP81p8e8LHDckcUaw/TlG411CTfxZNC0do.De2", "bcrypt $2y$, glibc's spelling of $2b$")]
	public async Task AStoredFormOpensTheCharacterAndIsRehashed(string stored, string form)
	{
		await Assert.That(Service.PasswordIsValid("hunter2", stored)).IsTrue().Because(form);
		await Assert.That(Service.PasswordIsValid("hunter3", stored)).IsFalse().Because(form);
		await Assert.That(Service.NeedsRehash(stored)).IsTrue().Because(form);
	}

	/// <summary>SHS, RhostMUSH's old digest: <c>XX</c> and two decimal words of SHA-0, as PennMUSH writes them.</summary>
	[Test]
	public async Task AnShsPasswordOpensTheCharacterAndIsRehashed()
	{
		var digest = Sha0.HashData("hunter2"u8.ToArray());
		var stored = $"XX{BitConverter.ToUInt32(digest, 0)}{BitConverter.ToUInt32(digest, 4)}";

		await Assert.That(Service.PasswordIsValid("hunter2", stored)).IsTrue();
		await Assert.That(Service.NeedsRehash(stored)).IsTrue();
	}

	/// <summary>A published bcrypt vector (OpenBSD's, as John the Ripper carries it), so the check is not only against itself.</summary>
	[Test]
	public async Task BcryptMatchesAPublishedVector()
	{
		await Assert.That(Service.PasswordIsValid("U*U", "$2a$05$CCCCCCCCCCCCCCCCCCCCC.E5YPO9kmyuRGyh0XouQYb4YMJKvyOeW")).IsTrue();
	}

	/// <summary>A value shaped like bcrypt whose salt does not decode opens nothing and does not throw.</summary>
	[Test]
	public async Task AMalformedBcryptOpensNothing()
	{
		await Assert.That(Service.PasswordIsValid("hunter2", "$2a$10$abcdefghijklmnopqrstuuABCDEFGHIJKLMNOPQRSTUVWXYZ01234")).IsFalse();
		await Assert.That(Service.PasswordIsValid("hunter2", "$2a$99$abcdefghijklmnopqrstuuABCDEFGHIJKLMNOPQRSTUVWXYZ01234")).IsFalse();
	}
}
