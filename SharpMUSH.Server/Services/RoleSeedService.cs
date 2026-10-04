using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Seeds the roles a world starts with (<see cref="BuiltInRoles"/>). A missing system role is written
/// on every start; an existing one keeps whatever an administrator made of it. The starter roles are
/// written only into a world that has no roles at all, so deleting one is permanent. Runs after the
/// database migration, which Program awaits before any hosted service starts.
/// </summary>
public class RoleSeedService(IRoleRegistryService roles, ILogger<RoleSeedService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var existing = (await roles.GetRolesAsync(cancellationToken)).Select(r => r.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var templates = existing.Count == 0 ? BuiltInRoles.All.Concat(BuiltInRoles.Starters) : BuiltInRoles.All;
		var seeded = 0;
		foreach (var template in templates.Where(t => !existing.Contains(t.Slug)))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			await roles.UpsertRoleAsync(new SharpRole
			{
				Slug = template.Slug,
				Name = template.Name,
				Color = template.Color,
				Priority = template.Priority,
				IsSystem = template.IsSystem,
				Permissions = new Dictionary<string, PermissionState>(template.Permissions),
				CreatedAt = now,
				UpdatedAt = now
			});
			seeded++;
		}

		if (seeded > 0)
			logger.LogInformation("Seeded {Count} role(s).", seeded);
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
