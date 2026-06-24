using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Portal roles &amp; account-assignment API (Discord-style RBAC). Roles are prioritised bundles of
/// three-state permission grants; system roles (God/Wizard/…) cannot be deleted or re-slugged, but
/// their name, color, priority, and permissions may be edited.
///
/// Routes (all require the <see cref="PortalPermission.RolesAdmin"/> policy):
///   GET    /api/roles                                 — list roles (priority-desc)
///   POST   /api/roles                                  — create or update a role
///   DELETE /api/roles/{slug}                           — remove a role (non-system only)
///   GET    /api/roles/account?username={username}      — an account's assigned roles
///   POST   /api/roles/account/{accountId}/{slug}       — assign a role to an account
///   DELETE /api/roles/account/{accountId}/{slug}       — remove a role from an account
/// </summary>
[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController(
	IRoleRegistryService roles,
	IAccountService accounts,
	ILogger<RolesController> logger) : ControllerBase
{
	public record RoleDto(
		string Slug,
		string Name,
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
		bool IsDisabled,
		string[] RoleSlugs);

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
		var slug = dto.Slug?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(slug))
		{
			return BadRequest(new { error = "Slug is required." });
		}

		if (!IsValidSlug(slug))
		{
			return BadRequest(new { error = "Slug must be lowercase and contain only letters, digits, '-', or '_'." });
		}

		foreach (var scope in dto.Permissions.Keys)
		{
			if (!PortalPermission.IsKnown(scope))
			{
				return BadRequest(new { error = $"Unknown permission scope: {scope}" });
			}
		}

		var existingResult = await roles.GetRoleAsync(slug);
		var existing = existingResult.Match(role => role, _ => (SharpRole?)null);

		var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		// System roles cannot be re-slugged or un-systemed, but their editable fields still apply.
		var isSystem = existing?.IsSystem ?? false;

		var role = new SharpRole
		{
			Id = existing?.Id,
			Slug = slug,
			Name = dto.Name?.Trim() ?? string.Empty,
			Color = string.IsNullOrWhiteSpace(dto.Color) ? null : dto.Color.Trim(),
			Priority = dto.Priority,
			IsSystem = isSystem,
			Permissions = ToPermissions(dto.Permissions),
			CreatedAt = existing?.CreatedAt ?? nowMs,
			UpdatedAt = nowMs
		};

		await roles.UpsertRoleAsync(role);
		logger.LogInformation("Upserted role '{Slug}' (system: {IsSystem}).", role.Slug, role.IsSystem);
		return Ok(ToDto(role));
	}

	[HttpDelete("{slug}")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<IActionResult> Delete(string slug)
	{
		var existingResult = await roles.GetRoleAsync(slug);
		var isSystem = existingResult.Match(role => role.IsSystem, _ => false);
		if (isSystem)
		{
			return BadRequest(new { error = "System roles cannot be deleted." });
		}

		await roles.RemoveRoleAsync(slug);
		logger.LogInformation("Removed role '{Slug}'.", slug);
		return Ok(new { deleted = true });
	}

	[HttpGet("account")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<ActionResult<AccountRolesDto>> GetAccountRoles([FromQuery] string username)
	{
		var account = await accounts.GetByUsernameAsync(username);
		if (account is null)
		{
			return NotFound();
		}

		var assigned = await roles.GetRolesForAccountAsync(account.Id!);
		return Ok(new AccountRolesDto(
			account.Id!,
			account.Username,
			account.Email,
			account.IsDisabled,
			assigned.Select(r => r.Slug).ToArray()));
	}

	[HttpPost("account/{accountId}/{slug}")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<IActionResult> AssignRole(string accountId, string slug)
	{
		var existingResult = await roles.GetRoleAsync(slug);
		var exists = existingResult.Match(_ => true, _ => false);
		if (!exists)
		{
			return BadRequest(new { error = $"Unknown role: {slug}" });
		}

		await roles.AssignRoleToAccountAsync(accountId, slug);
		logger.LogInformation("Assigned role '{Slug}' to account '{AccountId}'.", slug, accountId);
		return Ok();
	}

	[HttpDelete("account/{accountId}/{slug}")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<IActionResult> RemoveRole(string accountId, string slug)
	{
		await roles.RemoveRoleFromAccountAsync(accountId, slug);
		logger.LogInformation("Removed role '{Slug}' from account '{AccountId}'.", slug, accountId);
		return Ok();
	}

	private static bool IsValidSlug(string slug)
		=> slug.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_');

	/// <summary>Serializes the role's permission stances to their <see cref="PermissionState"/> names.</summary>
	private static Dictionary<string, string> ToPermissionStrings(IReadOnlyDictionary<string, PermissionState> permissions)
		=> permissions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());

	/// <summary>
	/// Parses incoming scope→name pairs to <see cref="PermissionState"/>, defaulting unrecognised
	/// values to <see cref="PermissionState.Inherit"/> and dropping Inherit entries (the storage default).
	/// </summary>
	private static Dictionary<string, PermissionState> ToPermissions(IReadOnlyDictionary<string, string> permissions)
	{
		var result = new Dictionary<string, PermissionState>();
		foreach (var (scope, value) in permissions)
		{
			if (!Enum.TryParse<PermissionState>(value, ignoreCase: true, out var state))
			{
				state = PermissionState.Inherit;
			}

			if (state != PermissionState.Inherit)
			{
				result[scope] = state;
			}
		}

		return result;
	}

	private static RoleDto ToDto(SharpRole role) => new(
		role.Slug,
		role.Name,
		role.Color,
		role.Priority,
		role.IsSystem,
		ToPermissionStrings(role.Permissions),
		role.CreatedAt,
		role.UpdatedAt);
}
