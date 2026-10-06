using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.RecurringJobs;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>What a package's roles, permissions, categories and jobs do on install, upgrade and removal.</summary>
public class PackageDeclarationPlannerTests
{
	private static readonly PackageCategorySpec Requests = new("Requests", "The request queue.");
	private static readonly PackagePermissionSpec Handle = new("requests.handle", "Requests", "Work on any request.");

	private static PackageRoleSpec Handler(int priority = 11, PermissionState state = PermissionState.Allow, string name = "Handler")
		=> new("handler", name, "Requests", null, priority, new Dictionary<string, PermissionState> { ["requests.handle"] = state });

	private static readonly PackageJobSpec Sweep = new("sweep", new PackageRef(PackageRefKind.Internal, "desk"), "JOB`SWEEP", "0 4 * * *", "UTC", "Sweep.");

	private static PackageDeclarations Declared(
		IReadOnlyList<PackageRoleSpec>? roles = null, IReadOnlyList<PackagePermissionSpec>? permissions = null, IReadOnlyList<PackageJobSpec>? jobs = null)
		=> new([Requests], [Requests], permissions ?? [Handle], roles ?? [Handler()], jobs ?? []);

	private static PackageDeclarationLiveState Live(
		IReadOnlyList<SharpRole>? roles = null,
		IReadOnlyList<CustomPermission>? permissions = null,
		IReadOnlyList<RoleCategory>? categories = null,
		IReadOnlySet<string>? held = null,
		IReadOnlyDictionary<(PackageDeclarationKind, string), string>? others = null,
		IReadOnlyList<RecurringJob>? jobs = null)
		=> new(
			[.. Categories.RoleSeeds, .. categories ?? []],
			[.. Categories.PermissionSeeds, .. categories ?? []],
			permissions ?? [],
			roles ?? [],
			held ?? new HashSet<string>(),
			others ?? new Dictionary<(PackageDeclarationKind, string), string>(),
			jobs ?? []);

	private static SharpRole Role(PackageRoleSpec spec) => new()
	{
		Slug = spec.Slug, Name = spec.Name, Category = spec.Category, Priority = spec.Priority,
		Permissions = new Dictionary<string, PermissionState>(spec.Permissions)
	};

	private static CustomPermission Permission(PackagePermissionSpec spec) => new(spec.Name, spec.Category, spec.Description, 1);

	private static RoleCategory Category(PackageCategorySpec spec) => new(spec.Name, spec.Description, 1);

	private static PackageDeclarationAction ActionOf(PackageDeclarationPlan plan, PackageDeclarationKind kind, string name)
		=> plan.Changes.Single(c => c.Kind == kind && c.Name == name).Action;

