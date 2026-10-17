using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Tests.Authentication.External;

public class AccountRecentProofServiceTests
{
	private static (AccountRecentProofService Service, IAccountService Accounts, IAccountSessionStore Sessions,
		IPasswordService Passwords, SharpAccount Account) Fixture()
	{
		var accounts = Substitute.For<IAccountService>();
		var sessions = Substitute.For<IAccountSessionStore>();
		var passwords = Substitute.For<IPasswordService>();
		var account = new SharpAccount { Id = "account", Username = "Owner", PasswordHash = "account-hash", CredentialEpoch = 4 };
		accounts.GetByIdAsync("account", Arg.Any<CancellationToken>()).Returns(account);
		sessions.InspectAsync("session", Arg.Any<CancellationToken>()).Returns(new Found<IAccountSessionStore.SessionIdentity>(
			new IAccountSessionStore.SessionIdentity("account", null, null)));
		passwords.PasswordIsValid("account-password", "account-hash").Returns(true);
		return (new AccountRecentProofService(accounts, sessions, passwords, new RecentAccountProofStore(TimeProvider.System), new AccountCredentialGate()),
			accounts, sessions, passwords, account);
	}

	[Test]
	public async Task DedicatedPasswordProofNeverAcceptsCharacterFallbackOrAbsentHash()
	{
		var (service, accounts, _, passwords, account) = Fixture();
		accounts.AuthenticateAsync("Owner", "character-password", Arg.Any<CancellationToken>()).Returns(new AccountSignIn(account));
		(await service.PasswordAsync("account", "session", "character-password", AccountSecurityOperation.LinkIdentity)).Expect<Error<string>>();
		account.PasswordHash = null;
		passwords.PasswordIsValid(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		(await service.PasswordAsync("account", "session", "anything", AccountSecurityOperation.LinkIdentity)).Expect<Error<string>>();
		await accounts.DidNotReceive().AuthenticateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task FreshProofAuthorizesOneOperationAndCarriesCurrentEpoch()
	{
		var (service, _, _, _, _) = Fixture();
		var proof = (await service.PasswordAsync("account", "session", "account-password", AccountSecurityOperation.NativeLogin)).Expect<RecentAccountProof>();
		var result = await service.WithProofAsync("account", "session", AccountSecurityOperation.NativeLogin, proof.Grant,
			(account, authority) => Task.FromResult<Result<long>>(authority.CredentialEpoch));
		await Assert.That(result.Expect<long>()).IsEqualTo(4);
		(await service.WithProofAsync("account", "session", AccountSecurityOperation.NativeLogin, proof.Grant,
			(_, _) => Task.FromResult<Result<long>>(7))).Expect<Error<string>>();
	}

	[Test]
	public async Task RevokedSessionAndChangedCredentialEpochCannotUseAProof()
	{
		var (service, _, sessions, _, account) = Fixture();
		var proof = (await service.PasswordAsync("account", "session", "account-password", AccountSecurityOperation.NativeLogin)).Expect<RecentAccountProof>();
		account.CredentialEpoch++;
		(await service.WithProofAsync("account", "session", AccountSecurityOperation.NativeLogin, proof.Grant,
			(_, _) => Task.FromResult<Result<long>>(7))).Expect<Error<string>>();
		sessions.InspectAsync("session", Arg.Any<CancellationToken>()).Returns(new Found<IAccountSessionStore.SessionIdentity>(new NotFound()));
		(await service.PasswordAsync("account", "session", "account-password", AccountSecurityOperation.NativeLogin)).Expect<Error<string>>();
	}

	[Test]
	public async Task ForcedPasswordChangeAllowsOnlyTheRequiredPasswordAction()
	{
		var (service, _, _, _, account) = Fixture();
		account.MustChangePassword = true;
		(await service.PasswordAsync("account", "session", "account-password", AccountSecurityOperation.NativeLogin)).Expect<Error<string>>();
		(await service.PasswordAsync("account", "session", "account-password", AccountSecurityOperation.AccountPassword)).Expect<RecentAccountProof>();
	}
}
