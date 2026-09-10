using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Seeds the built-in portal roles (God/Wizard/Royalty/Builder/Player/Guest) at startup.
/// Seeds absent roles and upgrades the approved administrative capabilities once. Explicit
/// permissions and other administrator edits survive restarts. Runs after the DB
/// migration, which Program awaits right after the host is built and before any hosted service
/// starts.
/// </summary>
public class RoleSeedService(IRoleRegistryService roles, ILogger<RoleSeedService> logger, IExpandedDataStore store) : IHostedService
{
	public const string CapabilityMigrationKey = "sharpmush.roles.administrative-capabilities.v1";
	public sealed record CapabilityMigration(int Version);
	private static readonly string[] AddedCapabilities =
	[
		PortalPermission.SnapshotCapture, PortalPermission.SnapshotRestore,
		PortalPermission.JobsManageOwn, PortalPermission.JobsManage,
		PortalPermission.QueueInspectOwn, PortalPermission.QueueInspect,
		PortalPermission.QueueControlOwn, PortalPermission.QueueControl,
		PortalPermission.DiagnosticsProfile, PortalPermission.RealityAdmin
	];

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var migration = await store.GetExpandedServerData<CapabilityMigration>(CapabilityMigrationKey, cancellationToken);
		var upgrade = migration is null;
		var seeded = 0;
		foreach (var template in BuiltInRoles.All)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var existing = await roles.GetRoleAsync(template.Slug);
			if (existing.IsT0)
			{
				var current = existing.AsT0;
				if (upgrade && current.IsSystem)
				{
					var permissions = new Dictionary<string, PermissionState>(current.Permissions);
					foreach (var scope in AddedCapabilities)
						if (template.Permissions.TryGetValue(scope, out var grant) &&
							!permissions.Keys.Contains(scope, StringComparer.OrdinalIgnoreCase))
							permissions.Add(scope, grant);
					if (permissions.Count != current.Permissions.Count)
						await roles.UpsertRoleAsync(new SharpRole
						{
							Id = current.Id, Slug = current.Slug, Name = current.Name, Color = current.Color,
							Priority = current.Priority, IsSystem = current.IsSystem, CreatedAt = current.CreatedAt,
							UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Permissions = permissions
						});
				}
				continue;
			}

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

		// Completion follows all durable writes. Retries preserve existing explicit choices;
		// after completion, removing a grant remains effective across restarts.
		if (upgrade)
			await store.SetExpandedServerData(CapabilityMigrationKey, new CapabilityMigration(1), cancellationToken);

		if (seeded > 0)
			logger.LogInformation("Seeded {Count} built-in portal role(s).", seeded);
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
