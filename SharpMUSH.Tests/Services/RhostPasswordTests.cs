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

	/// <summary>bcrypt is not checked: no password opens it, and the import reports the players who have one.</summary>
	[Test]
	public async Task BcryptOpensNothing()
	{
		await Assert.That(Service.PasswordIsValid("hunter2", "$2a$10$abcdefghijklmnopqrstuuABCDEFGHIJKLMNOPQRSTUVWXYZ01234")).IsFalse();
	}
}
