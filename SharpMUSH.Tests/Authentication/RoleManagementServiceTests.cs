using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// <see cref="RoleManagementService"/>'s rules, which <c>@role</c> and the portal share: roles.admin,
/// Discord's hierarchy (only below your highest role), granting only what you hold, no self-changes,
/// system roles fixed, and the owner exempt.
/// </summary>
public class RoleManagementServiceTests
{
	private sealed record World(RoleManagementService Service, InMemoryRoleRegistry Registry, IAccountClaimsInvalidator Invalidator);

	/// <summary>
	/// Accounts: <c>owner</c> plays #1, <c>wiz</c> a WIZARD, <c>mod</c> a player holding moderator,
	/// <c>pl</c> and <c>pl2</c> plain players.
	/// </summary>
	private static World Build()
	{
		var registry = InMemoryRoleRegistry.Seeded();
		var accounts = Substitute.For<IAccountService>();
		void Account(string id, SharpPlayer character)
		{
			accounts.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new SharpAccount { Id = id, Username = id, PasswordHash = "" });
			accounts.GetCharactersAsync(id, Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([character]));
		}

		Account("owner", AdministrativeCapabilityTests.Player(1));
		Account("wiz", AdministrativeCapabilityTests.Player(2, flags: ["WIZARD"]));
		Account("mod", AdministrativeCapabilityTests.Player(3));
		Account("pl", AdministrativeCapabilityTests.Player(4));
		Account("pl2", AdministrativeCapabilityTests.Player(5));
		registry.AssignRoleToAccountAsync("mod", "moderator").GetAwaiter().GetResult();

