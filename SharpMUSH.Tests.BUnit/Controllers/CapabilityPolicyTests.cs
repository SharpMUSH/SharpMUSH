using System.Collections.Immutable;
using SharpMUSH.Library.DiscriminatedUnions;
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
	[Arguments("node_accounts/a")]
	[Arguments("a")]
	public async Task ManagerCannotAssignRoleToSelf(string alias)
	{
		var registry = Substitute.For<IRoleRegistryService>();
		var accounts = Substitute.For<IAccountService>();
		accounts.GetByIdAsync(alias, Arg.Any<CancellationToken>()).Returns(new SharpAccount { Id = "node_accounts/a", Username = "a", PasswordHash = "" });
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.RolesAdmin });
		accounts.GetCharactersAsync("node_accounts/a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([]));
		registry.GetRolesForAccountAsync("node_accounts/a", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<SharpRole>>([]));
		registry.GetRoleAsync("operator").Returns(new SharpRole { Slug = "operator", Name = "Operator", Permissions = [] });
		var controller = new RolesController(registry, accounts, NullLogger<RolesController>.Instance, capabilities)
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "node_accounts/a")], "test")) } } };
		await Assert.That(await controller.AssignRole(alias, "operator")).IsTypeOf<ForbidResult>();
		await registry.DidNotReceive().AssignRoleToAccountAsync(Arg.Any<string>(), Arg.Any<string>());
		await Assert.That(await controller.RemoveRole(alias, "operator")).IsTypeOf<ForbidResult>();
		await registry.DidNotReceive().RemoveRoleFromAccountAsync(Arg.Any<string>(), Arg.Any<string>());
	}
	[Test]
	public async Task DerivedRolePriorityPermitsLowerDelegationWithoutExplicitAssignments()
	{
		var registry = Substitute.For<IRoleRegistryService>();
		var accounts = Substitute.For<IAccountService>();
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.RolesAdmin });
		capabilities.ExplainAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, PermissionExplanation>
		{ [PortalPermission.RolesAdmin] = new(true, 30, ["wizard"], "explicit") });
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([]));
		accounts.GetByIdAsync("b", Arg.Any<CancellationToken>()).Returns(new SharpAccount { Id = "b", Username = "b", PasswordHash = "" });
		registry.GetRolesForAccountAsync("a", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<SharpRole>>([]));
		registry.GetRoleAsync("operator").Returns(new SharpRole { Slug = "operator", Name = "Operator", Priority = 5, Permissions = [] });
		var controller = new RolesController(registry, accounts, NullLogger<RolesController>.Instance, capabilities)
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "a")], "test")) } } };
		await Assert.That(await controller.AssignRole("b", "operator")).IsTypeOf<OkResult>();
		await registry.Received(1).AssignRoleToAccountAsync("b", "operator");
	}

	[Test]
	public async Task SecondaryClaimsReflectRevocationAndNewGrantsWithoutMutatingCachedIdentity()
	{
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetGrantedScopesAsync(new CapabilityActor("a"), Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.WikiEdit });
		var cached = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "a"),
			new Claim(PortalPermission.ClaimType, PortalPermission.WikiAdmin)], "test"));
		var transformation = new FreshPermissionClaimsTransformation(capabilities);
		var current = await transformation.TransformAsync(cached);
		await Assert.That(current.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin)).IsFalse();
		await Assert.That(current.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiEdit)).IsTrue();
		await Assert.That(cached.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin)).IsTrue();
		capabilities.GetGrantedScopesAsync(new CapabilityActor("a"), Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.WikiEdit, PortalPermission.WikiAdmin });
		await Assert.That((await transformation.TransformAsync(cached)).HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin)).IsTrue();
	}

	[Test]
	public async Task ConcurrentRoleEditsCannotRaceGodRecoveryValidation()
	{
		var registry = Substitute.For<IRoleRegistryService>();
		var accounts = Substitute.For<IAccountService>();
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.RolesAdmin });
		var godPlayer = new SharpPlayer
		{
			PasswordHash = "", Quota = 0, Home = null!, Location = null!, Object = new SharpObject
			{
				Key = 1, Name = "God", Type = "PLAYER", Locks = ImmutableDictionary<string, SharpLockData>.Empty, Owner = null!, Powers = null!,
				Attributes = null!, LazyAttributes = null!, AllAttributes = null!, LazyAllAttributes = null!, Flags = null!, Parent = null!, Zone = null!, Children = null!
			}
		};
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([godPlayer]));
		var state = new Dictionary<string, SharpRole>
		{
			["god"] = new() { Slug = "god", Name = "God", IsSystem = true, Priority = 100, Permissions = new() { [PortalPermission.RolesAdmin] = PermissionState.Allow } },
			["restricted"] = new() { Slug = "restricted", Name = "Restricted", Priority = 40, Permissions = new() { [PortalPermission.RolesAdmin] = PermissionState.Deny } }
		};
		registry.GetRoleAsync(Arg.Any<string>()).Returns(call => Task.FromResult<Found<SharpRole>>(state[call.Arg<string>()]));
		registry.GetRolesAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyList<SharpRole>>(state.Values.ToArray()));
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		registry.UpsertRoleAsync(Arg.Any<SharpRole>()).Returns(async call =>
		{
			entered.TrySetResult();
			await release.Task;
			var role = call.Arg<SharpRole>(); state[role.Slug] = role;
		});
		RolesController Controller() => new(registry, accounts, NullLogger<RolesController>.Instance, capabilities)
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "a")], "test")) } } };
		var first = Controller().Upsert(new("god", "God", null, 50, true, new() { [PortalPermission.RolesAdmin] = "Allow" }, 0, 0));
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var second = Controller().Upsert(new("restricted", "Restricted", null, 60, false, new() { [PortalPermission.RolesAdmin] = "Deny" }, 0, 0));
		release.SetResult();
		await Assert.That(await first).IsTypeOf<OkObjectResult>();
		await Assert.That(await second).IsTypeOf<BadRequestObjectResult>();
		await Assert.That(state["restricted"].Priority).IsEqualTo(40);
	}

}
