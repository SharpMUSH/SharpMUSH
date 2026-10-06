using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.NATS;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The server page's figures, behind <c>server.admin</c>: version and build, uptime, connections, the
/// queue, readiness, the message bus, storage and the last backup. Everything is read in this process
/// from the services that already hold it; nothing scrapes <c>/metrics</c>.
///
/// Routes:
///   GET api/admin/server/status
/// </summary>
[ApiController]
[Route("api/admin/server")]
[Authorize(Policy = PortalPermission.ServerAdmin)]
public class AdminServerController(
	PortalBuild build,
	IExpandedObjectDataService objectData,
	IConnectionService connections,
	ITaskQueueReader queue,
	IOptionsWrapper<SharpMUSHOptions> options,
	ServerReadiness readiness,
	NatsMessagingMetrics bus,
	IStorageCapacityService storage,
	IWorldBackupService backups) : ControllerBase
{
	[HttpGet("status")]
	public async Task<IActionResult> Status(CancellationToken ct)
	{
		var uptime = await objectData.GetExpandedServerDataAsync<UptimeData>();
		var open = await connections.GetAll().ToListAsync(ct);
		var players = open
			.Where(connection => connection.State == IConnectionService.ConnectionState.LoggedIn)
			.Select(connection => connection.Ref)
			.Distinct()
			.Count();

		int? queued;
		try { queued = queue.GetQueueUsage().Total; }
		catch (NotSupportedException) { queued = null; }

		var snapshot = bus.Snapshot;
		var world = storage.Measure();

		return Ok(new AdminServerStatus(
			Implementation.Generated.VersionInfo.SharpMUSHVersion,
			build.Id,
			uptime?.StartTime,
			open.Count,
			players,
			queued,
			options.CurrentValue.Limit.GlobalQueueLimit,
			readiness.IsReady,
			readiness.Pending(),
			snapshot.Streams.Select(stream => new AdminBusStream(stream.Name, stream.Bytes, stream.MaxBytes)).ToList(),
			snapshot.Consumers.Sum(consumer => consumer.Pending),
			!ReferenceEquals(snapshot, NatsBrokerSnapshot.Empty),
			new AdminStorageStatus(world.LiveBytes, world.FileBytes, world.MapSizeBytes, world.WorldDiskFreeBytes),
			backups.List().FirstOrDefault()?.CreatedAt,
			backups.IsSupported));
	}
}
