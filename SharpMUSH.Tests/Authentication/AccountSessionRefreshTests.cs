using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Controllers;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Authentication;

public class AccountSessionRefreshTests
{
	[Test]
	[Arguments("valid")]
	[Arguments("missing")]
	[Arguments("expired")]
	[Arguments("disabled")]
	public async Task SessionRefreshUsesValidatedIdentityAndCurrentScopes(string state)
	{
		var sessions = Substitute.For<IAccountSessionStore>();
		var accounts = Substitute.For<IAccountService>();
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		var scopes = new HashSet<string> { "jobs.manage.own" };
		capabilities.GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(call => (IReadOnlySet<string>)new HashSet<string>(scopes));
		sessions.ValidateAsync("opaque", Arg.Any<CancellationToken>()).Returns(state == "expired" ? null : new IAccountSessionStore.SessionIdentity("account", null, null));
		accounts.GetByIdAsync("account", Arg.Any<CancellationToken>()).Returns(new SharpAccount
		{
			Id = "account", Username = "current", PasswordHash = "", MustChangePassword = true,
			Status = state == "disabled" ? AccountStatus.Disabled : AccountStatus.Active
		});
		accounts.GetCharactersAsync("account", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SharpPlayer>)[]);
		using var cache = new FusionCache(new OptionsWrapper<FusionCacheOptions>(new()));
		var claims = new AccountClaimsService(accounts, Substitute.For<IRoleDerivationService>(), Substitute.For<IRoleRegistryService>(),
			Substitute.For<IPermissionResolver>(), cache, new AccountClaimsInvalidator(cache), NullLogger<AccountClaimsService>.Instance);
		var controller = new AccountSessionController(sessions, accounts, claims, capabilities)
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
		};
		if (state != "missing") controller.Request.Headers.Authorization = "Bearer opaque";
		using var cancel = new CancellationTokenSource();
		var result = await controller.GetSession(cancel.Token);
		if (state != "valid")
		{
			await Assert.That(result).IsTypeOf<UnauthorizedResult>();
			await capabilities.DidNotReceive().GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>());
			return;
		}
		var response = (AccountSessionController.SessionStateResponse)((OkObjectResult)result).Value!;
		await Assert.That(response.Permissions).IsEquivalentTo(scopes);
		await Assert.That(response.MustChangePassword).IsTrue();
		await capabilities.Received(1).GetGrantedScopesAsync(new CapabilityActor("account"), cancel.Token);
		scopes.Clear();
		var refreshed = (AccountSessionController.SessionStateResponse)((OkObjectResult)await controller.GetSession(cancel.Token)).Value!;
		await Assert.That(refreshed.Permissions).IsEmpty();
	}
}
