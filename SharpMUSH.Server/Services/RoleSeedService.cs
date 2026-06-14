using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Seeds the built-in portal roles (God/Wizard/Royalty/Builder/Player/Guest) at startup.
/// Idempotent and non-destructive: only inserts a role that doesn't exist yet, so an admin's
/// edits to a built-in's permissions, color, or priority survive restarts. Runs after the DB
/// migration (which has already executed when the ISharpDatabase singleton was constructed).
/// </summary>
public class RoleSeedService(IRoleRegistryService roles, ILogger<RoleSeedService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var seeded = 0;
		foreach (var template in BuiltInRoles.All)
		{
			var existing = await roles.GetRoleAsync(template.Slug);
			if (existing.IsT0)
				continue; // already present — never clobber an admin's edits to a built-in

			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			await roles.UpsertRoleAsync(new SharpRole
			{
				Slug = template.Slug,
				Name = template.Name,
				Color = template.Color,
				Priority = template.Priority,
				IsSystem = true,
				Permissions = new Dictionary<string, PermissionState>(template.Permissions),
				CreatedAt = now,
				UpdatedAt = now
			});
			seeded++;
		}

		if (seeded > 0)
			logger.LogInformation("Seeded {Count} built-in portal role(s).", seeded);
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
