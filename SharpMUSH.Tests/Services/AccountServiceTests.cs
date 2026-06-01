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
	// ── Helpers ───────────────────────────────────────────────────────────────

	private static (AccountService Service, ISharpDatabase Db, IPasswordService Passwords) Build()
	{
		var db = Substitute.For<ISharpDatabase>();
		var pw = Substitute.For<IPasswordService>();
		return (new AccountService(db, pw), db, pw);
	}

	private static SharpAccount MakeAccount(string id = "accounts/1", string displayName = "TestUser",
		string? email = null, bool isDisabled = false)
		=> new()
		{
			Id = id,
			DisplayName = displayName,
			Email = email,
			PasswordHash = "hash",
			CreatedAt = 1_000_000,
			IsDisabled = isDisabled
		};

	// ── CreateAccountAsync ────────────────────────────────────────────────────

	[Test]
	public async ValueTask CreateAccount_NewDisplayName_CreatesAndReturnsAccount()
	{
		var (svc, db, pw) = Build();

		db.GetAccountByDisplayNameAsync("Alice", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);
		db.GetAccountByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var created = MakeAccount(displayName: "Alice");
		db.CreateAccountAsync(Arg.Any<string>(), Arg.Is<string?>(x => x == null), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(created);
		pw.HashPassword(Arg.Any<string>(), Arg.Any<string>()).Returns("real-hash");

		var result = await svc.CreateAccountAsync("Alice", null, "password123");

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.DisplayName).IsEqualTo("Alice");
		await db.Received(1).UpdateAccountPasswordAsync("accounts/1", "real-hash", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask CreateAccount_DuplicateDisplayName_ReturnsError()
	{
		var (svc, db, _) = Build();

		db.GetAccountByDisplayNameAsync("Alice", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(displayName: "Alice"));

		var result = await svc.CreateAccountAsync("Alice", null, "password");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("already taken");
	}

	[Test]
	public async ValueTask CreateAccount_DuplicateEmail_ReturnsError()
	{
		var (svc, db, _) = Build();

		db.GetAccountByDisplayNameAsync("NewUser", Arg.Any<CancellationToken>())
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
		var (svc, db, pw) = Build();

		db.GetAccountByDisplayNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var created = MakeAccount();
		db.CreateAccountAsync(Arg.Any<string>(), Arg.Is<string?>(x => x == null), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(created);
		pw.HashPassword(Arg.Any<string>(), Arg.Any<string>()).Returns("hash");

		var result = await svc.CreateAccountAsync("Bob", null, "pass");

		await Assert.That(result.IsT0).IsTrue();
		// Email check should never have been called
		await db.DidNotReceive().GetAccountByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	// ── AuthenticateAsync ─────────────────────────────────────────────────────

	[Test]
	public async ValueTask AuthenticateAsync_ValidDisplayNameAndPassword_ReturnsAccount()
	{
		var (svc, db, pw) = Build();

		var account = MakeAccount();
		db.GetAccountByDisplayNameAsync("TestUser", Arg.Any<CancellationToken>()).Returns(account);
		pw.PasswordIsValid(Arg.Any<string>(), "correct", "hash").Returns(true);

		var result = await svc.AuthenticateAsync("TestUser", "correct");

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.DisplayName).IsEqualTo("TestUser");
	}

	[Test]
	public async ValueTask AuthenticateAsync_ValidEmail_LooksUpByEmail()
	{
		var (svc, db, pw) = Build();

		var account = MakeAccount(email: "user@test.com");
		db.GetAccountByEmailAsync("user@test.com", Arg.Any<CancellationToken>()).Returns(account);
		pw.PasswordIsValid(Arg.Any<string>(), "correct", "hash").Returns(true);

		var result = await svc.AuthenticateAsync("user@test.com", "correct");

		await Assert.That(result).IsNotNull();
		await db.Received(1).GetAccountByEmailAsync("user@test.com", Arg.Any<CancellationToken>());
		await db.DidNotReceive().GetAccountByDisplayNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask AuthenticateAsync_WrongPassword_ReturnsNull()
	{
		var (svc, db, pw) = Build();

		var account = MakeAccount();
		db.GetAccountByDisplayNameAsync("TestUser", Arg.Any<CancellationToken>()).Returns(account);
		pw.PasswordIsValid(Arg.Any<string>(), "wrong", "hash").Returns(false);

		var result = await svc.AuthenticateAsync("TestUser", "wrong");

		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask AuthenticateAsync_AccountNotFound_ReturnsNull()
	{
		var (svc, db, _) = Build();

		db.GetAccountByDisplayNameAsync("Ghost", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.AuthenticateAsync("Ghost", "pass");

		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask AuthenticateAsync_DisabledAccount_ReturnsNull()
	{
		var (svc, db, pw) = Build();

		var disabled = MakeAccount(isDisabled: true);
		db.GetAccountByDisplayNameAsync("TestUser", Arg.Any<CancellationToken>()).Returns(disabled);
		pw.PasswordIsValid(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);

		var result = await svc.AuthenticateAsync("TestUser", "correct");

		await Assert.That(result).IsNull();
	}

	// ── ChangePasswordAsync ───────────────────────────────────────────────────

	[Test]
	public async ValueTask ChangePassword_CorrectOldPassword_UpdatesHash()
	{
		var (svc, db, pw) = Build();

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
		var (svc, db, pw) = Build();

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
		var (svc, db, _) = Build();

		db.GetAccountByIdAsync("accounts/ghost", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.ChangePasswordAsync("accounts/ghost", "old", "new");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("not found");
	}

	// ── ChangeEmailAsync ──────────────────────────────────────────────────────

	[Test]
	public async ValueTask ChangeEmail_ValidPassword_NewEmailSet()
	{
		var (svc, db, pw) = Build();

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
		var (svc, db, pw) = Build();

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
		var (svc, db, pw) = Build();

		var account = MakeAccount(email: "old@test.com");
		db.GetAccountByIdAsync("accounts/1", Arg.Any<CancellationToken>()).Returns(account);
		pw.PasswordIsValid(Arg.Any<string>(), "pass", "hash").Returns(true);

		var result = await svc.ChangeEmailAsync("accounts/1", null, "pass");

		await Assert.That(result.IsT0).IsTrue();
		await db.Received(1).UpdateAccountEmailAsync("accounts/1", null, Arg.Any<CancellationToken>());
		// Should not check whether null email is already taken
		await db.DidNotReceive().GetAccountByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	// ── ChangeDisplayNameAsync ────────────────────────────────────────────────

	[Test]
	public async ValueTask ChangeDisplayName_Unique_UpdatesName()
	{
		var (svc, db, _) = Build();

		db.GetAccountByDisplayNameAsync("NewName", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.ChangeDisplayNameAsync("accounts/1", "NewName");

		await Assert.That(result.IsT0).IsTrue();
		await db.Received(1).UpdateAccountDisplayNameAsync("accounts/1", "NewName", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask ChangeDisplayName_AlreadyTaken_ReturnsError()
	{
		var (svc, db, _) = Build();

		db.GetAccountByDisplayNameAsync("Taken", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(id: "accounts/99", displayName: "Taken"));

		var result = await svc.ChangeDisplayNameAsync("accounts/1", "Taken");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("already taken");
	}

	// ── DisplayNameExistsAsync / EmailExistsAsync ─────────────────────────────

	[Test]
	public async ValueTask DisplayNameExists_WhenPresent_ReturnsTrue()
	{
		var (svc, db, _) = Build();

		db.GetAccountByDisplayNameAsync("Alice", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(displayName: "Alice"));

		var result = await svc.DisplayNameExistsAsync("Alice");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async ValueTask DisplayNameExists_WhenAbsent_ReturnsFalse()
	{
		var (svc, db, _) = Build();

		db.GetAccountByDisplayNameAsync("Ghost", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.DisplayNameExistsAsync("Ghost");

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async ValueTask EmailExists_WhenPresent_ReturnsTrue()
	{
		var (svc, db, _) = Build();

		db.GetAccountByEmailAsync("a@b.com", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(email: "a@b.com"));

		var result = await svc.EmailExistsAsync("a@b.com");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async ValueTask EmailExists_WhenAbsent_ReturnsFalse()
	{
		var (svc, db, _) = Build();

		db.GetAccountByEmailAsync("nope@b.com", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.EmailExistsAsync("nope@b.com");

		await Assert.That(result).IsFalse();
	}

	// ── DeleteAccountAsync ────────────────────────────────────────────────────

	[Test]
	public async ValueTask DeleteAccountAsync_ForwardsToDatabase()
	{
		var (svc, db, _) = Build();

		db.DeleteAccountAsync("accounts/1", Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);

		await svc.DeleteAccountAsync("accounts/1");

		await db.Received(1).DeleteAccountAsync("accounts/1", Arg.Any<CancellationToken>());
	}

	// ── DisableAccountAsync ───────────────────────────────────────────────────

	[Test]
	public async ValueTask DisableAccountAsync_AccountNotFound_ReturnsError()
	{
		var (svc, db, _) = Build();

		db.GetAccountByIdAsync("accounts/ghost", Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var result = await svc.DisableAccountAsync("accounts/ghost");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("not found");
	}

	[Test]
	public async ValueTask DisableAccountAsync_NotYetImplemented_ReturnsError()
	{
		var (svc, db, _) = Build();

		db.GetAccountByIdAsync("accounts/1", Arg.Any<CancellationToken>()).Returns(MakeAccount());

		var result = await svc.DisableAccountAsync("accounts/1");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("not yet implemented");
	}
}
