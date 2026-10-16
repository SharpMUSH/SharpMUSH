using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.OAuth;

namespace SharpMUSH.Tests.OAuth;

/// <summary>
/// Permissions roll up into an app's scopes: the role editor's groups for the built-in ones, a custom permission's
/// category for the rest. An app sees only the permissions its scopes cover.
/// </summary>
public class DelegatedScopesTests
{
	private static readonly Dictionary<string, PermissionState> None = new();
	private static readonly CustomPermission SceneClose = new("scene.close", "Scene Running", "Close any scene.", 0);

	private static ObjectGrants Wizard(int number = 42) => ObjectGrants.For(number, isPlayer: true,
		[.. BuiltInRoles.All, .. BuiltInRoles.Starters, new SharpRole { Slug = "storyteller", Name = "Storyteller", Priority = 10,
			Permissions = new Dictionary<string, PermissionState> { [SceneClose.Scope] = PermissionState.Allow } }],
		[BuiltInRoles.WizardSlug, BuiltInRoles.BuilderSlug, "storyteller"], None, null, [SceneClose]);

	[Test]
	[Arguments(PortalPermission.WikiEdit, DelegatedScopes.Content)]
	[Arguments(PortalPermission.MediaUpload, DelegatedScopes.Content)]
	[Arguments(PortalPermission.ApplicationsAdmin, DelegatedScopes.Build)]
	[Arguments("game.builder", DelegatedScopes.Build)]
	[Arguments(PortalPermission.ConfigAdmin, DelegatedScopes.Manage)]
	[Arguments(PortalPermission.GameWizard, DelegatedScopes.Game)]
	[Arguments(PortalPermission.ControlAll, DelegatedScopes.Game)]
	[Arguments("game.see_all", DelegatedScopes.Game)]
	public async Task EachBuiltInPermission_RollsUpIntoItsGroup(string permission, string scope)
		=> await Assert.That(DelegatedScopes.ScopeOf(permission)).IsEqualTo(scope);

	[Test]
	[Arguments(PortalPermission.SoftcodeUse)]
	[Arguments(PortalPermission.SnapshotCapture)]
	[Arguments(PortalPermission.JobsManageOwn)]
	[Arguments(PortalPermission.QueueControlOwn)]
	public async Task APlayersOwnWork_ComesWithCommands_NotAStaffGroup(string permission)
	{
		await Assert.That(DelegatedScopes.ScopeOf(permission)).IsEqualTo(DelegatedScopes.Commands);
		var player = new HashSet<string>(DelegatedScopes.OwnWork, StringComparer.OrdinalIgnoreCase);
		await Assert.That(DelegatedScopes.Relevant([DelegatedScopes.Profile, DelegatedScopes.Commands, DelegatedScopes.Manage], player,
			new Dictionary<string, string>())).DoesNotContain(DelegatedScopes.Manage);
	}

	[Test]
	public async Task ACustomPermission_RollsUpIntoItsCategory()
	{
		await Assert.That(DelegatedScopes.CategoryScope("Scene Running")).IsEqualTo("scene-running");
		await Assert.That(DelegatedScopes.ScopeOf("scene.close", "Scene Running")).IsEqualTo("scene-running");
		await Assert.That(DelegatedScopes.ScopeOf("scene.close")).IsNull();
	}

	[Test]
	public async Task Administrator_NeedsEveryGroup()
	{
		await Assert.That(DelegatedScopes.Covers([DelegatedScopes.Manage], PortalPermission.Administrator)).IsFalse();
		await Assert.That(DelegatedScopes.Covers(DelegatedScopes.PermissionGroups, PortalPermission.Administrator)).IsTrue();
	}

	[Test]
	public async Task AnAppActingAsAWizard_HoldsOnlyWhatItsScopesCover()
	{
		var grants = Wizard();
		await Assert.That(grants.Has(PortalPermission.GameWizard)).IsTrue();

		var building = grants.Within([DelegatedScopes.Profile, DelegatedScopes.Commands, DelegatedScopes.Build]);
		await Assert.That(building.Has("game.builder")).IsTrue();
		await Assert.That(building.Shows("game.builder")).IsTrue();
		await Assert.That(building.Has(PortalPermission.GameWizard)).IsFalse();
		await Assert.That(building.Shows(PortalPermission.GameWizard)).IsFalse();
		await Assert.That(building.Has(PortalPermission.ConfigAdmin)).IsFalse();
		await Assert.That(building.Has(SceneClose.Scope)).IsFalse();

		var scenes = grants.Within([DelegatedScopes.Commands, "scene-running"]);
		await Assert.That(scenes.Has(SceneClose.Scope)).IsTrue();
		await Assert.That(scenes.Has("game.builder")).IsFalse();
	}

	[Test]
	public async Task God_IsTheOwnerToAnApp_OnlyWithEveryGroup()
	{
		var god = Wizard(number: 1);
		await Assert.That(god.Within([DelegatedScopes.Commands, DelegatedScopes.Game]).IsOwner).IsFalse();
		await Assert.That(god.Within([DelegatedScopes.Commands, .. DelegatedScopes.PermissionGroups]).IsOwner).IsTrue();
	}

	[Test]
	public async Task Parse_KeepsProfile_OrdersBuiltInsFirst_AndRefusesBadSpelling()
	{
		await Assert.That(DelegatedScopes.Parse("scenes game commands")).IsEquivalentTo(
			[DelegatedScopes.Profile, DelegatedScopes.Commands, DelegatedScopes.Game, "scenes"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(DelegatedScopes.Parse("Commands")).IsNull();
		await Assert.That(DelegatedScopes.Parse("scene.close")).IsNull();
	}
}
