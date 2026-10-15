using System.Text;
using Mediator;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Passwords;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Every password form a TinyMUX game stores verifies, and every one is rehashed on the first login.
/// </summary>
/// <remarks>
/// The <c>crypt(3)</c> vectors are <c>perl -e 'print crypt($ARGV[0], $ARGV[1])'</c> against glibc's
/// libxcrypt, which implements MD5-crypt and BSDi extended DES as TinyMUX's <c>crypt</c> call does.
/// </remarks>
public class TinyMuxPasswordTests
{
	private static readonly PasswordService Service = new(Substitute.For<IMediator>(), new PasswordHasher<string>());

	[Test]
	[Arguments("hunter2", "saltsalt", "$1$saltsalt$ZliGyAN3DciDHEkDboonh/")]
	[Arguments("", "abc", "$1$abc$Or2rbeUYTvt12aiVzMuS/.")]
	[Arguments("a very long password that runs past sixteen bytes", "12345678", "$1$12345678$bgI4J7Gr38D2K9aeZgJ9z1")]
	[Arguments("Pässwörd", "xy", "$1$xy$54Pih75ZjETAlDSuflaq71")]
	public async Task Md5CryptMatchesLibxcrypt(string password, string salt, string expected)
	{
		await Assert.That(Md5Crypt.Crypt(Encoding.UTF8.GetBytes(password), Encoding.ASCII.GetBytes(salt))).IsEqualTo(expected);
	}

	[Test]
	[Arguments("U*U*U*U*", "_J9..CCCC", "_J9..CCCCXBrJUJV154M")]
	[Arguments("longpassword12345", "_K1..crys", "_K1..crysgJbph421zyI")]
	[Arguments("", "_J9..SDiz", "_J9..SDizUsxsXqYns0c")]
	[Arguments("a", "_J9..SDiz", "_J9..SDizDN3nWgZLEX6")]
	[Arguments("Pässwörd", "_J9..SDiz", "_J9..SDizMiQApygig0I")]
	[Arguments("exactly8", "_..../...", "_..../...V2mBWHI4oQE")]
	public async Task ExtendedDesMatchesLibxcrypt(string password, string setting, string expected)
	{
		await Assert.That(DesCrypt.ExtendedCrypt(Encoding.UTF8.GetBytes(password), setting)).IsEqualTo(expected);
	}

	/// <summary>
	/// Each form TinyMUX writes: its own salted SHA-1, SHA-crypt, MD5-crypt, DES with "XX" or a random
	/// salt (<c>ENABLE_PROPER_DES</c>), extended DES, and a PennMUSH hash it carried over, once the
	/// reader has taken off TinyMUX's <c>$P6H$$</c>.
	/// </summary>
	[Test]
	[Arguments("$SHA1$c2FsdHNhbHQ=$u4A+CitIZ6Lu5DfqC2ZMpNK5rwQ=", "hunter2")]
	[Arguments("$6$saltsalt$8iYtNHxjWRl.NF6oNZ5tF.iKFlQREaXBLlSmZKP6dy9l5z3vsooWNW0/GZ6Nej73/TFug6pIPSqbJoCT6dfnj.", "hunter2")]
	[Arguments("$5$rounds=5000$saltsalt$OIdfjX.u4Y3SJ4I2bX8w5BMf1VAUhHABNUirScDzZi3", "hunter2")]
	[Arguments("$1$saltsalt$ZliGyAN3DciDHEkDboonh/", "hunter2")]
	[Arguments("XXtxz3x8n56Ig", "despass")]
	[Arguments("ab2RmhfeGz9J6", "despass")]
	[Arguments("_J9..CCCCXBrJUJV154M", "U*U*U*U*")]
	[Arguments("1:sha1:f3bbbd66a63d4bf1747940578ec3d0103530e21d:0", "hunter2")]
	[Arguments("cleartext", "cleartext")]
	public async Task EveryTinyMuxFormVerifiesAndIsRehashed(string stored, string password)
	{
		await Assert.That(Service.PasswordIsValid(password, stored)).IsTrue();
		await Assert.That(Service.PasswordIsValid("wrongpass", stored)).IsFalse();
		await Assert.That(Service.NeedsRehash(stored)).IsTrue();
	}

	/// <summary>Knowing an extended DES hash is not knowing the password: it is never compared as plaintext.</summary>
	[Test]
	public async Task AnExtendedDesHashIsNotItsOwnPassword()
	{
		await Assert.That(Service.PasswordIsValid("_J9..CCCCXBrJUJV154M", "_J9..CCCCXBrJUJV154M")).IsFalse();
	}
}
