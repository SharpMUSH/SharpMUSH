using System.Net;
using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Authentication.External;
using ZiggyCreatures.Caching.Fusion;
namespace SharpMUSH.Tests.Authentication.External;

public class ExternalLoginJourneyTests
{
	[Test]
	public async Task BrowserBoundCallbackAndHandoffAreOneUseAndSignupCollisionCanRetry()
	{
		using var fixture = new Fixture(); var jar = new Browser();
		var (id, properties) = await fixture.BeginAsync(jar);
		var key = new ExternalIdentityKey("registration", "https://accounts.google.com", "subject");
		var callback = jar.Context(); await fixture.Journey.CompleteAsync(callback, properties, key, "display", default); jar.Apply(callback);
		await Assert.That(callback.Response.Headers.Location.ToString()).IsEqualTo("/auth/external/complete");
		var replay = jar.Context(); await fixture.Journey.CompleteAsync(replay, properties, key, "display", default);
		await Assert.That(replay.Response.Headers.Location.ToString()).IsEqualTo("/login?external=failed");
		var wrong = new Browser(); wrong.Values[ExternalLoginJourney.HandoffCookie] = jar.Values[ExternalLoginJourney.HandoffCookie];
		await Assert.That(await fixture.Journey.FinishAsync(wrong.Context(), default)).IsTypeOf<BadRequestObjectResult>();
		var finish = jar.Context(); await Assert.That(await fixture.Journey.FinishAsync(finish, default)).IsTypeOf<OkObjectResult>(); jar.Apply(finish);
		await Assert.That(await fixture.Journey.FinishAsync(jar.Context(), default)).IsTypeOf<BadRequestObjectResult>();
		var attempts = 0;
		fixture.Mediator.Send(Arg.Any<ProvisionExternalAccountCommand>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<Result<SharpAccount>>(++attempts == 1 ? new Error<string>("Username already exists.") : fixture.Account));
		await Assert.That(await fixture.Journey.SignupAsync(jar.Context(), "taken", default)).IsTypeOf<ConflictObjectResult>();
		var signup = jar.Context(); await Assert.That(await fixture.Journey.SignupAsync(signup, "available", default)).IsTypeOf<OkObjectResult>(); jar.Apply(signup);
		await Assert.That(attempts).IsEqualTo(2);
		await Assert.That(await fixture.Journey.SignupAsync(jar.Context(), "replay", default)).IsTypeOf<BadRequestObjectResult>();
		await fixture.Sessions.Received(1).CreateTokenAsync(fixture.Account.Id!, Arg.Any<TimeSpan>(), Arg.Any<string>(), null, null);
	}
	[Test]
	[Arguments("generation")]
	[Arguments("disable")]
	public async Task ConfigurationChangesInvalidatePreviouslyStartedBrowserFlow(string change)
	{
		using var fixture = new Fixture(); var jar = new Browser(); var (id, properties) = await fixture.BeginAsync(jar);
		fixture.Settings = change == "disable" ? fixture.Settings with { Enabled = false } : fixture.Settings with { Generation = "replaced" };
		await Assert.That(await fixture.Journey.ChallengeAsync(jar.Context(), id, default)).IsTypeOf<BadRequestObjectResult>();
		var callback = jar.Context(); await fixture.Journey.CompleteAsync(callback, properties, new("registration", "https://accounts.google.com", "subject"), null, default);
		await Assert.That(callback.Response.Headers.Location.ToString()).IsEqualTo("/login?external=failed");
	}
	[Test]
	[Arguments("session")]
	[Arguments("epoch")]
	[Arguments("wrong-identity")]
	public async Task ReauthenticationCallbackRejectsRevokedSourceOrDifferentIdentity(string change)
	{
		using var fixture = new Fixture(); var jar = new Browser();
		var key = new ExternalIdentityKey("registration", "https://accounts.google.com", "subject");
		fixture.Mediator.Send(Arg.Any<GetExternalIdentitiesQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ExternalIdentity>>([new("identity", fixture.Account.Id!, key, "display", DateTimeOffset.UnixEpoch, null)]));
		var (id, properties) = await fixture.BeginAsync(jar, ExternalFlowPurpose.Reauthenticate);
		if (change == "session") fixture.SessionLive = false;
		if (change == "epoch") fixture.Account.CredentialEpoch++;
		var callback = jar.Context(); await fixture.Journey.CompleteAsync(callback, properties, change == "wrong-identity" ? key with { Subject = "other" } : key, null, default);
		await Assert.That(callback.Response.Headers.Location.ToString()).IsEqualTo("/login?external=failed");
		await Assert.That(callback.Response.Headers.SetCookie.ToString()).DoesNotContain(ExternalLoginJourney.HandoffCookie);
	}
	[Test]
	public async Task OpenRedirectAndMissingBindingAreRefusedBeforeChallenge()
	{
		using var fixture = new Fixture(); var jar = new Browser();
		await Assert.That(await fixture.Journey.BeginAsync(jar.Context(), "google", "//attacker.example", false, ExternalFlowPurpose.Login, null, null, null, default)).IsTypeOf<BadRequestObjectResult>();
		var (id, _) = await fixture.BeginAsync(jar);
		await Assert.That(await fixture.Journey.ChallengeAsync(new Browser().Context(), id, default)).IsTypeOf<BadRequestObjectResult>();
	}
	[Test]
	[Arguments("unchanged")]
	[Arguments("epoch")]
	[Arguments("revocation")]
	[Arguments("ownership")]
	public async Task KnownIdentityHandoffRetainsItsVerifiedAccountAuthority(string change)
	{
		using var fixture = new Fixture(); var browser = new Browser();
		var key = new ExternalIdentityKey("registration", "https://accounts.google.com", "subject");
		var identity = new ExternalIdentity("identity", fixture.Account.Id!, key, "display", DateTimeOffset.UnixEpoch, null);
		fixture.Mediator.Send(Arg.Any<GetExternalIdentityQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<Found<ExternalIdentity>>(identity));
		fixture.Accounts.GetByIdAsync("other-account", Arg.Any<CancellationToken>()).Returns(new SharpAccount { Id = "other-account", Username = "other" });
		fixture.Accounts.GetCharactersAsync("other-account", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SharpPlayer>)[]);
		var (_, properties) = await fixture.BeginAsync(browser);
		var callback = browser.Context(); await fixture.Journey.CompleteAsync(callback, properties, key, "display", default); browser.Apply(callback);
		if (change == "epoch") fixture.Account.CredentialEpoch++;
		if (change == "revocation") fixture.Flows.RevokeAccount(fixture.Account.Id!);
		if (change == "ownership") identity = identity with { AccountId = "other-account" };
		var result = await fixture.Journey.FinishAsync(browser.Context(), default);
		if (change == "unchanged") await Assert.That(result).IsTypeOf<OkObjectResult>();
		else await Assert.That(result is OkObjectResult).IsFalse().Because("A provider ceremony cannot survive account revocation, an epoch change, or identity ownership change.");
	}
	private sealed class Browser
	{
		public Dictionary<string, string> Values { get; } = new();
		public DefaultHttpContext Context()
		{
			var context = new DefaultHttpContext(); context.Request.Scheme = "https"; context.Request.Host = new("game.example");
			context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.42");
			context.Request.Headers.Cookie = string.Join("; ", Values.Select(pair => pair.Key + "=" + pair.Value)); return context;
		}
		public void Apply(HttpContext context)
		{
			foreach (var header in context.Response.Headers.SetCookie)
			{
				var first = header!.Split(';')[0]; var parts = first.Split('=', 2);
				if (parts[1].Length == 0) Values.Remove(parts[0]); else Values[parts[0]] = parts[1];
			}
		}
	}
	private sealed class Fixture : IDisposable
	{
		public IMediator Mediator { get; } = Substitute.For<IMediator>();
		public IAccountService Accounts { get; } = Substitute.For<IAccountService>();
		public ExternalLoginFlowStore Flows { get; } = new(TimeProvider.System);
		public IAccountSessionStore Sessions { get; } = Substitute.For<IAccountSessionStore>();
		public SharpAccount Account { get; } = new() { Id = "account", Username = "username", PasswordHash = null, AllowCharacterPasswordAccountLogin = false };
		public ExternalProviderSettings Settings { get; set; } = new() { Enabled = true, ClientId = "client", PublicOrigin = "https://game.example", RegistrationId = "registration", Generation = "initial" };
		public bool SessionLive { get; set; } = true;
		public ExternalLoginJourney Journey { get; }
		private readonly FusionCache cache = new(new Microsoft.Extensions.Options.OptionsWrapper<FusionCacheOptions>(new()));
		public Fixture()
		{
			Mediator.Send(Arg.Any<GetExternalProviderSettingsQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Settings));
			Mediator.Send(Arg.Any<GetExternalIdentityQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Found<ExternalIdentity>>(new NotFound()));
			var secrets = Substitute.For<ISecretStore>(); secrets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<FoundResult<string>>("secret"));
			var gate = new AccountCredentialGate(); var environment = Substitute.For<IHostEnvironment>();
			var providers = new ExternalProviderCatalog(Mediator, secrets, environment, gate);
			var accounts = Accounts; accounts.GetByIdAsync(Account.Id!, Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<SharpAccount?>(Account));
			accounts.GetCharactersAsync(Account.Id!, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<SharpPlayer>>([]));
			Sessions.InspectAsync("source-token", Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<Found<IAccountSessionStore.SessionIdentity>>(SessionLive ? new IAccountSessionStore.SessionIdentity(Account.Id!, null, null) : new NotFound()));
			Sessions.InspectDigestAsync(AccountSessionDigest.Compute("source-token"), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<Found<IAccountSessionStore.SessionIdentity>>(SessionLive ? new IAccountSessionStore.SessionIdentity(Account.Id!, null, null) : new NotFound()));
			Sessions.CreateTokenAsync(Account.Id!, Arg.Any<TimeSpan>(), Arg.Any<string>(), null, null).Returns(Task.FromResult("account-session"));
			var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(); var config = ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst"); options.CurrentValue.Returns(config with { Net = config.Net with { Logins = true, PlayerCreation = true } });
			var locks = new SitelockGuard(options); var capabilities = Substitute.For<IAdministrativeCapabilityService>();
			capabilities.GetContextAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(PermissionContext.None);
			capabilities.GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<string>)new HashSet<string>());
			var claims = new AccountClaimsService(capabilities, cache, new AccountClaimsInvalidator(cache, new ObjectVersions()), NullLogger<AccountClaimsService>.Instance);
			var proofStore = new RecentAccountProofStore(TimeProvider.System);
			var proofs = new AccountRecentProofService(accounts, Sessions, Substitute.For<IPasswordService>(), proofStore, gate);
			Journey = new(Flows, providers, Mediator, accounts, Sessions, proofs, gate,
			 new(accounts, Sessions, claims, options, locks, Substitute.For<IPortalThemeService>(), gate), options, locks, TimeProvider.System, new AccountProofAuthorityValidator(Mediator, providers));
		}
		public async Task<(string Id, AuthenticationProperties Properties)> BeginAsync(Browser browser, ExternalFlowPurpose purpose = ExternalFlowPurpose.Login)
		{
			var begin = browser.Context(); if (purpose != ExternalFlowPurpose.Login) begin.Request.Headers.Authorization = "Bearer source-token";
			var result = (OkObjectResult)await Journey.BeginAsync(begin, "google", "/account", false, purpose, null, purpose == ExternalFlowPurpose.Reauthenticate ? AccountSecurityOperation.AccountPassword : null, "identity", default);
			browser.Apply(begin); var id = JsonSerializer.SerializeToElement(result.Value).GetProperty("TransactionId").GetString()!;
			var challenge = (ChallengeResult)await Journey.ChallengeAsync(browser.Context(), id, default);
			return (id, challenge.Properties!);
		}
		public void Dispose() => cache.Dispose();
	}
}
