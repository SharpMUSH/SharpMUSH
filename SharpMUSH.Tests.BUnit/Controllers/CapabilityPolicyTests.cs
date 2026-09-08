using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.BUnit.Controllers;

public class CapabilityPolicyTests
{
	[Test]
	public async Task StaleTokenCannotGrantRevokedScope()
	{
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		var user = new ClaimsPrincipal(new ClaimsIdentity([
			new Claim(ClaimTypes.NameIdentifier, "a"), new Claim(PortalPermission.ClaimType, PortalPermission.SnapshotRestore)], "test"));
		var requirement = new PermissionRequirement(PortalPermission.SnapshotRestore);
		var context = new AuthorizationHandlerContext([requirement], user, null);
		await new PermissionAuthorizationHandler(capabilities).HandleAsync(context);
		await Assert.That(context.HasSucceeded).IsFalse();
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), PortalPermission.SnapshotRestore, Arg.Any<CancellationToken>()).Returns(true);
		context = new AuthorizationHandlerContext([requirement], user, null);
		await new PermissionAuthorizationHandler(capabilities).HandleAsync(context);
		await Assert.That(context.HasSucceeded).IsTrue();
	}

	[Test]
	public async Task PortalPolicyRetainsAccountAuthorityWithSelectedCharacter()
	{
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.AuthorizeAsync(new CapabilityActor("a"), PortalPermission.RolesAdmin, Arg.Any<CancellationToken>()).Returns(true);
		var user = new ClaimsPrincipal(new ClaimsIdentity([
			new Claim(ClaimTypes.NameIdentifier, "a"),
			new Claim(SharpMUSH.Server.Hubs.GameHub.CharacterDbrefClaim, "#7:1")], "test"));
		var context = new AuthorizationHandlerContext([new PermissionRequirement(PortalPermission.RolesAdmin)], user, new DefaultHttpContext());
		await new PermissionAuthorizationHandler(capabilities).HandleAsync(context);
		await Assert.That(context.HasSucceeded).IsTrue();
		await capabilities.Received(1).AuthorizeAsync(new CapabilityActor("a"), PortalPermission.RolesAdmin, Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ManagerCannotAssignRoleToSelf()
	{
		var registry = Substitute.For<IRoleRegistryService>();
		var accounts = Substitute.For<IAccountService>();
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.RolesAdmin });
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([]));
		registry.GetRolesForAccountAsync("a", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<SharpRole>>([]));
		registry.GetRoleAsync("operator").Returns(new SharpRole { Slug = "operator", Name = "Operator", Permissions = [] });
		var controller = new RolesController(registry, accounts, NullLogger<RolesController>.Instance, capabilities)
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "a")], "test")) } } };
		await Assert.That(await controller.AssignRole("a", "operator")).IsTypeOf<ForbidResult>();
		await registry.DidNotReceive().AssignRoleToAccountAsync(Arg.Any<string>(), Arg.Any<string>());
	}
}
