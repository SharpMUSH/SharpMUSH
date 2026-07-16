using NSubstitute;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Unit tests for <see cref="AccountService"/> using NSubstitute mocks for
/// <see cref="ISharpDatabase"/> and <see cref="IPasswordService"/>.
/// </summary>
public class AccountServiceTests
{
	private static (AccountService Service, ISharpDatabase Db, IPasswordService Passwords, IAccountSessionStore Sessions) Build()
	{
		var (service, db, pw, sessions, _) = BuildWithBanEnforcer(banEnforcer: null);
		return (service, db, pw, sessions);
	}

	/// <summary>
	/// Builds an <see cref="AccountService"/> with an explicit (possibly null) <see cref="IBanEnforcer"/>.
	/// Passing <c>null</c> exercises the optional-dependency path (e.g. a Library-only host without
	/// a Server); passing a substitute lets tests assert the wiring in <c>DisableAccountAsync</c>.
	/// </summary>
	private static (AccountService Service, ISharpDatabase Db, IPasswordService Passwords, IAccountSessionStore Sessions, IBanEnforcer? BanEnforcer)
		BuildWithBanEnforcer(IBanEnforcer? banEnforcer)
	{
		var db = Substitute.For<ISharpDatabase>();
		var pw = Substitute.For<IPasswordService>();
		var sessions = Substitute.For<IAccountSessionStore>();

		// Default no-match/empty results for the login-matrix lookups the login flow now always
		// touches on a password mismatch; individual tests override these where they matter.
		db.GetPlayerByNameOrAliasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Enumerable.Empty<SharpPlayer>().ToAsyncEnumerable());
		db.GetCharactersForAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(new List<SharpPlayer>());

