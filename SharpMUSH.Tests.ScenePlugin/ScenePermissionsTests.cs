using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Common;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>What a reader with no character may read: the <c>everyone</c> role's answer for <c>scene.view</c>.</summary>
public class ScenePermissionsTests
{
	private static IRoleRegistryService Roles(bool declared, PermissionState everyone)
	{
		var roles = Substitute.For<IRoleRegistryService>();
		roles.GetCustomPermissionsAsync(Arg.Any<CancellationToken>()).Returns(declared
			? [new CustomPermission(ScenePermissions.View, "Scenes", "Read scenes you are not in.", 0)]
			: []);
		var role = new SharpRole { Slug = BuiltInRoles.EveryoneSlug, Name = "Everyone" };
		if (everyone != PermissionState.Inherit) role.Permissions[ScenePermissions.View] = everyone;
		roles.GetRolesAsync(Arg.Any<CancellationToken>()).Returns([role]);
		return roles;
	}

	[Test]
	public async Task Everyone_HoldingSceneView_MayRead()
		=> await Assert.That(await ScenePermissions.EveryoneAllowsAsync(Roles(true, PermissionState.Allow), new PermissionResolver(),
			ScenePermissions.View)).IsTrue();

	[Test]
	public async Task Everyone_NotHoldingSceneView_MayNotRead()
		=> await Assert.That(await ScenePermissions.EveryoneAllowsAsync(Roles(true, PermissionState.Inherit), new PermissionResolver(),
			ScenePermissions.View)).IsFalse();

	/// <summary>Without the scene package nothing declares scene.view, and a permission nobody declares restricts nothing.</summary>
	[Test]
	public async Task SceneView_NotDeclared_RestrictsNothing()
		=> await Assert.That(await ScenePermissions.EveryoneAllowsAsync(Roles(false, PermissionState.Inherit), new PermissionResolver(),
			ScenePermissions.View)).IsTrue();
}
