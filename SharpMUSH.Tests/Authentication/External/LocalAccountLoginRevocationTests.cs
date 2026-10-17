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
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Authentication.Passkeys;
using SharpMUSH.Server.Controllers;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Authentication.External;

public class LocalAccountLoginRevocationTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RevocationWinningTheCredentialGatePreventsPendingLoginFromIssuingANewSession(bool remembered)
	{
		var gate = new AccountCredentialGate();
		var sessions = new InMemoryAccountSessionStore(TimeProvider.System, gate);
		var authenticated = new SharpAccount { Id = "account", Username = "tester", PasswordHash = "old-hash", CredentialEpoch = 4 };
		var current = authenticated;
		var accounts = Substitute.For<IAccountService>();
		accounts.GetByIdAsync("account", Arg.Any<CancellationToken>()).Returns(_ => new ValueTask<SharpAccount?>(current));
		accounts.GetCharactersAsync("account", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SharpPlayer>)[]);
		accounts.AuthenticateAsync("tester", "old-password", Arg.Any<CancellationToken>()).Returns(_ => current.CredentialEpoch == 4
			? new ValueTask<AccountSignIn>(authenticated) : new ValueTask<AccountSignIn>(new NotFound()));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		var config = ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");
		options.CurrentValue.Returns(config with { Net = config.Net with { Logins = true } });
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetContextAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(PermissionContext.None);
		capabilities.GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<string>)new HashSet<string>());
		using var cache = new FusionCache(new FusionCacheOptions());
		var claims = new AccountClaimsService(capabilities, cache,
			new AccountClaimsInvalidator(cache, new ObjectVersions()), NullLogger<AccountClaimsService>.Instance);
		var themes = Substitute.For<IPortalThemeService>();
		var sitelocks = new SitelockGuard(options);
		var completion = new AccountLoginCompletion(accounts, sessions, claims, options, sitelocks, themes, gate);
		var passkeys = new PasskeyService(Substitute.For<IAccountStore>(),
			new PasskeyRelyingParty(new ConfigurationBuilder().Build(), options), new PasskeyCeremonyStore(TimeProvider.System),
			TimeProvider.System, NullLogger<PasskeyService>.Instance);
		using var services = new ServiceCollection().AddSingleton(gate).AddSingleton(completion).BuildServiceProvider();
		var context = new DefaultHttpContext { RequestServices = services };
		var rememberedToken = await sessions.CreateRememberedLoginAsync("account", TimeSpan.FromDays(7), "127.0.0.1");
		context.Request.Headers.Cookie = RememberedLoginCookie.Name + "=" + rememberedToken;
		var controller = new AuthController(Substitute.For<IMediator>(), Substitute.For<IPasswordService>(), Substitute.For<IOttStore>(),
			accounts, sessions, claims, options, Substitute.For<IHostEnvironment>(), sitelocks, passkeys, themes,
			NullLogger<AuthController>.Instance, completion)
		{ ControllerContext = new() { HttpContext = context } };
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var revocation = gate.RunAsync(async () =>
		{
			entered.SetResult();
			await release.Task;
			current = new SharpAccount { Id = "account", Username = "tester", PasswordHash = "new-hash", CredentialEpoch = 5 };
			await sessions.RevokeAllForAccountAsync("account");
		});
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Task<IActionResult> attempt;
		try
		{
			// Before the fix authentication/redemption completes synchronously, then issuance waits
			// behind the winning mutation. After the fix the whole local login waits instead.
			attempt = remembered ? controller.AccountResume(null) : controller.AccountLogin(new("tester", "old-password"));
		}
		finally { release.SetResult(); }
		await revocation.WaitAsync(TimeSpan.FromSeconds(5));
		var response = await attempt.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(response is OkObjectResult).IsFalse()
			.Because("a revoked credential cannot be exchanged for a fresh session after the revocation won the shared gate");
	}
}
