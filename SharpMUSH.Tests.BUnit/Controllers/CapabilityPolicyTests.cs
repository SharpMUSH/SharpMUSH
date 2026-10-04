using SharpMUSH.Library.DiscriminatedUnions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
	[Arguments(RoleRefusalKind.Forbidden, 403)]
	[Arguments(RoleRefusalKind.NotFound, 404)]
	[Arguments(RoleRefusalKind.Invalid, 400)]
	public async Task RefusedAssignmentAnswersWithTheRefusalKind(RoleRefusalKind kind, int status)
	{
		var management = Substitute.For<IRoleManagementService>();
		management.AssignAsync(new CapabilityActor("a"), "b", "operator", Arg.Any<CancellationToken>())
			.Returns(new RoleOutcome<Success>(new RoleRefusal(kind, "no")));
		var result = await Controller(management).AssignRole("b", "operator");
		await Assert.That(result).IsAssignableTo<ObjectResult>();
		await Assert.That(((ObjectResult)result).StatusCode).IsEqualTo(status);
	}

	[Test]
	public async Task AcceptedAssignmentIsOk()
	{
		var management = Substitute.For<IRoleManagementService>();
		management.AssignAsync(new CapabilityActor("a"), "b", "operator", Arg.Any<CancellationToken>())
			.Returns(new RoleOutcome<Success>(new Success()));
		await Assert.That(await Controller(management).AssignRole("b", "operator")).IsTypeOf<OkResult>();
	}

	[Test]
	public async Task UnknownOverrideStateIsRefusedBeforeTheService()
	{
		var management = Substitute.For<IRoleManagementService>();
		var result = await Controller(management).SetOverride("b", new RolesController.OverrideDto(PortalPermission.WikiEdit, "Maybe"));
		await Assert.That(result).IsTypeOf<BadRequestObjectResult>();
		await management.DidNotReceiveWithAnyArgs().SetOverrideAsync(default!, default!, default!, default);
	}

	private static RolesController Controller(IRoleManagementService management) => new(
		Substitute.For<IRoleRegistryService>(), Substitute.For<IAccountService>(), Substitute.For<IAdministrativeCapabilityService>(), management)
	{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "a")], "test")) } } };

	[Test]
	public async Task SecondaryClaimsReflectRevocationAndNewGrantsWithoutMutatingCachedIdentity()
	{
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetGrantedScopesAsync(new CapabilityActor("a"), Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.WikiEdit });
		var cached = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "a"),
			new Claim(PortalPermission.ClaimType, PortalPermission.WikiAdmin)], "test"));
		var transformation = new FreshPermissionClaimsTransformation(capabilities, new HttpContextAccessor());
		var current = await transformation.TransformAsync(cached);
		await Assert.That(current.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin)).IsFalse();
		await Assert.That(current.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiEdit)).IsTrue();
		await Assert.That(cached.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin)).IsTrue();
		capabilities.GetGrantedScopesAsync(new CapabilityActor("a"), Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.WikiEdit, PortalPermission.WikiAdmin });
		await Assert.That((await transformation.TransformAsync(cached)).HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin)).IsTrue();
	}

	[Test]
	public async Task ScopesAreReadOncePerRequestAndAgainOnTheNext()
	{
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetGrantedScopesAsync(new CapabilityActor("a"), Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.WikiEdit });
		var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
		var transformation = new FreshPermissionClaimsTransformation(capabilities, accessor);
		var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "a")], "test"));
		var first = await transformation.TransformAsync(principal);
		var second = await transformation.TransformAsync(principal);
		await Assert.That(first.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiEdit)).IsTrue();
		await Assert.That(second.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiEdit)).IsTrue();
		await capabilities.Received(1).GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>());
		accessor.HttpContext = new DefaultHttpContext();
		await transformation.TransformAsync(principal);
		await capabilities.Received(2).GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>());
	}
}