	[Test]
	public async Task AFreshInstallCreatesAndOwnsEverything()
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(jobs: [Sweep]), null, Live(), 5);

		await Assert.That(plan.IsBlocked).IsFalse();
		await Assert.That(plan.Changes.All(c => c.Action == PackageDeclarationAction.Create)).IsTrue();
		await Assert.That(plan.CategoryWrites.Count).IsEqualTo(2);
		await Assert.That(plan.PermissionWrites.Single().Scope).IsEqualTo("requests.handle");
		await Assert.That(plan.RoleWrites.Single().Permissions["requests.handle"]).IsEqualTo(PermissionState.Allow);
		await Assert.That(plan.Jobs.Single()).IsEqualTo(Sweep);
		await Assert.That(plan.Owned.Roles.Single().Slug).IsEqualTo("handler");
		await Assert.That(plan.Owned.Permissions.Single()).IsEqualTo(Handle);
		await Assert.That(plan.Owned.RoleCategories.Single()).IsEqualTo(Requests);
		await Assert.That(plan.Owned.Jobs.Single()).IsEqualTo(Sweep);
	}

	[Test]
	public async Task WhatTheGameAlreadyHasIsLeftAlone()
	{
		var theirs = Role(Handler(name: "Their Handler"));
		var plan = PackageDeclarationPlanner.Plan(Declared(), null,
			Live([theirs], [Permission(Handle)], [Category(Requests)]), 5);

		await Assert.That(plan.IsBlocked).IsFalse();
		await Assert.That(ActionOf(plan, PackageDeclarationKind.Role, "handler")).IsEqualTo(PackageDeclarationAction.Existing);
		await Assert.That(ActionOf(plan, PackageDeclarationKind.Permission, "requests.handle")).IsEqualTo(PackageDeclarationAction.Existing);
		await Assert.That(plan.RoleWrites).IsEmpty();
		await Assert.That(plan.PermissionWrites).IsEmpty();
		await Assert.That(plan.Owned.Roles).IsEmpty();
		await Assert.That(plan.Owned.RoleCategories).IsEmpty();
	}

	[Test]
	public async Task AnotherPackagesRoleBlocks()
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(), null,
			Live([Role(Handler())], others: new Dictionary<(PackageDeclarationKind, string), string> { [(PackageDeclarationKind.Role, "handler")] = "helpdesk" }), 5);

		await Assert.That(plan.IsBlocked).IsTrue();
		await Assert.That(plan.Changes.Single(c => c.Action == PackageDeclarationAction.Blocked).Detail!).Contains("helpdesk");
	}

	[Test]
	public async Task ARoleNamingACategoryNobodyHasBlocks()
	{
		var plan = PackageDeclarationPlanner.Plan(
			new PackageDeclarations([], [], [], [Handler() with { Category = "Nowhere", Permissions = new Dictionary<string, PermissionState>() }], []),
			null, Live(), 5);

		await Assert.That(ActionOf(plan, PackageDeclarationKind.Role, "handler")).IsEqualTo(PackageDeclarationAction.Blocked);
	}

	[Test]
	public async Task ARoleNamingAPermissionNobodyDefinesBlocks()
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(permissions: []), null, Live(), 5);

		await Assert.That(ActionOf(plan, PackageDeclarationKind.Role, "handler")).IsEqualTo(PackageDeclarationAction.Blocked);
	}

	[Test]
	public async Task AnUpgradeKeepsWhatTheAdministratorChangedAndTakesWhatThePackageChanged()
	{
		// The administrator renamed the role and denied the permission; the new version raises its priority.
		var live = Role(Handler(name: "Desk Staff", state: PermissionState.Deny));
		var plan = PackageDeclarationPlanner.Plan(Declared([Handler(priority: 14)]), Declared(),
			Live([live], [Permission(Handle)], [Category(Requests)]), 5);

		var role = plan.RoleWrites.Single();
		await Assert.That(ActionOf(plan, PackageDeclarationKind.Role, "handler")).IsEqualTo(PackageDeclarationAction.Update);
		await Assert.That(role.Name).IsEqualTo("Desk Staff");
		await Assert.That(role.Priority).IsEqualTo(14);
		await Assert.That(role.Permissions["requests.handle"]).IsEqualTo(PermissionState.Deny);
		// The baseline is what the package declared, not the merge, so the next upgrade merges against it.
		await Assert.That(plan.Owned.Roles.Single().Priority).IsEqualTo(14);
		await Assert.That(plan.Owned.Roles.Single().Name).IsEqualTo("Handler");
	}

	[Test]
	public async Task AnUpgradeThatChangesAPermissionOnARoleTakesIt()
	{
		var live = Role(Handler());
		live.Permissions["scene.close"] = PermissionState.Allow;
		var plan = PackageDeclarationPlanner.Plan(Declared([Handler(state: PermissionState.Deny)]), Declared(),
			Live([live], [Permission(Handle), new CustomPermission("scene.close", "Staff", "", 1)], [Category(Requests)]), 5);

		var role = plan.RoleWrites.Single();
		await Assert.That(role.Permissions["requests.handle"]).IsEqualTo(PermissionState.Deny);
		await Assert.That(role.Permissions["scene.close"]).IsEqualTo(PermissionState.Allow);
	}

	[Test]
	public async Task RemovingEverythingKeepsWhatTheGameReliesOn()
	{
		// Someone holds the role, so it stays; it still allows the permission, so that stays; and both
		// categories still hold something.
		var plan = PackageDeclarationPlanner.Plan(PackageDeclarations.None, Declared(),
			Live([Role(Handler())], [Permission(Handle)], [Category(Requests)], held: new HashSet<string> { "handler" }), 5);

		await Assert.That(plan.Changes.All(c => c.Action == PackageDeclarationAction.Release)).IsTrue();
		await Assert.That(plan.RoleRemovals).IsEmpty();
		await Assert.That(plan.PermissionRemovals).IsEmpty();
		await Assert.That(plan.CategoryRemovals).IsEmpty();
		await Assert.That(plan.Owned.IsEmpty).IsTrue();
	}

	[Test]
	public async Task RemovingAnUnusedSetRemovesItAll()
	{
		var plan = PackageDeclarationPlanner.Plan(PackageDeclarations.None, Declared(),
			Live([Role(Handler())], [Permission(Handle)], [Category(Requests)]), 5);

		await Assert.That(plan.RoleRemovals).IsEquivalentTo(["handler"]);
		await Assert.That(plan.PermissionRemovals).IsEquivalentTo(["requests.handle"]);
		await Assert.That(plan.CategoryRemovals).IsEquivalentTo([(CategoryKind.Role, "Requests"), (CategoryKind.Permission, "Requests")]);
	}

	[Test]
	public async Task APermissionAnotherRoleSetsIsKept()
	{
		var other = new SharpRole
		{
			Slug = "staff", Name = "Staff", Category = "Staff", Priority = 20,
			Permissions = new Dictionary<string, PermissionState> { ["requests.handle"] = PermissionState.Allow }
		};
		var plan = PackageDeclarationPlanner.Plan(PackageDeclarations.None, Declared(),
			Live([Role(Handler()), other], [Permission(Handle)], [Category(Requests)]), 5);

		await Assert.That(ActionOf(plan, PackageDeclarationKind.Permission, "requests.handle")).IsEqualTo(PackageDeclarationAction.Release);
		await Assert.That(plan.Changes.Single(c => c.Kind == PackageDeclarationKind.Permission).Detail!).Contains("staff");
		await Assert.That(plan.RoleRemovals).IsEquivalentTo(["handler"]);
	}

	[Test]
	public async Task AJobKeepsAnAdministratorsScheduleUntilThePackageChangesIt()
	{
		var live = new RecurringJob("id", "", "#9:1", "#9:1", "JOB`SWEEP", "30 6 * * 1", "Europe/Oslo", "Sweep.", false, 3, null, null, null, "disabled", null, "pkg", "sweep");

		var kept = PackageDeclarationPlanner.Plan(Declared(jobs: [Sweep]), Declared(jobs: [Sweep]), Live(jobs: [live]), 5);
		await Assert.That(kept.Jobs.Single().Schedule).IsEqualTo("30 6 * * 1");
		await Assert.That(kept.Jobs.Single().TimeZone).IsEqualTo("Europe/Oslo");
		await Assert.That(kept.Owned.Jobs.Single()).IsEqualTo(Sweep);

		var moved = PackageDeclarationPlanner.Plan(Declared(jobs: [Sweep with { Schedule = "0 5 * * *" }]), Declared(jobs: [Sweep]), Live(jobs: [live]), 5);
		await Assert.That(moved.Jobs.Single().Schedule).IsEqualTo("0 5 * * *");
		await Assert.That(moved.Jobs.Single().TimeZone).IsEqualTo("UTC");
	}
}
