using Mediator;
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
		Account("wiz", AdministrativeCapabilityTests.Player(2));
		Account("mod", AdministrativeCapabilityTests.Player(3));
		Account("pl", AdministrativeCapabilityTests.Player(4));
		Account("pl2", AdministrativeCapabilityTests.Player(5));
		registry.AssignRoleToAccountAsync("mod", "moderator").GetAwaiter().GetResult();
		registry.AssignRoleToObjectAsync(2, BuiltInRoles.WizardSlug).GetAwaiter().GetResult();

		var resolver = new PermissionResolver();
		var capabilities = new AdministrativeCapabilityService(accounts, registry, resolver);
		var invalidator = Substitute.For<IAccountClaimsInvalidator>();
		return new World(new RoleManagementService(registry, accounts, capabilities, resolver, invalidator, Substitute.For<IMediator>()), registry, invalidator);
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

	private static RoleActor A(string accountId) => new CapabilityActor(accountId);

	private static RoleDraft Draft(string slug, int priority, params string[] allows)
		=> new(slug, slug, "Staff", null, priority, allows.ToDictionary(a => a, _ => PermissionState.Allow));

	[Test]
	public async Task ChangesNeedRolesAdmin()
	{
		var world = Build();
		await Refused(world.Service.AssignAsync(A("pl"), "pl2", "helper"), RoleRefusalKind.Forbidden);
		await Refused(world.Service.SaveRoleAsync(A("pl"), Draft("x", 1)), RoleRefusalKind.Forbidden);
	}

	[Test]
	public async Task ModeratorAssignsOnlyRolesBelowItself()
	{
		var world = Build();
		await Accepted(world.Service.AssignAsync(A("mod"), "pl", "helper"));
		await Assert.That(world.Registry.AssignedTo("pl")).Contains("helper");
		await world.Invalidator.Received().InvalidateAsync("pl", Arg.Any<CancellationToken>());
		await Refused(world.Service.AssignAsync(A("mod"), "pl2", "moderator"), RoleRefusalKind.Forbidden);
	}

	[Test]
	public async Task NobodyChangesAnAccountAtOrAboveThem()
	{
		var world = Build();
		await Refused(world.Service.AssignAsync(A("mod"), "wiz", "helper"), RoleRefusalKind.Forbidden);
		await Refused(world.Service.SetOverridesAsync(A("mod"), "wiz", [PortalPermission.WikiEdit], PermissionState.Deny), RoleRefusalKind.Forbidden);
		await Refused(world.Service.AssignAsync(A("wiz"), "owner", "helper"), RoleRefusalKind.Forbidden);
	}

	[Test]
	public async Task NobodyButTheOwnerChangesThemselves()
	{
		var world = Build();
		await Accepted(world.Service.AssignAsync(A("owner"), "mod", "helper"));
		var refusal = await Refused(world.Service.UnassignAsync(A("mod"), "mod", "helper"), RoleRefusalKind.Forbidden);
		await Assert.That(refusal.Message).Contains("your own");
		await Accepted(world.Service.AssignAsync(A("owner"), "owner", "helper"));
	}

	/// <summary>A wizard changes its own account's roles below its highest one, but not wizard itself or god.</summary>
	[Test]
	public async Task AWizardChangesItsOwnRolesBelowItsHighest()
	{
		var world = Build();
		await Accepted(world.Service.AssignAsync(A("wiz"), "wiz", BuiltInRoles.ApprovedSlug));
		await Assert.That(world.Registry.AssignedTo("wiz")).Contains(BuiltInRoles.ApprovedSlug);
		await Accepted(world.Service.UnassignAsync(A("wiz"), "wiz", BuiltInRoles.ApprovedSlug));
		await Assert.That(world.Registry.AssignedTo("wiz")).DoesNotContain(BuiltInRoles.ApprovedSlug);
		await Refused(world.Service.AssignAsync(A("wiz"), "wiz", BuiltInRoles.WizardSlug), RoleRefusalKind.Forbidden);
		await Refused(world.Service.AssignAsync(A("wiz"), "wiz", BuiltInRoles.GodSlug), RoleRefusalKind.Invalid);
		await Refused(world.Service.SetOverridesAsync(A("wiz"), "wiz", [PortalPermission.WikiEdit], PermissionState.Deny), RoleRefusalKind.Forbidden);
	}

	[Test]
	public async Task RolesArePlacedAndEditedOnlyBelowTheManager()
	{
		var world = Build();
		await Accepted(world.Service.SaveRoleAsync(A("wiz"), Draft("storyteller", 29, PortalPermission.WikiDelete)));
		await Refused(world.Service.SaveRoleAsync(A("wiz"), Draft("rival", 30)), RoleRefusalKind.Forbidden);
		await Refused(world.Service.EditRoleAsync(A("mod"), "storyteller", role => Draft("storyteller", 5)), RoleRefusalKind.Forbidden);
		await Accepted(world.Service.EditRoleAsync(A("wiz"), "storyteller", role => Draft("storyteller", 5)));
		await Assert.That(await world.Registry.GetRoleAsync("storyteller") is SharpRole { Priority: 5 }).IsTrue();
	}

	[Test]
	public async Task ManagersAllowOnlyWhatTheyHold()
	{
		var world = Build();
		var refusal = await Refused(world.Service.SaveRoleAsync(A("wiz"), Draft("ops", 20, PortalPermission.ServerAdmin)), RoleRefusalKind.Forbidden);
		await Assert.That(refusal.Message).Contains(PortalPermission.ServerAdmin);
		await Refused(world.Service.SaveRoleAsync(A("wiz"), Draft("ops", 20, PortalPermission.Administrator)), RoleRefusalKind.Forbidden);
		await Refused(world.Service.SetOverridesAsync(A("mod"), "pl", [PortalPermission.ConfigAdmin], PermissionState.Allow), RoleRefusalKind.Forbidden);
		await Accepted(world.Service.SetOverridesAsync(A("mod"), "pl", [PortalPermission.ConfigAdmin], PermissionState.Deny));
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
		await Accepted(world.Service.EditRoleAsync(A("wiz"), "legacy", role => new RoleDraft(role.Slug, "Renamed", "Staff", role.Color, role.Priority, role.Permissions)));
	}

	[Test]
	public async Task SystemRolesKeepSlugAndPriorityAndImplicitOnesAreNeverAssigned()
	{
		var world = Build();
		await Refused(world.Service.DeleteRoleAsync(A("owner"), "player"), RoleRefusalKind.Invalid);
		await Refused(world.Service.EditRoleAsync(A("owner"), "player", role => Draft("player", 11)), RoleRefusalKind.Invalid);
		foreach (var implicitRole in new[] { BuiltInRoles.EveryoneSlug, BuiltInRoles.PlayerSlug, BuiltInRoles.GodSlug })
			await Refused(world.Service.AssignAsync(A("owner"), "pl", implicitRole), RoleRefusalKind.Invalid);
		await Accepted(world.Service.AssignAsync(A("owner"), "pl", BuiltInRoles.WizardSlug));
		await Accepted(world.Service.EditRoleAsync(A("wiz"), "player",
			role => new RoleDraft(role.Slug, role.Name, role.Category, role.Color, role.Priority,
				new Dictionary<string, PermissionState>(role.Permissions) { [PortalPermission.WikiDelete] = PermissionState.Allow })));
		await Refused(world.Service.EditRoleAsync(A("wiz"), "wizard", role => Draft("wizard", 30)), RoleRefusalKind.Forbidden);
	}

	[Test]
	public async Task TheOwnerIsExemptFromTheHierarchy()
	{
		var world = Build();
		await Accepted(world.Service.SaveRoleAsync(A("owner"), Draft("council", 35, PortalPermission.ServerAdmin)));
		await Accepted(world.Service.AssignAsync(A("owner"), "wiz", "council"));
		await Accepted(world.Service.SetOverridesAsync(A("owner"), "wiz", [PortalPermission.WikiEdit], PermissionState.Deny));
	}

	[Test]
	public async Task OverridesAreAllOrNone()
	{
		var world = Build();
		await Refused(world.Service.SetOverridesAsync(A("wiz"), "pl", [PortalPermission.WikiDelete, PortalPermission.ServerAdmin], PermissionState.Allow),
			RoleRefusalKind.Forbidden);
		await Refused(world.Service.SetOverridesAsync(A("wiz"), "pl", [PortalPermission.WikiDelete, PortalPermission.Administrator], PermissionState.Allow),
			RoleRefusalKind.Invalid);
		await Assert.That(await world.Registry.GetAccountOverridesAsync("pl")).IsEmpty();
		await Accepted(world.Service.SetOverridesAsync(A("wiz"), "pl", [PortalPermission.WikiDelete, PortalPermission.MediaAdmin], PermissionState.Allow));
		await Assert.That((await world.Registry.GetAccountOverridesAsync("pl")).Count).IsEqualTo(2);
	}

	[Test]
	public async Task AnAssignedSystemRoleCanBeTakenAway()
	{
		var world = Build();
		await world.Registry.AssignRoleToAccountAsync("pl", "royalty");
		await Refused(world.Service.UnassignAsync(A("owner"), "pl", BuiltInRoles.EveryoneSlug), RoleRefusalKind.Invalid);
		await Accepted(world.Service.UnassignAsync(A("owner"), "pl", "royalty"));
		await Assert.That(world.Registry.AssignedTo("pl")).DoesNotContain("royalty");
	}

	[Test]
	public async Task AdministratorIsNeverAnOverride()
	{
		var world = Build();
		await Refused(world.Service.SetOverridesAsync(A("owner"), "pl", [PortalPermission.Administrator], PermissionState.Allow), RoleRefusalKind.Invalid);
		await Refused(world.Service.SetOverridesAsync(A("owner"), "pl", ["no.such"], PermissionState.Allow), RoleRefusalKind.Invalid);
	}

	[Test]
	public async Task DeletingARoleTakesItFromEveryone()
	{
		var world = Build();
		await Accepted(world.Service.AssignAsync(A("wiz"), "pl", "helper"));
		await Accepted(world.Service.DeleteRoleAsync(A("wiz"), "helper"));
		await Assert.That(world.Registry.AssignedTo("pl")).DoesNotContain("helper");
		await Assert.That(await world.Registry.GetRoleAsync("helper") is NotFound).IsTrue();
	}

	[Test]
	public async Task MalformedRolesAreRefused()
	{
		var world = Build();
		await Refused(world.Service.SaveRoleAsync(A("owner"), Draft("Bad Slug", 1)), RoleRefusalKind.Invalid);
		await Refused(world.Service.SaveRoleAsync(A("owner"), Draft("ok", 1, "no.such")), RoleRefusalKind.Invalid);
		await Refused(world.Service.SaveRoleAsync(A("owner"), Draft("ok", 1) with { Color = "blue" }), RoleRefusalKind.Invalid);
		await Refused(world.Service.EditRoleAsync(A("owner"), "missing", role => Draft("missing", 1)), RoleRefusalKind.NotFound);
	}

	[Test]
	public async Task CustomPermissionIsDefinedByARolesAdminAndGrantedLikeAnyOther()
	{
		var world = Build();
		await Assert.That(await world.Service.DefinePermissionAsync(A("pl"), "scene.close", "Staff", "Finish any scene") is RoleRefusal { Kind: RoleRefusalKind.Forbidden }).IsTrue();
		await Assert.That(await world.Service.DefinePermissionAsync(A("mod"), "Scene.Close", " staff ", "Finish any scene") is CustomPermission { Scope: "scene.close", Category: "Staff" }).IsTrue();
		await Assert.That((await world.Registry.GetCustomPermissionsAsync()).Select(p => p.Description)).IsEquivalentTo(["Finish any scene"]);

		// The moderator defined it but does not hold it, so cannot grant it; a wizard may.
		await Refused(world.Service.SaveRoleAsync(A("mod"), Draft("closers", 2, "scene.close")), RoleRefusalKind.Forbidden);
		await Accepted(world.Service.SaveRoleAsync(A("wiz"), Draft("closers", 2, "scene.close")));
		await Accepted(world.Service.AssignAsync(A("wiz"), "pl", "closers"));
		await Accepted(world.Service.SetOverridesAsync(A("wiz"), "pl2", ["scene.close"], PermissionState.Allow));
	}

	[Test]
	public async Task RolesAndCustomPermissionsGoInACategoryThatExists()
	{
		var world = Build();
		foreach (var category in new[] { "", "   ", "Scene staff" })
		{
			await Refused(world.Service.SaveRoleAsync(A("owner"), Draft("ok", 1) with { Category = category }), RoleRefusalKind.Invalid);
			await Assert.That(await world.Service.DefinePermissionAsync(A("owner"), "scene.close", category, "") is RoleRefusal { Kind: RoleRefusalKind.Invalid })
				.IsTrue().Because($"'{category}'");
		}

		var missing = await Refused(world.Service.SaveRoleAsync(A("owner"), Draft("ok", 1) with { Category = "Scene staff" }), RoleRefusalKind.Invalid);
		await Assert.That(missing.Message).Contains("No role category named 'Scene staff'. Create the category first: @role/category/create Scene staff=");

		await Assert.That(await world.Service.CreateCategoryAsync(A("owner"), CategoryKind.Role, "Scene staff", "Who runs scenes") is RoleCategory).IsTrue();
		await Accepted(world.Service.SaveRoleAsync(A("owner"), Draft("ok", 1) with { Category = " scene STAFF " }));
		await Assert.That((await world.Registry.GetRoleAsync("ok")).Expect<SharpRole>().Category).IsEqualTo("Scene staff");

		// The lists are separate: a role category does not take a permission.
		var permission = await world.Service.DefinePermissionAsync(A("owner"), "scene.close", "Scene staff", "");
		await Assert.That(permission is RoleRefusal { Message: var message } && message.Contains("@permission/category/create Scene staff=")).IsTrue();
		await Assert.That(await world.Service.CreateCategoryAsync(A("owner"), CategoryKind.Permission, "Scene staff", "Scene permissions") is RoleCategory).IsTrue();
		await Assert.That(await world.Service.DefinePermissionAsync(A("owner"), "scene.close", "scene staff", "") is CustomPermission { Category: "Scene staff" }).IsTrue();
	}

	[Test]
	public async Task RoleDisplayNamesUsePlayerNameCharacters()
	{
		var world = Build();
		foreach (var name in new[] { "Bad=Name", "[Staff]", "50% off", "Café", new string('x', RoleNames.MaxLength + 1) })
			await Refused(world.Service.SaveRoleAsync(A("owner"), Draft("ok", 1) with { Name = name }), RoleRefusalKind.Invalid);
		await Accepted(world.Service.SaveRoleAsync(A("owner"), Draft("ok", 1) with { Name = "Scene Runner's Guild" }));
	}

	[Test]
	public async Task CategoriesAreCreatedDescribedRenamedAndDeleted()
	{
		var world = Build();
		const CategoryKind roles = CategoryKind.Role;
		const CategoryKind permissions = CategoryKind.Permission;
		await Assert.That(await world.Service.CreateCategoryAsync(A("pl"), roles, "Scenes", "Scene running") is RoleRefusal { Kind: RoleRefusalKind.Forbidden }).IsTrue();
		foreach (var name in new[] { "", "Staff/Helpers", "Bad=Name", "[x]", new string('x', Categories.MaxNameLength + 1) })
			await Assert.That(await world.Service.CreateCategoryAsync(A("mod"), roles, name, "Scene running") is RoleRefusal { Kind: RoleRefusalKind.Invalid }).IsTrue().Because($"'{name}'");
		await Assert.That(await world.Service.CreateCategoryAsync(A("mod"), roles, "Scenes", " ") is RoleRefusal { Kind: RoleRefusalKind.Invalid }).IsTrue();

		await Assert.That(await world.Service.CreateCategoryAsync(A("mod"), roles, "Scenes", "Scene running") is RoleCategory { Name: "Scenes" }).IsTrue();
		await Assert.That(await world.Service.CreateCategoryAsync(A("mod"), roles, "SCENES", "Again") is RoleRefusal { Kind: RoleRefusalKind.Invalid }).IsTrue();
		await Assert.That(await world.Service.CreateCategoryAsync(A("mod"), permissions, "Scenes", "Scene permissions") is RoleCategory { Name: "Scenes" }).IsTrue();
		await Assert.That(await world.Service.DescribeCategoryAsync(A("mod"), roles, "scenes", "Who runs scenes") is RoleCategory { Name: "Scenes", Description: "Who runs scenes" }).IsTrue();
		await Assert.That(await world.Service.DescribeCategoryAsync(A("mod"), roles, "Nope", "x") is RoleRefusal { Kind: RoleRefusalKind.NotFound }).IsTrue();

		await world.Service.DefinePermissionAsync(A("owner"), "scene.close", "Scenes", "");
		await Accepted(world.Service.SaveRoleAsync(A("owner"), Draft("closers", 2) with { Category = "Scenes" }));
		var inUse = await Refused(world.Service.DeleteCategoryAsync(A("mod"), roles, "Scenes"), RoleRefusalKind.Invalid);
		await Assert.That(inUse.Message).Contains("closers").And.DoesNotContain("scene.close");

		// Renaming a role category moves its roles and leaves the permission list alone.
		await Assert.That(await world.Service.RenameCategoryAsync(A("mod"), roles, "Scenes", "Staff") is RoleRefusal { Kind: RoleRefusalKind.Invalid }).IsTrue();
		await Assert.That(await world.Service.RenameCategoryAsync(A("mod"), roles, "Scenes", "Scene staff") is RoleCategory { Name: "Scene staff", Description: "Who runs scenes" }).IsTrue();
		await Assert.That((await world.Registry.GetRoleAsync("closers")).Expect<SharpRole>().Category).IsEqualTo("Scene staff");
		await Assert.That((await world.Registry.GetCustomPermissionsAsync()).Single().Category).IsEqualTo("Scenes");
		await Assert.That((await world.Registry.GetCategoriesAsync(roles)).Select(c => c.Name)).IsEquivalentTo(["Scene staff", "Staff", "System"]);
		await Assert.That((await world.Registry.GetCategoriesAsync(permissions)).Select(c => c.Name)).IsEquivalentTo(["Scenes", "Staff"]);

		await Assert.That(await world.Service.RenameCategoryAsync(A("mod"), permissions, "Scenes", "Scene tools") is RoleCategory).IsTrue();
		await Assert.That((await world.Registry.GetCustomPermissionsAsync()).Single().Category).IsEqualTo("Scene tools");
		await Assert.That((await world.Registry.GetRoleAsync("closers")).Expect<SharpRole>().Category).IsEqualTo("Scene staff");

		await Accepted(world.Service.EditRoleAsync(A("owner"), "closers", role => Draft("closers", 2) with { Category = "Staff" }));
		await Accepted(world.Service.DeleteCategoryAsync(A("mod"), roles, "scene STAFF"));
		await Assert.That((await world.Registry.GetCategoriesAsync(roles)).Select(c => c.Name)).IsEquivalentTo(["Staff", "System"]);
		await Refused(world.Service.DeleteCategoryAsync(A("mod"), permissions, "Scene tools"), RoleRefusalKind.Invalid);
	}

	[Test]
	public async Task CustomPermissionNamesAreChecked()
	{
		var world = Build();
		foreach (var name in new[] { "scene", "wiki.read", "game.thing", "control.x", "has space.x" })
			await Assert.That(await world.Service.DefinePermissionAsync(A("owner"), name, "Staff", "") is RoleRefusal { Kind: RoleRefusalKind.Invalid }).IsTrue().Because(name);
		await Assert.That(await world.Service.DefinePermissionAsync(A("owner"), "scene.close", "Staff", new string('x', RoleManagementService.MaxDescriptionLength + 1)) is RoleRefusal { Kind: RoleRefusalKind.Invalid }).IsTrue();
		// An undefined name is no permission at all.
		await Refused(world.Service.SaveRoleAsync(A("owner"), Draft("ok", 1, "scene.close")), RoleRefusalKind.Invalid);
		await Refused(world.Service.SetOverridesAsync(A("owner"), "pl", ["scene.close"], PermissionState.Allow), RoleRefusalKind.Invalid);
	}

	[Test]
	public async Task RemovingACustomPermissionTakesEverySettingOfIt()
	{
		var world = Build();
		await world.Service.DefinePermissionAsync(A("owner"), "scene.close", "Staff", "");
		await Accepted(world.Service.SaveRoleAsync(A("owner"), Draft("closers", 2, "scene.close", PortalPermission.WikiEdit)));
		await Accepted(world.Service.SetOverridesAsync(A("owner"), "pl", ["scene.close"], PermissionState.Deny));

		await Refused(world.Service.RemovePermissionAsync(A("mod"), "scene.close"), RoleRefusalKind.Forbidden);
		await Refused(world.Service.RemovePermissionAsync(A("owner"), "wiki.read"), RoleRefusalKind.NotFound);
		await Accepted(world.Service.RemovePermissionAsync(A("wiz"), "scene.close"));

		await Assert.That(await world.Registry.GetCustomPermissionsAsync()).IsEmpty();
		var role = (await world.Registry.GetRoleAsync("closers")).Expect<SharpRole>();
		await Assert.That(role.Permissions.Keys).IsEquivalentTo([PortalPermission.WikiEdit]);
		await Assert.That(await world.Registry.GetAccountOverridesAsync("pl")).IsEmpty();
	}
}