		var resolver = new PermissionResolver();
		var capabilities = new AdministrativeCapabilityService(accounts, registry, new RoleDerivationService(), resolver);
		var invalidator = Substitute.For<IAccountClaimsInvalidator>();
		return new World(new RoleManagementService(registry, accounts, capabilities, resolver, invalidator), registry, invalidator);
	}

	private static RoleRefusal? RefusalOf(RoleOutcome<Success> outcome) => outcome switch
	{
		RoleRefusal refusal => refusal,
		_ => null
	};

	private static RoleRefusal? RefusalOf(RoleOutcome<SharpRole> outcome) => outcome switch
	{
		RoleRefusal refusal => refusal,
		_ => null
	};

	private static async Task<RoleRefusal> Refused(Task<RoleOutcome<Success>> outcome, RoleRefusalKind kind)
		=> await ExpectRefusal(RefusalOf(await outcome), kind);

	private static async Task<RoleRefusal> Refused(Task<RoleOutcome<SharpRole>> outcome, RoleRefusalKind kind)
		=> await ExpectRefusal(RefusalOf(await outcome), kind);

	private static async Task Accepted(Task<RoleOutcome<Success>> outcome)
		=> await ExpectAccepted(RefusalOf(await outcome));

	private static async Task Accepted(Task<RoleOutcome<SharpRole>> outcome)
		=> await ExpectAccepted(RefusalOf(await outcome));

	private static async Task<RoleRefusal> ExpectRefusal(RoleRefusal? refusal, RoleRefusalKind kind)
	{
		var found = refusal ?? throw new InvalidOperationException("Expected a refusal, but the change was accepted.");
		await Assert.That(found.Kind).IsEqualTo(kind).Because(found.Message);
		return found;
	}

	private static async Task ExpectAccepted(RoleRefusal? refusal)
		=> await Assert.That(refusal).IsNull().Because(refusal?.Message ?? "");

	private static RoleDraft Draft(string slug, int priority, params string[] allows)
		=> new(slug, slug, null, priority, allows.ToDictionary(a => a, _ => PermissionState.Allow));

	[Test]
	public async Task ChangesNeedRolesAdmin()
	{
		var world = Build();
		await Refused(world.Service.AssignAsync(new("pl"), "pl2", "helper"), RoleRefusalKind.Forbidden);
		await Refused(world.Service.SaveRoleAsync(new("pl"), Draft("x", 1)), RoleRefusalKind.Forbidden);
	}

	[Test]
	public async Task ModeratorAssignsOnlyRolesBelowItself()
	{
		var world = Build();
		await Accepted(world.Service.AssignAsync(new("mod"), "pl", "helper"));
		await Assert.That(world.Registry.AssignedTo("pl")).Contains("helper");
		await world.Invalidator.Received().InvalidateAsync("pl", Arg.Any<CancellationToken>());
		await Refused(world.Service.AssignAsync(new("mod"), "pl2", "moderator"), RoleRefusalKind.Forbidden);
	}

	[Test]
	public async Task NobodyChangesAnAccountAtOrAboveThem()
	{
		var world = Build();
		await Refused(world.Service.AssignAsync(new("mod"), "wiz", "helper"), RoleRefusalKind.Forbidden);
		await Refused(world.Service.SetOverridesAsync(new("mod"), "wiz", [PortalPermission.WikiEdit], PermissionState.Deny), RoleRefusalKind.Forbidden);
		await Refused(world.Service.AssignAsync(new("wiz"), "owner", "helper"), RoleRefusalKind.Forbidden);
	}

	[Test]
	public async Task NobodyButTheOwnerChangesThemselves()
	{
		var world = Build();
		await Accepted(world.Service.AssignAsync(new("owner"), "mod", "helper"));
		var refusal = await Refused(world.Service.UnassignAsync(new("mod"), "mod", "helper"), RoleRefusalKind.Forbidden);
		await Assert.That(refusal.Message).Contains("your own");
		await Accepted(world.Service.AssignAsync(new("owner"), "owner", "helper"));
	}

	[Test]
	public async Task RolesArePlacedAndEditedOnlyBelowTheManager()
	{
		var world = Build();
		await Accepted(world.Service.SaveRoleAsync(new("wiz"), Draft("storyteller", 29, PortalPermission.WikiDelete)));
		await Refused(world.Service.SaveRoleAsync(new("wiz"), Draft("rival", 30)), RoleRefusalKind.Forbidden);
		await Refused(world.Service.EditRoleAsync(new("mod"), "storyteller", role => Draft("storyteller", 5)), RoleRefusalKind.Forbidden);
		await Accepted(world.Service.EditRoleAsync(new("wiz"), "storyteller", role => Draft("storyteller", 5)));
		await Assert.That(await world.Registry.GetRoleAsync("storyteller") is SharpRole { Priority: 5 }).IsTrue();
	}

	[Test]
	public async Task ManagersAllowOnlyWhatTheyHold()
	{
		var world = Build();
		var refusal = await Refused(world.Service.SaveRoleAsync(new("wiz"), Draft("ops", 20, PortalPermission.ServerAdmin)), RoleRefusalKind.Forbidden);
		await Assert.That(refusal.Message).Contains(PortalPermission.ServerAdmin);
		await Refused(world.Service.SaveRoleAsync(new("wiz"), Draft("ops", 20, PortalPermission.Administrator)), RoleRefusalKind.Forbidden);
		await Refused(world.Service.SetOverridesAsync(new("mod"), "pl", [PortalPermission.ConfigAdmin], PermissionState.Allow), RoleRefusalKind.Forbidden);
		await Accepted(world.Service.SetOverridesAsync(new("mod"), "pl", [PortalPermission.ConfigAdmin], PermissionState.Deny));
	}

	[Test]
	public async Task AnExistingGrantTheManagerLacksMayStay()
	{
		var world = Build();
		world.Registry.Add(new SharpRole
		{
			Slug = "legacy", Name = "Legacy", Priority = 5,
			Permissions = new() { [PortalPermission.ServerAdmin] = PermissionState.Allow }
		});
		await Accepted(world.Service.EditRoleAsync(new("wiz"), "legacy", role => new RoleDraft(role.Slug, "Renamed", role.Color, role.Priority, role.Permissions)));
	}

	[Test]
	public async Task SystemRolesKeepSlugAndPriorityAndAreNeverAssigned()
	{
		var world = Build();
		await Refused(world.Service.DeleteRoleAsync(new("owner"), "player"), RoleRefusalKind.Invalid);
		await Refused(world.Service.EditRoleAsync(new("owner"), "player", role => Draft("player", 11)), RoleRefusalKind.Invalid);
		await Refused(world.Service.AssignAsync(new("owner"), "pl", "wizard"), RoleRefusalKind.Invalid);
		await Accepted(world.Service.EditRoleAsync(new("wiz"), "player",
			role => new RoleDraft(role.Slug, role.Name, role.Color, role.Priority,
				new Dictionary<string, PermissionState>(role.Permissions) { [PortalPermission.WikiDelete] = PermissionState.Allow })));
		await Refused(world.Service.EditRoleAsync(new("wiz"), "wizard", role => Draft("wizard", 30)), RoleRefusalKind.Forbidden);
	}

	[Test]
	public async Task TheOwnerIsExemptFromTheHierarchy()
	{
		var world = Build();
		await Accepted(world.Service.SaveRoleAsync(new("owner"), Draft("council", 35, PortalPermission.ServerAdmin)));
		await Accepted(world.Service.AssignAsync(new("owner"), "wiz", "council"));
		await Accepted(world.Service.SetOverridesAsync(new("owner"), "wiz", [PortalPermission.WikiEdit], PermissionState.Deny));
	}

	[Test]
	public async Task OverridesAreAllOrNone()
	{
		var world = Build();
		await Refused(world.Service.SetOverridesAsync(new("wiz"), "pl", [PortalPermission.WikiDelete, PortalPermission.ServerAdmin], PermissionState.Allow),
			RoleRefusalKind.Forbidden);
		await Refused(world.Service.SetOverridesAsync(new("wiz"), "pl", [PortalPermission.WikiDelete, PortalPermission.Administrator], PermissionState.Allow),
			RoleRefusalKind.Invalid);
		await Assert.That(await world.Registry.GetAccountOverridesAsync("pl")).IsEmpty();
		await Accepted(world.Service.SetOverridesAsync(new("wiz"), "pl", [PortalPermission.WikiDelete, PortalPermission.MediaAdmin], PermissionState.Allow));
		await Assert.That((await world.Registry.GetAccountOverridesAsync("pl")).Count).IsEqualTo(2);
	}

	[Test]
	public async Task AnAssignedSystemRoleCanBeTakenAway()
	{
		var world = Build();
		await world.Registry.AssignRoleToAccountAsync("pl", "royalty");
		await Refused(world.Service.UnassignAsync(new("owner"), "pl2", "royalty"), RoleRefusalKind.Invalid);
		await Accepted(world.Service.UnassignAsync(new("owner"), "pl", "royalty"));
		await Assert.That(world.Registry.AssignedTo("pl")).DoesNotContain("royalty");
	}

	[Test]
	public async Task AdministratorIsNeverAnOverride()
	{
		var world = Build();
		await Refused(world.Service.SetOverridesAsync(new("owner"), "pl", [PortalPermission.Administrator], PermissionState.Allow), RoleRefusalKind.Invalid);
		await Refused(world.Service.SetOverridesAsync(new("owner"), "pl", ["no.such"], PermissionState.Allow), RoleRefusalKind.Invalid);
	}

	[Test]
	public async Task DeletingARoleTakesItFromEveryone()
	{
		var world = Build();
		await Accepted(world.Service.AssignAsync(new("wiz"), "pl", "helper"));
		await Accepted(world.Service.DeleteRoleAsync(new("wiz"), "helper"));
		await Assert.That(world.Registry.AssignedTo("pl")).DoesNotContain("helper");
		await Assert.That(await world.Registry.GetRoleAsync("helper") is NotFound).IsTrue();
	}

	[Test]
	public async Task MalformedRolesAreRefused()
	{
		var world = Build();
		await Refused(world.Service.SaveRoleAsync(new("owner"), Draft("Bad Slug", 1)), RoleRefusalKind.Invalid);
		await Refused(world.Service.SaveRoleAsync(new("owner"), Draft("ok", 1, "no.such")), RoleRefusalKind.Invalid);
		await Refused(world.Service.SaveRoleAsync(new("owner"), Draft("ok", 1) with { Color = "blue" }), RoleRefusalKind.Invalid);
		await Refused(world.Service.EditRoleAsync(new("owner"), "missing", role => Draft("missing", 1)), RoleRefusalKind.NotFound);
	}
}
