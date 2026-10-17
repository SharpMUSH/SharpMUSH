using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Authentication.External;
using ZiggyCreatures.Caching.Fusion;
namespace SharpMUSH.Tests.Authentication.External;

public class AccountSecurityTests
{
	[Test]
	public async Task PasswordChangeRevokesOtherSessionsAndReplacesOnlyTheInitiatingBrowser()
	{
		var gate = new AccountCredentialGate(); var store = new RecentAccountProofStore(TimeProvider.System);
		var sessions = new InMemoryAccountSessionStore(TimeProvider.System, gate, [store]);
		var account = new SharpAccount { Id = "account", Username = "name", PasswordHash = "old-hash", CredentialEpoch = 3 };
		var accounts = Substitute.For<IAccountService>(); accounts.GetByIdAsync("account", Arg.Any<CancellationToken>()).Returns(account);
		accounts.GetCharactersAsync("account", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SharpPlayer>)[]);
		accounts.SetPasswordAsync("account", "new-password", false, Arg.Any<CancellationToken>()).Returns(_ =>
		{ account.PasswordHash = "new-hash"; account.CredentialEpoch++; return new ValueTask<Result<Success>>(new Success()); });
		var passwords = Substitute.For<IPasswordService>(); passwords.PasswordIsValid("old-password", "old-hash").Returns(true);
		var proofs = new AccountRecentProofService(accounts, sessions, passwords, store, gate);
		var source = await sessions.CreateTokenAsync("account", TimeSpan.FromMinutes(15), "127.0.0.1");
		var other = await sessions.CreateTokenAsync("account", TimeSpan.FromMinutes(15), "127.0.0.2");
		var remembered = await sessions.CreateRememberedLoginAsync("account", TimeSpan.FromDays(7), "127.0.0.2");
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(); var config = ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst"); options.CurrentValue.Returns(config with { Net = config.Net with { Logins = true } });
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetContextAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(PermissionContext.None);
		capabilities.GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<string>)new HashSet<string>());
		using var cache = new FusionCache(new FusionCacheOptions());
		var claims = new AccountClaimsService(capabilities, cache, new AccountClaimsInvalidator(cache, new ObjectVersions()), NullLogger<AccountClaimsService>.Instance);
		var login = new AccountLoginCompletion(accounts, sessions, claims, options, new SitelockGuard(options), Substitute.For<IPortalThemeService>(), gate);
		var mediator = Substitute.For<IMediator>(); var catalog = new ExternalProviderCatalog(mediator, Substitute.For<ISecretStore>(), Substitute.For<IHostEnvironment>(), gate);
		var enforcer = Substitute.For<IBanEnforcer>(); var service = new AccountSecurityService(proofs, accounts, sessions, enforcer, mediator, catalog, login);
		var context = new DefaultHttpContext(); context.Request.Headers.Authorization = "Bearer " + source;
		(await service.PasswordAsync(context, "account", "new-password", null, "old-password", default)).Expect<Success>();
		(await sessions.InspectAsync(source)).Expect<NotFound>(); (await sessions.InspectAsync(other)).Expect<NotFound>();
		await Assert.That(await sessions.RedeemRememberedLoginAsync(remembered)).IsNull();
		var replacement = context.Response.Headers["X-SharpMUSH-Replacement-Session"].ToString();
		await Assert.That(replacement).IsNotEmpty();
		await Assert.That((await sessions.InspectAsync(replacement)).Expect<IAccountSessionStore.SessionIdentity>().AccountId).IsEqualTo("account");
		await enforcer.Received(1).EnforceAccountBanAsync("account", Arg.Any<CancellationToken>());
	}
	[Test]
	public async Task AProviderDisabledAfterProofCannotAuthorizeALocalSecurityMutation()
	{
		var account = new SharpAccount { Id = "account", Username = "name", CredentialEpoch = 4 };
		var accounts = Substitute.For<IAccountService>(); accounts.GetByIdAsync("account", Arg.Any<CancellationToken>()).Returns(account);
		var sessions = new InMemoryAccountSessionStore(); var token = await sessions.CreateTokenAsync("account", TimeSpan.FromMinutes(15), "ip");
		var store = new RecentAccountProofStore(TimeProvider.System); var grant = store.Issue("account", token, AccountSecurityOperation.AccountPassword, new("external", "identity", 4)).Expect<RecentAccountProof>();
		var validator = Substitute.For<IAccountProofAuthorityValidator>(); validator.IsCurrentAsync("account", Arg.Any<AccountProofAuthority>(), Arg.Any<CancellationToken>()).Returns(false);
		var service = new AccountRecentProofService(accounts, sessions, Substitute.For<IPasswordService>(), store, new AccountCredentialGate(), validator);
		var ran = false;
		(await service.WithProofAsync<Success>("account", token, AccountSecurityOperation.AccountPassword, grant.Grant, (_, _) => { ran = true; return Task.FromResult<Result<Success>>(new Success()); })).Expect<Error<string>>();
		await Assert.That(ran).IsFalse();
	}
	[Test]
	public async Task PasskeySecurityCeremoniesAreSessionBoundFixedExpiryAndOneUse()
	{
		var clock = new Clock(); var store = new AccountSecurityCeremonyStore(clock);
		await Assert.That(store.Begin("challenge", "account", "session", 7, AccountSecurityOperation.EnrollPasskey)).IsTrue();
		await Assert.That(store.Take("challenge", "account", "other")).IsNull();
		await Assert.That(store.Take("challenge", "account", "session")?.Epoch).IsEqualTo(7);
		await Assert.That(store.Take("challenge", "account", "session")).IsNull();
		store.Begin("expired", "account", "session", 7, AccountSecurityOperation.EnrollPasskey); clock.Now = clock.Now.AddMinutes(5);
		await Assert.That(store.Take("expired", "account", "session")).IsNull();
	}
	private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch; public override DateTimeOffset GetUtcNow() => Now; }
}
