using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Tells every portal tab to read its permissions again after a change to what a role or permission means:
/// a role saved, edited or deleted, a custom permission defined or removed, a category renamed. Its holders
/// may be characters on any account, which <see cref="PortalPermissionNotices"/> does not hear about one by
/// one. Changes to one account or object reach their tabs through <see cref="PortalPermissionNotices"/>, and
/// a refused change tells no one.
/// </summary>
public sealed class PermissionNoticeRoleManagementService(IRoleManagementService inner, PortalPermissionNotices notices)
	: IRoleManagementService
{
	public async Task<RoleOutcome<SharpRole>> SaveRoleAsync(RoleActor actor, RoleDraft draft, CancellationToken ct = default)
		=> Told(await inner.SaveRoleAsync(actor, draft, ct));

	public async Task<RoleOutcome<SharpRole>> EditRoleAsync(RoleActor actor, string slug, Func<SharpRole, RoleDraft> change,
		CancellationToken ct = default)
		=> Told(await inner.EditRoleAsync(actor, slug, change, ct));

	public async Task<RoleOutcome<Success>> DeleteRoleAsync(RoleActor actor, string slug, CancellationToken ct = default)
		=> Told(await inner.DeleteRoleAsync(actor, slug, ct));

	public Task<RoleOutcome<Success>> AssignAsync(RoleActor actor, string accountId, string slug, CancellationToken ct = default)
		=> inner.AssignAsync(actor, accountId, slug, ct);

	public Task<RoleOutcome<Success>> UnassignAsync(RoleActor actor, string accountId, string slug, CancellationToken ct = default)
		=> inner.UnassignAsync(actor, accountId, slug, ct);

	public Task<RoleOutcome<Success>> SetOverridesAsync(RoleActor actor, string accountId, IReadOnlyCollection<string> scopes,
		PermissionState state, string? reach = null, CancellationToken ct = default)
		=> inner.SetOverridesAsync(actor, accountId, scopes, state, reach, ct);

	public Task<RoleOutcome<Success>> AssignToObjectAsync(RoleActor actor, AnySharpObject target, string slug,
		CancellationToken ct = default)
		=> inner.AssignToObjectAsync(actor, target, slug, ct);

	public Task<RoleOutcome<Success>> UnassignFromObjectAsync(RoleActor actor, AnySharpObject target, string slug,
		CancellationToken ct = default)
		=> inner.UnassignFromObjectAsync(actor, target, slug, ct);

	public Task<RoleOutcome<Success>> SetObjectOverridesAsync(RoleActor actor, AnySharpObject target,
		IReadOnlyCollection<string> scopes, PermissionState state, string? reach = null, CancellationToken ct = default)
		=> inner.SetObjectOverridesAsync(actor, target, scopes, state, reach, ct);

	public async Task<RoleOutcome<CustomPermission>> DefinePermissionAsync(RoleActor actor, string scope, string category,
		string description, CancellationToken ct = default)
		=> Told(await inner.DefinePermissionAsync(actor, scope, category, description, ct));

	public async Task<RoleOutcome<Success>> RemovePermissionAsync(RoleActor actor, string scope, CancellationToken ct = default)
		=> Told(await inner.RemovePermissionAsync(actor, scope, ct));

	public Task<RoleOutcome<RoleCategory>> CreateCategoryAsync(RoleActor actor, CategoryKind kind, string name, string description,
		CancellationToken ct = default)
		=> inner.CreateCategoryAsync(actor, kind, name, description, ct);

	public Task<RoleOutcome<RoleCategory>> DescribeCategoryAsync(RoleActor actor, CategoryKind kind, string name, string description,
		CancellationToken ct = default)
		=> inner.DescribeCategoryAsync(actor, kind, name, description, ct);

	public async Task<RoleOutcome<RoleCategory>> RenameCategoryAsync(RoleActor actor, CategoryKind kind, string name, string newName,
		CancellationToken ct = default)
		=> Told(await inner.RenameCategoryAsync(actor, kind, name, newName, ct));

	public Task<RoleOutcome<Success>> DeleteCategoryAsync(RoleActor actor, CategoryKind kind, string name, CancellationToken ct = default)
		=> inner.DeleteCategoryAsync(actor, kind, name, ct);

	private RoleOutcome<T> Told<T>(RoleOutcome<T> outcome)
	{
		if (outcome is not RoleRefusal) notices.Everyone();
		return outcome;
	}
}
