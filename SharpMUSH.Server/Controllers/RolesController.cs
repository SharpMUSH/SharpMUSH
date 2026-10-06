using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Roles, account assignments and per-account overrides (Discord-style; see
/// <see cref="PermissionResolver"/>). Every change goes through <see cref="IRoleManagementService"/>,
/// the same rules <c>@role</c> applies in the game.
///
/// Routes (all but <c>effective</c> require the <see cref="PortalPermission.RolesAdmin"/> policy):
///   GET    /api/roles/effective                         — the caller's permissions and why
///   GET    /api/roles                                   — list roles (priority-desc)
///   POST   /api/roles                                   — create or update a role
///   DELETE /api/roles/{slug}                            — remove a role (non-system only)
///   GET    /api/roles/account?username={username}       — an account's assigned roles and overrides
///   POST   /api/roles/account/{accountId}/{slug}        — assign a role to an account
///   DELETE /api/roles/account/{accountId}/{slug}        — remove a role from an account
///   PUT    /api/roles/account/{accountId}/overrides     — set one per-account override
///   GET    /api/roles/permissions                       — the custom permissions the game defines
///   PUT    /api/roles/permissions                       — define one, or change its description
///   DELETE /api/roles/permissions/{scope}               — remove one and every setting of it
///   GET    /api/roles/categories                        — the categories of roles and permissions
///   POST   /api/roles/categories                        — create one
///   PUT    /api/roles/categories/{name}                 — change its description, or rename it
///   DELETE /api/roles/categories/{name}                 — delete one that holds nothing
/// </summary>
[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController(
	IRoleRegistryService roles,
	IAccountService accounts,
	IAdministrativeCapabilityService capabilities,
	IRoleManagementService management) : ControllerBase
{
	public record RoleDto(
		string Slug,
		string Name,
		string Category,
		string? Color,
		int Priority,
		bool IsSystem,
		Dictionary<string, string> Permissions,
		long CreatedAt,
		long UpdatedAt);

	public record AccountRolesDto(
		string AccountId,
		string Username,
		string? Email,
		string Status,
		string[] RoleSlugs,
		Dictionary<string, string> Overrides);

	public record OverrideDto(string Scope, string State);

	public record CustomPermissionDto(string Scope, string Category, string Description);

	public record CategoryDto(string Name, string Description);

	[HttpGet("effective")]
	public async Task<IActionResult> Effective()
	{
		var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (id is null) return Forbid();
		return Ok(await capabilities.ExplainAsync(new(id)));
	}

	[HttpGet]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<ActionResult<IReadOnlyList<RoleDto>>> List()
	{
		var all = await roles.GetRolesAsync();
		return Ok(all.Select(ToDto).ToList());
	}

	[HttpPost]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<IActionResult> Upsert([FromBody] RoleDto dto)
	{
		if (Actor() is not { } actor) return Forbid();
		if (dto.Permissions is null) return BadRequest(new { error = "Permissions are required." });
		var permissions = new Dictionary<string, PermissionState>();
		foreach (var (scope, value) in dto.Permissions)
		{
			if (!Enum.TryParse<PermissionState>(value, true, out var state) || !Enum.IsDefined(state))
				return BadRequest(new { error = $"Invalid permission state: {value}" });
			permissions[scope] = state;
		}

		var draft = new RoleDraft(dto.Slug ?? string.Empty, dto.Name ?? string.Empty, dto.Category ?? string.Empty, dto.Color, dto.Priority, permissions);
		return await management.SaveRoleAsync(actor, draft, HttpContext.RequestAborted) switch
		{
			SharpRole role => Ok(ToDto(role)),
			RoleRefusal refusal => Refused(refusal)
		};
	}

	[HttpDelete("{slug}")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<IActionResult> Delete(string slug)
	{
		if (Actor() is not { } actor) return Forbid();
		return await management.DeleteRoleAsync(actor, slug, HttpContext.RequestAborted) switch
		{
			Success => Ok(new { deleted = true }),
			RoleRefusal refusal => Refused(refusal)
		};
	}

	[HttpGet("account")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<ActionResult<AccountRolesDto>> GetAccountRoles([FromQuery] string username)
	{
		var account = await accounts.GetByUsernameAsync(username);
		if (account?.Id is null)
		{
			return NotFound();
		}

		var assigned = await roles.GetRolesForAccountAsync(account.Id);
		var overrides = await roles.GetAccountOverridesAsync(account.Id);
		return Ok(new AccountRolesDto(
			account.Id,
			account.Username,
			account.Email,
			account.Status.ToString(),
			assigned.Select(r => r.Slug).ToArray(),
			overrides.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString())));
	}

	[HttpPost("account/{accountId}/{slug}")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	[SuppressMessage("Security", "cs/cleartext-storage-of-sensitive-information", Justification = "accountId is a public database identity used for account-role foreign keys, not a credential or token.")]
	public async Task<IActionResult> AssignRole(string accountId, string slug)
	{
		if (Actor() is not { } actor) return Forbid();
		return await management.AssignAsync(actor, accountId, slug, HttpContext.RequestAborted) switch
		{
			Success => Ok(),
			RoleRefusal refusal => Refused(refusal)
		};
	}

	[HttpDelete("account/{accountId}/{slug}")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	[SuppressMessage("Security", "cs/cleartext-storage-of-sensitive-information", Justification = "accountId is a public database identity used for account-role foreign keys, not a credential or token.")]
	public async Task<IActionResult> RemoveRole(string accountId, string slug)
	{
		if (Actor() is not { } actor) return Forbid();
		return await management.UnassignAsync(actor, accountId, slug, HttpContext.RequestAborted) switch
		{
			Success => Ok(),
			RoleRefusal refusal => Refused(refusal)
		};
	}

	[HttpPut("account/{accountId}/overrides")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	[SuppressMessage("Security", "cs/cleartext-storage-of-sensitive-information", Justification = "accountId is a public database identity used for account-role foreign keys, not a credential or token.")]
	public async Task<IActionResult> SetOverride(string accountId, [FromBody] OverrideDto dto)
	{
		if (Actor() is not { } actor) return Forbid();
		if (!Enum.TryParse<PermissionState>(dto.State, true, out var state) || !Enum.IsDefined(state))
			return BadRequest(new { error = $"Invalid permission state: {dto.State}" });
		return await management.SetOverridesAsync(actor, accountId, [dto.Scope], state, HttpContext.RequestAborted) switch
		{
			Success => Ok(),
			RoleRefusal refusal => Refused(refusal)
		};
	}

	/// <summary>
	/// The custom permissions, for the role editor and for the wiki requirement pickers: a wiki.admin holder
	/// who cannot manage roles still chooses among them.
	/// </summary>
	[HttpGet("permissions")]
	public async Task<ActionResult<IReadOnlyList<CustomPermission>>> ListPermissions()
	{
		var ct = HttpContext.RequestAborted;
		if (Actor() is not { } actor
			|| !(await capabilities.AuthorizeAsync(actor, PortalPermission.RolesAdmin, ct)
				|| await capabilities.AuthorizeAsync(actor, PortalPermission.WikiAdmin, ct)))
			return Forbid();
		return Ok(await roles.GetCustomPermissionsAsync(ct));
	}

	[HttpPut("permissions")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<IActionResult> DefinePermission([FromBody] CustomPermissionDto dto)
	{
		if (Actor() is not { } actor) return Forbid();
		return await management.DefinePermissionAsync(actor, dto.Scope ?? string.Empty, dto.Category ?? string.Empty, dto.Description ?? string.Empty, HttpContext.RequestAborted) switch
		{
			CustomPermission permission => Ok(permission),
			RoleRefusal refusal => Refused(refusal)
		};
	}

	[HttpDelete("permissions/{scope}")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<IActionResult> RemovePermission(string scope)
	{
		if (Actor() is not { } actor) return Forbid();
		return await management.RemovePermissionAsync(actor, scope, HttpContext.RequestAborted) switch
		{
			Success => Ok(new { deleted = true }),
			RoleRefusal refusal => Refused(refusal)
		};
	}

	/// <summary>The category list <paramref name="kind"/> (<c>role</c> or <c>permission</c>).</summary>
	[HttpGet("categories/{kind}")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<ActionResult<IReadOnlyList<RoleCategory>>> ListCategories(string kind)
		=> ParseKind(kind) is { } list
			? Ok(await roles.GetCategoriesAsync(list, HttpContext.RequestAborted))
			: NotFound();

	[HttpPost("categories/{kind}")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<IActionResult> CreateCategory(string kind, [FromBody] CategoryDto dto)
	{
		if (Actor() is not { } actor) return Forbid();
		if (ParseKind(kind) is not { } list) return NotFound();
		return await management.CreateCategoryAsync(actor, list, dto.Name ?? string.Empty, dto.Description ?? string.Empty, HttpContext.RequestAborted) switch
		{
			RoleCategory category => Ok(category),
			RoleRefusal refusal => Refused(refusal)
		};
	}

	/// <summary>Sets the description, then the name when <see cref="CategoryDto.Name"/> differs from the route's.</summary>
	[HttpPut("categories/{kind}/{name}")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<IActionResult> UpdateCategory(string kind, string name, [FromBody] CategoryDto dto)
	{
		if (Actor() is not { } actor) return Forbid();
		if (ParseKind(kind) is not { } list) return NotFound();
		var ct = HttpContext.RequestAborted;
		if (await management.DescribeCategoryAsync(actor, list, name, dto.Description ?? string.Empty, ct) is RoleRefusal refusal)
			return Refused(refusal);
		var newName = dto.Name?.Trim() ?? string.Empty;
		if (newName.Length == 0 || newName == name.Trim()) return Ok();
		return await management.RenameCategoryAsync(actor, list, name, newName, ct) switch
		{
			RoleCategory category => Ok(category),
			RoleRefusal renameRefusal => Refused(renameRefusal)
		};
	}

	[HttpDelete("categories/{kind}/{name}")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<IActionResult> DeleteCategory(string kind, string name)
	{
		if (Actor() is not { } actor) return Forbid();
		if (ParseKind(kind) is not { } list) return NotFound();
		return await management.DeleteCategoryAsync(actor, list, name, HttpContext.RequestAborted) switch
		{
			Success => Ok(new { deleted = true }),
			RoleRefusal refusal => Refused(refusal)
		};
	}

	/// <summary>The category list a route names: <c>role</c> or <c>permission</c>.</summary>
	private static CategoryKind? ParseKind(string kind) => kind.ToLowerInvariant() switch
	{
		"role" => CategoryKind.Role,
		"permission" => CategoryKind.Permission,
		_ => null
	};

	/// <summary>Portal role authority is account-wide, as in every other HTTP policy gate.</summary>
	private CapabilityActor? Actor()
		=> User.FindFirstValue(ClaimTypes.NameIdentifier) is { } id ? new CapabilityActor(id) : null;

	private IActionResult Refused(RoleRefusal refusal) => refusal.Kind switch
	{
		RoleRefusalKind.NotFound => NotFound(new { error = refusal.Message }),
		RoleRefusalKind.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = refusal.Message }),
		_ => BadRequest(new { error = refusal.Message })
	};

	private static RoleDto ToDto(SharpRole role) => new(
		role.Slug,
		role.Name,
		role.Category,
		role.Color,
		role.Priority,
		role.IsSystem,
		role.Permissions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString()),
		role.CreatedAt,
		role.UpdatedAt);
}
