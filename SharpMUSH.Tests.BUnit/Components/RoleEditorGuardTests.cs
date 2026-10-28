using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Models.Roles;
using SharpMUSH.Client.Pages.Admin.Roles;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.Authorization;
using RoleDraft = SharpMUSH.Client.Pages.Admin.Roles.RoleDraft;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The role editor offers a row for passing each guarded role's guard (<c>control.role.&lt;slug&gt;</c>): one per role that
/// allows <c>protect.role</c>, the role being edited counted as it is drafted.
/// </summary>
public class RoleEditorGuardTests : TrackingBunitContext
{
	private static PortalRoleModel Role(string slug, int priority, bool guarded) => new(slug, slug, "Staff", "#ffffff", priority, false,
		guarded ? new Dictionary<string, string> { [PortalPermission.ProtectRole] = "Allow" } : [], 0, 0);

	private IRenderedComponent<RoleEditor> Render(PortalRoleModel editing, RoleDraft draft, params PortalRoleModel[] roles)
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
		return Render<RoleEditor>(parameters => parameters
			.Add(p => p.Role, editing)
			.Add(p => p.Draft, draft)
			.Add(p => p.Roles, roles));
	}

	private static IReadOnlyList<string> Scopes(IRenderedComponent<RoleEditor> editor)
		=> [.. editor.FindAll(".ra-perm-scope").Select(code => code.TextContent)];

	[Test]
	public async Task EachGuardedRoleHasARowForPassingItsGuard()
	{
		var curator = Role("curator", 18, guarded: false);
		var draft = new RoleDraft();
		draft.Load(curator);

		var editor = Render(curator, draft, Role(BuiltInRoles.WizardSlug, 30, true), Role(BuiltInRoles.RoyaltySlug, 20, true),
			Role(BuiltInRoles.BuilderSlug, 15, false), curator);

		await Assert.That(Scopes(editor)).Contains(PortalPermission.ControlRole(BuiltInRoles.WizardSlug))
			.And.Contains(PortalPermission.ControlRole(BuiltInRoles.RoyaltySlug));
		await Assert.That(Scopes(editor)).DoesNotContain(PortalPermission.ControlRole(BuiltInRoles.BuilderSlug))
			.And.DoesNotContain(PortalPermission.ControlRole("curator"));
	}

	[Test]
	public async Task GuardingTheRoleBeingEditedAddsItsOwnRow()
	{
		var curator = Role("curator", 18, guarded: false);
		var draft = new RoleDraft();
		draft.Load(curator);
		var editor = Render(curator, draft, curator);

		draft.SetState(PortalPermission.ProtectRole, "Allow");
		editor.Render();

		await Assert.That(Scopes(editor)).Contains(PortalPermission.ControlRole("curator"));
	}
}
