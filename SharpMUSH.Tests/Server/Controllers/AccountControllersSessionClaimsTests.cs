using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Authentication.Passkeys;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// The account endpoints take the identity the AccountSession handler already validated on the request
/// instead of validating the bearer again; any other scheme (DebugAuth) still goes through the bearer.
/// </summary>
public class AccountControllersSessionClaimsTests
{
	private const string AccountId = "node_accounts/7";

	private static ClaimsPrincipal Principal(string scheme, PortalRole role = PortalRole.Player, bool mustChange = false)
	{
		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, AccountId),
			new(ClaimTypes.Role, role.ToString()),
			new(GameHub.CharacterDbrefClaim, new DBRef(5, 0).ToString())
		};
		if (mustChange) claims.Add(new Claim(AccountSessionAuthenticationHandler.MustChangePasswordClaim, "true"));
		return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
	}

	private static ControllerContext Context(ClaimsPrincipal user, string? bearer = null)
	{
		var http = new DefaultHttpContext { User = user };
		if (bearer is not null) http.Request.Headers.Authorization = $"Bearer {bearer}";
		return new ControllerContext { HttpContext = http };
	}

	private static AccountController Account(IAccountService accounts, IAccountSessionStore sessions, ClaimsPrincipal user, string? bearer = null)
		=> new(Substitute.For<IMediator>(), accounts, sessions,
			Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(), Substitute.For<IValidateService>(),
			new PasskeyService(Substitute.For<SharpMUSH.Library.IAccountStore>(),
				new PasskeyRelyingParty(new ConfigurationBuilder().Build(), Substitute.For<IOptionsWrapper<SharpMUSHOptions>>()),
				new PasskeyCeremonyStore(TimeProvider.System), TimeProvider.System, NullLogger<PasskeyService>.Instance),
			Substitute.For<SharpMUSH.Library.Services.Interfaces.IPortalThemeService>(),
			new BearerAccountResolver(sessions, accounts),
			NullLogger<AccountController>.Instance)
		{ ControllerContext = Context(user, bearer) };

	[Test]
	public async Task AccountEndpointsTrustTheSchemeWithoutRevalidatingTheSession()
	{
		var accounts = Substitute.For<IAccountService>();
		var sessions = Substitute.For<IAccountSessionStore>();
		accounts.ChangeUsernameAsync(AccountId, "newname").Returns(new ValueTask<SharpMUSH.Library.DiscriminatedUnions.Result<SharpMUSH.Library.DiscriminatedUnions.Success>>(new SharpMUSH.Library.DiscriminatedUnions.Success()));

		var result = await Account(accounts, sessions, Principal(AccountSessionAuthenticationHandler.SchemeName), "token")
			.ChangeUsername(new AccountController.ChangeUsernameRequest("newname"));

		await Assert.That(result).IsTypeOf<NoContentResult>();
		await sessions.DidNotReceive().ValidateAsync(Arg.Any<string>());
		await accounts.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task MustChangePasswordClaimStillRefusesEverythingButThePasswordChange()
	{
		var accounts = Substitute.For<IAccountService>();
		var sessions = Substitute.For<IAccountSessionStore>();
		accounts.ChangePasswordAsync(AccountId, "old", "new").Returns(new ValueTask<SharpMUSH.Library.DiscriminatedUnions.Result<SharpMUSH.Library.DiscriminatedUnions.Success>>(new SharpMUSH.Library.DiscriminatedUnions.Success()));
		var user = Principal(AccountSessionAuthenticationHandler.SchemeName, mustChange: true);

		var refused = await Account(accounts, sessions, user).ChangeUsername(new AccountController.ChangeUsernameRequest("x"));
		var allowed = await Account(accounts, sessions, user).ChangePassword(new AccountController.ChangePasswordRequest("old", "new"));

		await Assert.That(refused).IsTypeOf<ObjectResult>();
		await Assert.That(((ObjectResult)refused).StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
		await Assert.That(allowed).IsTypeOf<NoContentResult>();
	}

	[Test]
	public async Task AnotherSchemeFallsBackToTheBearer()
	{
		var accounts = Substitute.For<IAccountService>();
		var sessions = Substitute.For<IAccountSessionStore>();
		sessions.ValidateAsync("token").Returns(Task.FromResult<IAccountSessionStore.SessionIdentity?>(null));

		var result = await Account(accounts, sessions, Principal("DebugAuth"), "token")
			.ChangeUsername(new AccountController.ChangeUsernameRequest("newname"));

		await Assert.That(result).IsTypeOf<UnauthorizedObjectResult>();
		await sessions.Received(1).ValidateAsync("token");
	}

	private static AdminAccountsController Admin(IAccountService accounts, IAccountSessionStore sessions, ClaimsPrincipal user,
		FusionCache cache)
	{
		var claims = new AccountClaimsService(
			Substitute.For<IAdministrativeCapabilityService>(), cache, new AccountClaimsInvalidator(cache), NullLogger<AccountClaimsService>.Instance);
		return new AdminAccountsController(accounts, sessions, claims, Substitute.For<IAdministrativeCapabilityService>(),
			Substitute.For<IAuditLog>(), Substitute.For<IMediator>(), new BearerAccountResolver(sessions, accounts),
			NullLogger<AdminAccountsController>.Instance)
		{ ControllerContext = Context(user, "token") };
	}

	[Test]
	[Arguments(PortalRole.Player, false)]
	[Arguments(PortalRole.Wizard, true)]
	[Arguments(PortalRole.God, true)]
	public async Task AdminAccountsReadsTheRoleClaim(PortalRole role, bool admitted)
	{
		var accounts = Substitute.For<IAccountService>();
		var sessions = Substitute.For<IAccountSessionStore>();
		accounts.GetAllAccountsAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpAccount>>([]));

		using var cache = new FusionCache(new Microsoft.Extensions.Options.OptionsWrapper<FusionCacheOptions>(new FusionCacheOptions()));
		var result = await Admin(accounts, sessions, Principal(AccountSessionAuthenticationHandler.SchemeName, role), cache).List();

		await Assert.That(result is OkObjectResult).IsEqualTo(admitted);
		await sessions.DidNotReceive().ValidateAsync(Arg.Any<string>());
	}
}
