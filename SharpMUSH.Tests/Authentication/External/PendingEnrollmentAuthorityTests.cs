using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Authentication.External;
using SharpMUSH.Server.Authentication.Passkeys;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Authentication.External;

public class PendingEnrollmentAuthorityTests
{
	[Test]
	[Arguments("unchanged")]
	[Arguments("disabled")]
	[Arguments("reenabled")]
	public async Task PendingPasskeyEnrollmentRetainsItsSourceProviderAuthority(string change)
	{
		var gate = new AccountCredentialGate(); var store = new RecentAccountProofStore(TimeProvider.System);
		var sessions = new InMemoryAccountSessionStore(TimeProvider.System, gate, [store]);
		var account = new SharpAccount { Id = "account", Username = "owner", PasswordHash = null, CredentialEpoch = 3 };
		var accounts = Substitute.For<IAccountService>(); accounts.GetByIdAsync("account", Arg.Any<CancellationToken>()).Returns(account);
		var settings = new ExternalProviderSettings { Enabled = true, ClientId = "client", PublicOrigin = "https://game.example", RegistrationId = "external-google", Generation = "original" };
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetExternalProviderSettingsQuery>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(call.Arg<GetExternalProviderSettingsQuery>().Provider == "google" ? settings : new()));
		var identity = new ExternalIdentity("identity", "account", new("external-google", "https://accounts.google.com", "subject"), null, DateTimeOffset.UnixEpoch, null);
		mediator.Send(new GetExternalIdentitiesQuery("account"), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ExternalIdentity>>([identity]));
		var secrets = Substitute.For<ISecretStore>(); secrets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<FoundResult<string>>("secret"));
		var catalog = new ExternalProviderCatalog(mediator, secrets, Substitute.For<IHostEnvironment>(), gate);
		var current = (await catalog.EffectiveAsync("google")).Expect<EffectiveExternalProvider>();
		var validator = new AccountProofAuthorityValidator(mediator, catalog);
		var proofs = new AccountRecentProofService(accounts, sessions, Substitute.For<IPasswordService>(), store, gate, validator);
		var security = new AccountSecurityService(proofs, accounts, sessions, Substitute.For<IBanEnforcer>(), mediator, catalog);
		var accountStore = Substitute.For<SharpMUSH.Library.IAccountStore>();
		accountStore.GetAccountPasskeysAsync("account", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<AccountPasskey>>([]));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(); options.CurrentValue.Returns(ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst"));
		var passkeys = new PasskeyService(accountStore, new PasskeyRelyingParty(new ConfigurationBuilder().Build(), options), new PasskeyCeremonyStore(TimeProvider.System), TimeProvider.System, NullLogger<PasskeyService>.Instance);
		var token = await sessions.CreateTokenAsync("account", TimeSpan.FromMinutes(15), "ip");
		var grant = store.Issue("account", token, AccountSecurityOperation.EnrollPasskey, new("external", "identity", 3, current.Generation)).Expect<RecentAccountProof>();
		using var services = new ServiceCollection().AddSingleton(gate).AddSingleton<IAccountProofAuthorityValidator>(validator).BuildServiceProvider();
		var context = new DefaultHttpContext { RequestServices = services }; context.Request.Scheme = "https"; context.Request.Host = new("game.example"); context.Request.Headers.Authorization = "Bearer " + token;
		var controller = new AccountController(mediator, accounts, sessions, options, Substitute.For<IValidateService>(), passkeys, Substitute.For<IPortalThemeService>(), new(sessions, accounts), NullLogger<AccountController>.Instance, security, new AccountSecurityCeremonyStore(TimeProvider.System)) { ControllerContext = new() { HttpContext = context } };
		var started = (OkObjectResult)await controller.PasskeyOptions(new("", grant.Grant)); var challenge = (PasskeyService.Challenge)started.Value!;
		if (change != "unchanged") settings = settings with { Enabled = false, Generation = "disabled" };
		if (change == "reenabled") settings = settings with { Enabled = true, Generation = "reenabled" };
		using var malformed = System.Text.Json.JsonDocument.Parse("{}");
		var result = await controller.AddPasskey(new(challenge.CeremonyId, "key", malformed.RootElement));
		if (change == "unchanged") await Assert.That(result).IsTypeOf<BadRequestObjectResult>().Because("Valid authority reaches the separate WebAuthn assertion check.");
		else await Assert.That(result).IsTypeOf<UnauthorizedObjectResult>().Because("An invalidated provider proof cannot finish enrollment.");
		await accountStore.DidNotReceive().AddAccountPasskeyAsync(Arg.Any<AccountPasskey>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
	}
}
