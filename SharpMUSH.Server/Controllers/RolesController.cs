using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
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
	ILogger<RolesController> logger,
	IAdministrativeCapabilityService capabilities) : ControllerBase
{
	// A single engine serializes all public role mutations, including recovery validation.
	private static readonly SemaphoreSlim MutationGate = new(1, 1);

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
		string Status,
		string[] RoleSlugs);

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
		await MutationGate.WaitAsync(HttpContext.RequestAborted);
		try
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

			if (dto.Permissions is null) return BadRequest(new { error = "Permissions are required." });

			foreach (var (scope, value) in dto.Permissions)
			{
				if (!Enum.TryParse<PermissionState>(value, true, out var state) || !Enum.IsDefined(state))
					return BadRequest(new { error = $"Invalid permission state: {value}" });
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

			if (!await CanChangeAsync(existing ?? role) || !await CanChangeAsync(role)) return Forbid();
			if (!RoleRecoveryPolicy.PreservesRecovery(role, await roles.GetRolesAsync()))
				return BadRequest(new { error = "The God recovery grant must remain above every roles.admin denial." });
			await roles.UpsertRoleAsync(role);
			logger.LogInformation("Upserted role '{Slug}' (system: {IsSystem}).", role.Slug, role.IsSystem);
			return Ok(ToDto(role));

		}
		finally { MutationGate.Release(); }
	}

	[HttpDelete("{slug}")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	public async Task<IActionResult> Delete(string slug)
	{
		await MutationGate.WaitAsync(HttpContext.RequestAborted);
		try
		{
			var existingResult = await roles.GetRoleAsync(slug);
			var isSystem = existingResult.Match(role => role.IsSystem, _ => false);
			if (isSystem)
			{
				return BadRequest(new { error = "System roles cannot be deleted." });
			}

			if (existingResult.IsT0 && !await CanChangeAsync(existingResult.AsT0)) return Forbid();
			await roles.RemoveRoleAsync(slug);
			logger.LogInformation("Removed role '{Slug}'.", slug);
			return Ok(new { deleted = true });

		}
		finally { MutationGate.Release(); }
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
			account.Status.ToString(),
			assigned.Select(r => r.Slug).ToArray()));
	}

	[HttpPost("account/{accountId}/{slug}")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	[SuppressMessage("Security", "cs/cleartext-storage-of-sensitive-information", Justification = "accountId is a public database identity used for account-role foreign keys, not a credential or token.")]
	public async Task<IActionResult> AssignRole(string accountId, string slug)
	{
		await MutationGate.WaitAsync(HttpContext.RequestAborted);
		try
		{
			var existingResult = await roles.GetRoleAsync(slug);
			var exists = existingResult.Match(_ => true, _ => false);
			if (!exists)
			{
				return BadRequest(new { error = $"Unknown role: {slug}" });
			}

			if (!await CanChangeAsync(existingResult.AsT0, accountId)) return Forbid();
			if (await accounts.GetByIdAsync(accountId) is null) return NotFound();
			await roles.AssignRoleToAccountAsync(accountId, slug);
			logger.LogInformation("Assigned role '{Slug}' to account '{AccountId}'.", slug, accountId);
			return Ok();

		}
		finally { MutationGate.Release(); }
	}

	[HttpDelete("account/{accountId}/{slug}")]
	[Authorize(Policy = PortalPermission.RolesAdmin)]
	[SuppressMessage("Security", "cs/cleartext-storage-of-sensitive-information", Justification = "accountId is a public database identity used for account-role foreign keys, not a credential or token.")]
	public async Task<IActionResult> RemoveRole(string accountId, string slug)
	{
		await MutationGate.WaitAsync(HttpContext.RequestAborted);
		try
		{
			var existing = await roles.GetRoleAsync(slug);
			if (existing.IsT0 && !await CanChangeAsync(existing.AsT0, accountId)) return Forbid();
			await roles.RemoveRoleFromAccountAsync(accountId, slug);
			logger.LogInformation("Removed role '{Slug}' from account '{AccountId}'.", slug, accountId);
			return Ok();

		}
		finally { MutationGate.Release(); }
	}

	private async Task<bool> CanChangeAsync(SharpRole role, string? targetAccount = null)
	{
		var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (id is null) return false;
		var grants = await capabilities.GetGrantedScopesAsync(new(id));
		if (!grants.Contains(PortalPermission.RolesAdmin)) return false;
		var characters = await accounts.GetCharactersAsync(id);
		if (characters.Any(c => c.Object.Key == 1)) return true;
		var assigned = await roles.GetRolesForAccountAsync(id);
		// A delegated manager cannot change their own authority, system roles or restrictions.
		if (role.IsSystem || targetAccount == id || assigned.Any(r => r.Slug == role.Slug) ||
			role.Permissions.Values.Any(v => v == PermissionState.Deny)) return false;
		var decisions = await capabilities.ExplainAsync(new(id));
		var ceiling = decisions.TryGetValue(PortalPermission.RolesAdmin, out var decision)
			? decision.Priority ?? int.MinValue : int.MinValue;
		return role.Priority < ceiling && new PermissionResolver().Resolve([role]).All(grants.Contains);
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
				result[PortalPermission.AllScopes.Single(known => string.Equals(known, scope, StringComparison.OrdinalIgnoreCase))] = state;
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
