using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The account lifecycle: <see cref="AccountStatus"/> gates authentication, transitions away from
/// Active revoke live access, and the reserved system account is exempt from status changes.
/// </summary>
public class AccountStatusTests
{
	private static (AccountService Service, ISharpDatabase Db, IPasswordService Passwords, IAccountSessionStore Sessions) Build()
	{
		var db = Substitute.For<ISharpDatabase>();
		var pw = Substitute.For<IPasswordService>();
		var sessions = Substitute.For<IAccountSessionStore>();

		db.GetPlayerByNameOrAliasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Enumerable.Empty<SharpPlayer>().ToAsyncEnumerable());
		db.GetCharactersForAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(new List<SharpPlayer>());

		return (new AccountService(db, pw, sessions), db, pw, sessions);
	}

	private static SharpAccount MakeAccount(AccountStatus status = AccountStatus.Active, string username = "TestUser") => new()
	{
		Id = "node_accounts/1",
		Username = username,
		PasswordHash = "hash",
		CreatedAt = 1_000_000,
		Status = status
	};

	[Test]
	[Arguments(null, AccountStatus.Active)]
	[Arguments("", AccountStatus.Active)]
	[Arguments("Active", AccountStatus.Active)]
	[Arguments("Disabled", AccountStatus.Disabled)]
	[Arguments("Closed", AccountStatus.Closed)]
	[Arguments("Deleted", AccountStatus.Deleted)]
	[Arguments("Banished", AccountStatus.Disabled)]
	[Arguments("garbage", AccountStatus.Disabled)]
	[Arguments("   ", AccountStatus.Active)]
	[Arguments("  Closed  ", AccountStatus.Closed)]
	[Arguments("active", AccountStatus.Active)]
	[Arguments("CLOSED", AccountStatus.Closed)]
	[Arguments("42", AccountStatus.Disabled)]
	[Arguments("-1", AccountStatus.Disabled)]
	[Arguments("0", AccountStatus.Disabled)]
	public async ValueTask ParseStatus_MissingIsActive_UnparseableFailsClosed(string? stored, AccountStatus expected)
	{
		await Assert.That(AccountStatusParser.Parse(stored)).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask NewAccount_DefaultsToActive()
	{
		var account = new SharpAccount { Username = "Fresh", PasswordHash = "hash" };

		await Assert.That(account.Status).IsEqualTo(AccountStatus.Active);
		await Assert.That(account.IsActive).IsTrue();
	}

	[Test]
	[Arguments(AccountStatus.Disabled)]
	[Arguments(AccountStatus.Closed)]
	[Arguments(AccountStatus.Deleted)]
	public async ValueTask Authenticate_NonActiveStatus_ReturnsNull(AccountStatus status)
	{
		var (svc, db, pw, _) = Build();
		db.GetAccountByUsernameAsync("TestUser", Arg.Any<CancellationToken>()).Returns(MakeAccount(status));
		pw.PasswordIsValid(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);

		await Assert.That(await svc.AuthenticateAsync("TestUser", "correct-password")).IsNull();
	}

	[Test]
	public async ValueTask Authenticate_ActiveStatus_ReturnsAccount()
	{
		var (svc, db, pw, _) = Build();
		db.GetAccountByUsernameAsync("TestUser", Arg.Any<CancellationToken>()).Returns(MakeAccount());
		pw.PasswordIsValid(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);

		var result = await svc.AuthenticateAsync("TestUser", "correct-password");

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Username).IsEqualTo("TestUser");
	}

	[Test]
	[Arguments(AccountStatus.Disabled)]
	[Arguments(AccountStatus.Closed)]
	[Arguments(AccountStatus.Deleted)]
	public async ValueTask SetAccountStatus_LeavingActive_PersistsAndRevokesSessions(AccountStatus status)
	{
		var (svc, db, _, sessions) = Build();
		db.GetAccountByIdAsync("node_accounts/1", Arg.Any<CancellationToken>()).Returns(MakeAccount());

		var result = await svc.SetAccountStatusAsync("node_accounts/1", status);

		await Assert.That(result.IsT0).IsTrue();
		await db.Received(1).UpdateAccountStatusAsync("node_accounts/1", status, Arg.Any<CancellationToken>());
		await sessions.Received(1).RevokeAllForAccountAsync("node_accounts/1", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask SetAccountStatus_Active_DoesNotRevokeSessions()
	{
		var (svc, db, _, sessions) = Build();
		db.GetAccountByIdAsync("node_accounts/1", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(AccountStatus.Closed));

		var result = await svc.SetAccountStatusAsync("node_accounts/1", AccountStatus.Active);

		await Assert.That(result.IsT0).IsTrue();
		await sessions.DidNotReceive().RevokeAllForAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask SetAccountStatus_UnknownAccount_ReturnsError()
	{
		var (svc, db, _, _) = Build();
		db.GetAccountByIdAsync("node_accounts/404", Arg.Any<CancellationToken>()).Returns((SharpAccount?)null);

		var result = await svc.SetAccountStatusAsync("node_accounts/404", AccountStatus.Closed);

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).IsEqualTo("Account not found.");
	}

	[Test]
	public async ValueTask CloseAndMarkDeleted_SetTheirRespectiveStatuses()
	{
		var (svc, db, _, _) = Build();
		db.GetAccountByIdAsync("node_accounts/1", Arg.Any<CancellationToken>()).Returns(MakeAccount());

		await svc.CloseAccountAsync("node_accounts/1");
		await svc.MarkAccountDeletedAsync("node_accounts/1");

		await db.Received(1).UpdateAccountStatusAsync("node_accounts/1", AccountStatus.Closed, Arg.Any<CancellationToken>());
		await db.Received(1).UpdateAccountStatusAsync("node_accounts/1", AccountStatus.Deleted, Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask SetAccountStatus_SystemAccount_ReturnsErrorWithoutPersisting()
	{
		var (svc, db, _, _) = Build();
		db.GetAccountByIdAsync("node_accounts/9", Arg.Any<CancellationToken>())
			.Returns(MakeAccount(username: SystemAccount.Username));

		var result = await svc.SetAccountStatusAsync("node_accounts/9", AccountStatus.Closed);

		await Assert.That(result.IsT1).IsTrue();
		await db.DidNotReceive().UpdateAccountStatusAsync(
			Arg.Any<string>(), Arg.Any<AccountStatus>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask CreateAccount_ReservedSystemUsername_ReturnsErrorWithoutCreating()
	{
		var (svc, db, _, _) = Build();
		db.GetAccountByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SharpAccount?)null);
		db.GetAccountByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SharpAccount?)null);

		var result = await svc.CreateAccountAsync(SystemAccount.Username.ToUpperInvariant(), null, "password123");

		await Assert.That(result.IsT1).IsTrue();
		await db.DidNotReceive().CreateAccountAsync(
			Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask GetOrCreateSystemAccount_IsIdempotent()
	{
		var (svc, db, _, _) = Build();
		db.GetAccountByUsernameAsync(SystemAccount.Username, Arg.Any<CancellationToken>())
			.Returns(MakeAccount(username: SystemAccount.Username));

		var result = await svc.GetOrCreateSystemAccountAsync();

		await Assert.That(result.Username).IsEqualTo(SystemAccount.Username);
		await db.DidNotReceive().CreateAccountAsync(
			Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}
}