		return (new AccountService(db, pw, sessions, banEnforcer), db, pw, sessions, banEnforcer);
	}

	private static SharpAccount MakeAccount(string id = "accounts/1", string username = "TestUser",
		string? email = null, bool isDisabled = false)
		=> new()
		{
			Id = id,
			Username = username,
			Email = email,
			PasswordHash = "hash",
			CreatedAt = 1_000_000,
			IsDisabled = isDisabled
		};

	[Test]
	public async ValueTask CreateAccount_NewDisplayName_CreatesAndReturnsAccount()
	{
		var (svc, db, pw, _) = Build();

		db.GetAccountByUsernameAsync("Alice", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);
		db.GetAccountByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var created = MakeAccount(username: "Alice");
		db.CreateAccountAsync(Arg.Any<string>(), Arg.Is<string?>(x => x == null), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(created);
		pw.HashPassword(Arg.Any<string>(), Arg.Any<string>()).Returns("real-hash");

		var result = await svc.CreateAccountAsync("Alice", null, "password123");

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Username).IsEqualTo("Alice");
		await db.Received(1).UpdateAccountPasswordAsync("accounts/1", "real-hash", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask CreateAccount_DuplicateDisplayName_ReturnsError()
	{
		var (svc, db, _, _) = Build();

		db.GetAccountByUsernameAsync("Alice", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(username: "Alice"));

		var result = await svc.CreateAccountAsync("Alice", null, "password");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("already taken");
	}

	[Test]
	public async ValueTask CreateAccount_DuplicateEmail_ReturnsError()
	{
		var (svc, db, _, _) = Build();

		db.GetAccountByUsernameAsync("NewUser", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);
		db.GetAccountByEmailAsync("used@example.com", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(email: "used@example.com"));

		var result = await svc.CreateAccountAsync("NewUser", "used@example.com", "password");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("already registered");
	}

	[Test]
	public async ValueTask CreateAccount_NullEmail_SkipsEmailCheck()
	{
		var (svc, db, pw, _) = Build();

		db.GetAccountByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var created = MakeAccount();
		db.CreateAccountAsync(Arg.Any<string>(), Arg.Is<string?>(x => x == null), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(created);
		pw.HashPassword(Arg.Any<string>(), Arg.Any<string>()).Returns("hash");

		var result = await svc.CreateAccountAsync("Bob", null, "pass");

		await Assert.That(result.IsT0).IsTrue();
		await db.DidNotReceive().GetAccountByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask AuthenticateAsync_ValidDisplayNameAndPassword_ReturnsAccount()
	{
		var (svc, db, pw, _) = Build();

		var account = MakeAccount();
		db.GetAccountByUsernameAsync("TestUser", Arg.Any<CancellationToken>()).Returns(account);
		pw.PasswordIsValid(Arg.Any<string>(), "correct", "hash").Returns(true);

		var result = await svc.AuthenticateAsync("TestUser", "correct");

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Username).IsEqualTo("TestUser");
	}

	[Test]
	public async ValueTask AuthenticateAsync_ValidEmail_LooksUpByEmail()
	{
		var (svc, db, pw, _) = Build();

		var account = MakeAccount(email: "user@test.com");
		db.GetAccountByEmailAsync("user@test.com", Arg.Any<CancellationToken>()).Returns(account);
		pw.PasswordIsValid(Arg.Any<string>(), "correct", "hash").Returns(true);

		var result = await svc.AuthenticateAsync("user@test.com", "correct");

		await Assert.That(result).IsNotNull();
		await db.Received(1).GetAccountByEmailAsync("user@test.com", Arg.Any<CancellationToken>());
		await db.DidNotReceive().GetAccountByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask AuthenticateAsync_WrongPassword_ReturnsNull()
	{
		var (svc, db, pw, _) = Build();

		var account = MakeAccount();
		db.GetAccountByUsernameAsync("TestUser", Arg.Any<CancellationToken>()).Returns(account);
		pw.PasswordIsValid(Arg.Any<string>(), "wrong", "hash").Returns(false);

		var result = await svc.AuthenticateAsync("TestUser", "wrong");

		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask AuthenticateAsync_AccountNotFound_ReturnsNull()
	{
		var (svc, db, _, _) = Build();

		db.GetAccountByUsernameAsync("Ghost", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.AuthenticateAsync("Ghost", "pass");

		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask AuthenticateAsync_DisabledAccount_ReturnsNull()
	{
		var (svc, db, pw, _) = Build();

		var disabled = MakeAccount(isDisabled: true);
		db.GetAccountByUsernameAsync("TestUser", Arg.Any<CancellationToken>()).Returns(disabled);
		pw.PasswordIsValid(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);

		var result = await svc.AuthenticateAsync("TestUser", "correct");

		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask ChangePassword_CorrectOldPassword_UpdatesHash()
	{
		var (svc, db, pw, _) = Build();

		var account = MakeAccount();
		db.GetAccountByIdAsync("accounts/1", Arg.Any<CancellationToken>()).Returns(account);
		pw.PasswordIsValid(Arg.Any<string>(), "oldpass", "hash").Returns(true);
		pw.HashPassword(Arg.Any<string>(), "newpass").Returns("new-hash");

		var result = await svc.ChangePasswordAsync("accounts/1", "oldpass", "newpass");

		await Assert.That(result.IsT0).IsTrue();
		await db.Received(1).UpdateAccountPasswordAsync("accounts/1", "new-hash", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask ChangePassword_WrongOldPassword_ReturnsError()
	{
		var (svc, db, pw, _) = Build();

		var account = MakeAccount();
		db.GetAccountByIdAsync("accounts/1", Arg.Any<CancellationToken>()).Returns(account);
		pw.PasswordIsValid(Arg.Any<string>(), "wrong", "hash").Returns(false);

		var result = await svc.ChangePasswordAsync("accounts/1", "wrong", "newpass");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("incorrect");
	}

	[Test]
	public async ValueTask ChangePassword_AccountNotFound_ReturnsError()
	{
		var (svc, db, _, _) = Build();

		db.GetAccountByIdAsync("accounts/ghost", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.ChangePasswordAsync("accounts/ghost", "old", "new");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("not found");
	}

	[Test]
	public async ValueTask ChangeEmail_ValidPassword_NewEmailSet()
	{
		var (svc, db, pw, _) = Build();

		var account = MakeAccount();
		db.GetAccountByIdAsync("accounts/1", Arg.Any<CancellationToken>()).Returns(account);
		pw.PasswordIsValid(Arg.Any<string>(), "pass", "hash").Returns(true);
		db.GetAccountByEmailAsync("new@test.com", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.ChangeEmailAsync("accounts/1", "new@test.com", "pass");

		await Assert.That(result.IsT0).IsTrue();
		await db.Received(1).UpdateAccountEmailAsync("accounts/1", "new@test.com", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask ChangeEmail_DuplicateEmail_ReturnsError()
	{
		var (svc, db, pw, _) = Build();

		var account = MakeAccount();
		db.GetAccountByIdAsync("accounts/1", Arg.Any<CancellationToken>()).Returns(account);
		pw.PasswordIsValid(Arg.Any<string>(), "pass", "hash").Returns(true);
		db.GetAccountByEmailAsync("taken@test.com", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(id: "accounts/99", email: "taken@test.com"));

		var result = await svc.ChangeEmailAsync("accounts/1", "taken@test.com", "pass");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("already registered");
	}

	[Test]
	public async ValueTask ChangeEmail_NullEmail_ClearsEmail()
	{
		var (svc, db, pw, _) = Build();

		var account = MakeAccount(email: "old@test.com");
		db.GetAccountByIdAsync("accounts/1", Arg.Any<CancellationToken>()).Returns(account);
		pw.PasswordIsValid(Arg.Any<string>(), "pass", "hash").Returns(true);

		var result = await svc.ChangeEmailAsync("accounts/1", null, "pass");

		await Assert.That(result.IsT0).IsTrue();
		await db.Received(1).UpdateAccountEmailAsync("accounts/1", null, Arg.Any<CancellationToken>());
		await db.DidNotReceive().GetAccountByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask ChangeDisplayName_Unique_UpdatesName()
	{
		var (svc, db, _, _) = Build();

		db.GetAccountByUsernameAsync("NewName", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.ChangeUsernameAsync("accounts/1", "NewName");

		await Assert.That(result.IsT0).IsTrue();
		await db.Received(1).UpdateAccountUsernameAsync("accounts/1", "NewName", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask ChangeDisplayName_AlreadyTaken_ReturnsError()
	{
		var (svc, db, _, _) = Build();

		db.GetAccountByUsernameAsync("Taken", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(id: "accounts/99", username: "Taken"));

		var result = await svc.ChangeUsernameAsync("accounts/1", "Taken");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("already taken");
	}

	[Test]
	public async ValueTask DisplayNameExists_WhenPresent_ReturnsTrue()
	{
		var (svc, db, _, _) = Build();

		db.GetAccountByUsernameAsync("Alice", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(username: "Alice"));

		var result = await svc.UsernameExistsAsync("Alice");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async ValueTask DisplayNameExists_WhenAbsent_ReturnsFalse()
	{
		var (svc, db, _, _) = Build();

		db.GetAccountByUsernameAsync("Ghost", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.UsernameExistsAsync("Ghost");

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async ValueTask EmailExists_WhenPresent_ReturnsTrue()
	{
		var (svc, db, _, _) = Build();

		db.GetAccountByEmailAsync("a@b.com", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(email: "a@b.com"));

		var result = await svc.EmailExistsAsync("a@b.com");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async ValueTask EmailExists_WhenAbsent_ReturnsFalse()
	{
		var (svc, db, _, _) = Build();

		db.GetAccountByEmailAsync("nope@b.com", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.EmailExistsAsync("nope@b.com");

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async ValueTask DeleteAccountAsync_ForwardsToDatabase()
	{
		var (svc, db, _, _) = Build();

		db.DeleteAccountAsync("accounts/1", Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);

		await svc.DeleteAccountAsync("accounts/1");

		await db.Received(1).DeleteAccountAsync("accounts/1", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask DisableAccountAsync_AccountNotFound_ReturnsError()
	{
		var (svc, db, _, _) = Build();

		db.GetAccountByIdAsync("accounts/ghost", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.DisableAccountAsync("accounts/ghost");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("not found");
	}

	[Test]
	public async ValueTask DisableAccountAsync_AccountFound_DisablesAndRevokesSessions()
	{
		var (svc, db, _, sessions) = Build();

		db.GetAccountByIdAsync("accounts/1", Arg.Any<CancellationToken>()).Returns(MakeAccount());

		var result = await svc.DisableAccountAsync("accounts/1");

		await Assert.That(result.IsT0).IsTrue();
		await db.Received(1).UpdateAccountDisabledAsync("accounts/1", true, Arg.Any<CancellationToken>());
		await sessions.Received(1).RevokeAllForAccountAsync("accounts/1", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask DisableAccountAsync_NoBanEnforcerWired_StillDisablesAndRevokesSessions()
	{
		// Library-only hosts (no SharpMUSH.Server) never wire IBanEnforcer; DisableAccountAsync must
		// still work, using RevokeAllForAccountAsync alone as the floor.
		var (svc, db, _, sessions, _) = BuildWithBanEnforcer(banEnforcer: null);
		db.GetAccountByIdAsync("accounts/1", Arg.Any<CancellationToken>()).Returns(MakeAccount());

		var result = await svc.DisableAccountAsync("accounts/1");

		await Assert.That(result.IsT0).IsTrue();
		await db.Received(1).UpdateAccountDisabledAsync("accounts/1", true, Arg.Any<CancellationToken>());
		await sessions.Received(1).RevokeAllForAccountAsync("accounts/1", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask DisableAccountAsync_BanEnforcerWired_InvokesEnforceAccountBanAsync()
	{
		var banEnforcer = Substitute.For<IBanEnforcer>();
		var (svc, db, _, sessions, _) = BuildWithBanEnforcer(banEnforcer);
		db.GetAccountByIdAsync("accounts/1", Arg.Any<CancellationToken>()).Returns(MakeAccount());

		var result = await svc.DisableAccountAsync("accounts/1");

		await Assert.That(result.IsT0).IsTrue();
		// The session revoke floor still runs...
		await sessions.Received(1).RevokeAllForAccountAsync("accounts/1", Arg.Any<CancellationToken>());
		// ...and the wired enforcer is additionally invoked with the same account id.
		await banEnforcer.Received(1).EnforceAccountBanAsync("accounts/1", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask DisableAccountAsync_AccountNotFound_DoesNotInvokeBanEnforcer()
	{
		var banEnforcer = Substitute.For<IBanEnforcer>();
		var (svc, db, _, _, _) = BuildWithBanEnforcer(banEnforcer);
		db.GetAccountByIdAsync("accounts/ghost", Arg.Any<CancellationToken>()).Returns((SharpAccount?)null);

		var result = await svc.DisableAccountAsync("accounts/ghost");

		await Assert.That(result.IsT1).IsTrue();
		await banEnforcer.DidNotReceive().EnforceAccountBanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask EnableAccountAsync_AccountNotFound_ReturnsError()
	{
		var (svc, db, _, _) = Build();

		db.GetAccountByIdAsync("accounts/ghost", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.EnableAccountAsync("accounts/ghost");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("not found");
	}

	[Test]
	public async ValueTask EnableAccountAsync_AccountFound_ClearsDisabledFlag()
	{
		var (svc, db, _, _) = Build();

		db.GetAccountByIdAsync("accounts/1", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(isDisabled: true));

		var result = await svc.EnableAccountAsync("accounts/1");

		await Assert.That(result.IsT0).IsTrue();
		await db.Received(1).UpdateAccountDisabledAsync("accounts/1", false, Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask SetPasswordAsync_AccountNotFound_ReturnsError()
	{
		var (svc, db, _, _) = Build();

		db.GetAccountByIdAsync("accounts/ghost", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.SetPasswordAsync("accounts/ghost", "newpass", false);

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("not found");
	}

	[Test]
	public async ValueTask SetPasswordAsync_AccountFound_SetsHashAndMustChangeFlag()
	{
		var (svc, db, pw, _) = Build();

		db.GetAccountByIdAsync("accounts/1", Arg.Any<CancellationToken>()).Returns(MakeAccount());
		pw.HashPassword(Arg.Any<string>(), "newpass").Returns("new-hash");

		var result = await svc.SetPasswordAsync("accounts/1", "newpass", true);

		await Assert.That(result.IsT0).IsTrue();
		await db.Received(1).UpdateAccountPasswordAsync("accounts/1", "new-hash", Arg.Any<CancellationToken>());
		await db.Received(1).UpdateAccountMustChangePasswordAsync("accounts/1", true, Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask CreateUnclaimedAccountAsync_CreatesAccountWithEmptyHash()
	{
		var (svc, db, _, _) = Build();

		var created = MakeAccount(username: "unclaimed-admin");
		db.CreateAccountAsync("unclaimed-admin", null, string.Empty, Arg.Any<CancellationToken>())
			.Returns(created);

		var result = await svc.CreateUnclaimedAccountAsync("unclaimed-admin");

		await Assert.That(result.Username).IsEqualTo("unclaimed-admin");
		await db.Received(1).CreateAccountAsync("unclaimed-admin", null, string.Empty, Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask GetAllAccountsAsync_ForwardsToDatabase()
	{
		var (svc, db, _, _) = Build();

		var accounts = new List<SharpAccount> { MakeAccount() };
		db.GetAllAccountsAsync(Arg.Any<CancellationToken>()).Returns(accounts);

		var result = await svc.GetAllAccountsAsync();

		await Assert.That(result.Count).IsEqualTo(1);
		await db.Received(1).GetAllAccountsAsync(Arg.Any<CancellationToken>());
	}
}
