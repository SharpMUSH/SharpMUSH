using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Models;
using SharpMUSH.Server.Authentication.External;
using Microsoft.Extensions.Hosting;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Controllers;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Authentication.External;

public class StaffAccountRecoveryTests
{
	[Test]
	public async Task ModeratorSessionAloneCannotResetAnotherAccountsPassword()
	{
		var accounts = Substitute.For<IAccountService>();
		var sessions = Substitute.For<IAccountSessionStore>();
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.AuthorizeAsync(new CapabilityActor("node_accounts/actor"), PortalPermission.PlayersModerate,
			Arg.Any<CancellationToken>()).Returns(true);
		accounts.SetPasswordAsync("node_accounts/target", "temporary-password", true, Arg.Any<CancellationToken>())
			.Returns(new ValueTask<Result<Success>>(new Success()));
		using var cache = new FusionCache(new FusionCacheOptions());
		var claims = new AccountClaimsService(capabilities, cache, new AccountClaimsInvalidator(cache, new ObjectVersions()),
			NullLogger<AccountClaimsService>.Instance);
		var controller = new AdminAccountsController(accounts, sessions, claims, capabilities,
			Substitute.For<IAuditLog>(), Substitute.For<IMediator>(), new BearerAccountResolver(sessions, accounts),
			NullLogger<AdminAccountsController>.Instance)
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext
				{
					User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "node_accounts/actor")],
						AccountSessionAuthenticationHandler.SchemeName))
				}
			}
		};
		controller.Request.Headers.Authorization = "Bearer session";

		var result = await controller.ResetPassword("target", new AdminAccountsController.ResetPasswordRequest("temporary-password"));

		await Assert.That(result is ObjectResult { StatusCode: StatusCodes.Status403Forbidden }).IsTrue();
		await accounts.DidNotReceive().SetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
	}
	[Test]
	public async Task FreshProofResetsLowerInactiveTargetRevokesEverySessionAndPreservesStatus()
	{
		using var fixture = await Fixture.Create();
		fixture.Target.Status = AccountStatus.Disabled;
		var targetSession = await fixture.Sessions.CreateTokenAsync(fixture.Target.Id!, TimeSpan.FromMinutes(15), "192.0.2.2");
		var remembered = await fixture.Sessions.CreateRememberedLoginAsync(fixture.Target.Id!, TimeSpan.FromDays(7), "192.0.2.2");
		var grant = fixture.Grant();
		var result = await fixture.Controller.ResetPassword("target", new("temporary-password", grant));
		await Assert.That(result).IsTypeOf<NoContentResult>();
		await Assert.That(fixture.Target.Status).IsEqualTo(AccountStatus.Disabled);
		await Assert.That(fixture.Target.MustChangePassword).IsTrue();
		(await fixture.Sessions.InspectAsync(targetSession)).Expect<NotFound>();
		await Assert.That(await fixture.Sessions.RedeemRememberedLoginAsync(remembered)).IsNull();
		await fixture.Enforcer.Received(1).EnforceAccountBanAsync(fixture.Target.Id!, Arg.Any<CancellationToken>());
		await fixture.Audit.Received(1).RecordPortalAsync(fixture.Actor.Id!, AuditActions.AccountPassword, Arg.Any<AuditTarget>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
		await Assert.That(await fixture.Controller.ResetPassword("target", new("another-password", grant))).IsTypeOf<ObjectResult>();
		await fixture.Accounts.Received(1).SetPasswordAsync(fixture.Target.Id!, "temporary-password", true, Arg.Any<CancellationToken>());
	}
	[Test]
	[Arguments("none")]
	[Arguments("expired")]
	[Arguments("wrong-session")]
	[Arguments("revoked-session")]
	[Arguments("must-change")]
	public async Task MissingOrStaleDedicatedStaffProofCannotWrite(string condition)
	{
		using var fixture = await Fixture.Create(); var grant = condition == "none" ? null : fixture.Grant();
		if (condition == "expired") fixture.Clock.Now += TimeSpan.FromMinutes(6);
		if (condition == "wrong-session") fixture.Controller.Request.Headers.Authorization = "Bearer " + await fixture.Sessions.CreateTokenAsync(fixture.Actor.Id!, TimeSpan.FromMinutes(15), "192.0.2.1");
		if (condition == "revoked-session") await fixture.Sessions.RevokeAsync(fixture.Source);
		if (condition == "must-change") fixture.Actor.MustChangePassword = true;
		var result = await fixture.Controller.ResetPassword("target", new("temporary-password", grant));
		await Assert.That(result is ObjectResult { StatusCode: StatusCodes.Status403Forbidden }).IsTrue();
		await fixture.Accounts.DidNotReceive().SetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
	}
	[Test]
	public async Task RevokedModerationPermissionWhileWaitingForProofGateCannotRecoverAnAccount()
	{
		using var fixture = await Fixture.Create();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var checkedOuterPermission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var permitted = true;
		fixture.Capabilities.AuthorizeAsync(new CapabilityActor(fixture.Actor.Id!), PortalPermission.PlayersModerate, Arg.Any<CancellationToken>()).Returns(_ =>
		{
			checkedOuterPermission.TrySetResult();
			return Task.FromResult(permitted);
		});
		var grant = fixture.Grant();
		var blocker = fixture.Gate.RunAsync(async () => await release.Task);
		var pending = fixture.Controller.ResetPassword("target", new("temporary-password", grant));
		try
		{
			await checkedOuterPermission.Task.WaitAsync(TimeSpan.FromSeconds(5));
			permitted = false;
		}
		finally { release.TrySetResult(); }
		await blocker;
		var result = await pending;
		await Assert.That(result is ObjectResult { StatusCode: StatusCodes.Status403Forbidden }).IsTrue();
		await fixture.Accounts.DidNotReceive().SetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
		await fixture.Audit.DidNotReceive().RecordPortalAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<AuditTarget>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
		await fixture.Enforcer.DidNotReceive().EnforceAccountBanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}
	[Test]
	public async Task ProofedRecoveryPreservesBadRequestNotFoundAndHierarchyForbiddenStatuses()
	{
		using var fixture = await Fixture.Create();
		await Assert.That(await fixture.Controller.ResetPassword("target", new("short", fixture.Grant()))).IsTypeOf<BadRequestObjectResult>();
		await Assert.That(await fixture.Controller.ResetPassword("missing", new("temporary-password", fixture.Grant()))).IsTypeOf<NotFoundObjectResult>();
		await fixture.Registry.AssignRoleToAccountAsync(fixture.Target.Id!, "manager");
		var result = await fixture.Controller.ResetPassword("target", new("temporary-password", fixture.Grant()));
		await Assert.That(result is ObjectResult { StatusCode: StatusCodes.Status403Forbidden }).IsTrue();
		await fixture.Accounts.DidNotReceive().SetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
	}
	private sealed class FixedClock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
		public override DateTimeOffset GetUtcNow() => Now;
	}
	private sealed class Fixture : IDisposable
	{
		public FixedClock Clock { get; } = new();
		public SharpAccount Actor { get; } = new() { Id = "node_accounts/actor", Username = "actor", PasswordHash = "dedicated-hash", CredentialEpoch = 3 };
		public SharpAccount Target { get; } = new() { Id = "node_accounts/target", Username = "target", PasswordHash = "old-hash" };
		public IAccountService Accounts { get; } = Substitute.For<IAccountService>();
		public IAdministrativeCapabilityService Capabilities { get; } = Substitute.For<IAdministrativeCapabilityService>();
		public AccountCredentialGate Gate { get; } = new();
		public IBanEnforcer Enforcer { get; } = Substitute.For<IBanEnforcer>();
		public IAuditLog Audit { get; } = Substitute.For<IAuditLog>();
		public InMemoryRoleRegistry Registry { get; } = InMemoryRoleRegistry.Seeded();
		public InMemoryAccountSessionStore Sessions { get; private set; } = null!;
		public string Source { get; private set; } = "";
		public AdminAccountsController Controller { get; private set; } = null!;
		private RecentAccountProofStore proofs = null!;
		private readonly FusionCache cache = new(new FusionCacheOptions());
		public static async Task<Fixture> Create()
		{
			var fixture = new Fixture(); var gate = fixture.Gate; fixture.proofs = new(fixture.Clock);
			fixture.Sessions = new(fixture.Clock, gate, [fixture.proofs]);
			fixture.Source = await fixture.Sessions.CreateTokenAsync(fixture.Actor.Id!, TimeSpan.FromMinutes(15), "192.0.2.1");
			fixture.Accounts.GetByIdAsync(fixture.Actor.Id!, Arg.Any<CancellationToken>()).Returns(_ => fixture.Actor);
			fixture.Accounts.GetByIdAsync(fixture.Target.Id!, Arg.Any<CancellationToken>()).Returns(_ => fixture.Target);
			fixture.Accounts.GetCharactersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SharpPlayer>)[]);
			fixture.Accounts.SetPasswordAsync(fixture.Target.Id!, Arg.Any<string>(), true, Arg.Any<CancellationToken>()).Returns(call =>
			{ fixture.Target.PasswordHash = "new-hash"; fixture.Target.MustChangePassword = true; fixture.Target.CredentialEpoch++; return ValueTask.FromResult<Result<Success>>(new Success()); });
			var manager = new SharpRole { Slug = "manager", Name = "Manager", Priority = 100 }; fixture.Registry.Add(manager);
			var capabilities = fixture.Capabilities;
			capabilities.AuthorizeAsync(new CapabilityActor(fixture.Actor.Id!), PortalPermission.PlayersModerate, Arg.Any<CancellationToken>()).Returns(true);
			capabilities.GetContextAsync(new CapabilityActor(fixture.Actor.Id!), Arg.Any<CancellationToken>()).Returns(new PermissionContext([manager], new Dictionary<string, PermissionState>(), false));
			var mediator = Substitute.For<IMediator>(); var catalog = new ExternalProviderCatalog(mediator, Substitute.For<ISecretStore>(), Substitute.For<IHostEnvironment>(), gate);
			var security = new AccountSecurityService(new(fixture.Accounts, fixture.Sessions, Substitute.For<IPasswordService>(), fixture.proofs, gate), fixture.Accounts, fixture.Sessions, fixture.Enforcer, mediator, catalog);
			var claims = new AccountClaimsService(capabilities, fixture.cache, new AccountClaimsInvalidator(fixture.cache, new ObjectVersions()), NullLogger<AccountClaimsService>.Instance);
			fixture.Controller = new(fixture.Accounts, fixture.Sessions, claims, capabilities, fixture.Audit, mediator, new(fixture.Sessions, fixture.Accounts), NullLogger<AdminAccountsController>.Instance, security, new(fixture.Accounts, fixture.Registry, capabilities))
			{ ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, fixture.Actor.Id!)], AccountSessionAuthenticationHandler.SchemeName)) } } };
			fixture.Controller.Request.Headers.Authorization = "Bearer " + fixture.Source;
			return fixture;
		}
		public string Grant() => proofs.Issue(Actor.Id!, Source, AccountSecurityOperation.StaffRecovery, new("password", CredentialEpoch: Actor.CredentialEpoch)).Expect<RecentAccountProof>().Grant;
		public void Dispose() => cache.Dispose();
	}
}
