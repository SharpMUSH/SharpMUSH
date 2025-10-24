using Mediator;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSH.Library.Services.Interfaces.IPermissionService;

namespace SharpMUSH.Library.Services;

public class CommunicationService(
	IMediator mediator,
	INotifyService notifyService,
	IConnectionService connectionService,
	IPermissionService permissionService) : ICommunicationService
{
	public async ValueTask SendToPortsAsync(
		AnySharpObject executor,
		long[] ports,
		OneOf<MString, string> message,
		INotifyService.NotificationType notificationType)
	{
		// Filter ports by permission check
		// Ports without a DBRef always work (no interactability check)
		// Ports with a DBRef require permission check
		var validPorts = new List<long>();
		foreach (var port in ports)
		{
			var connectionData = connectionService.Get(port);

			// If no connection or no DBRef, allow the port (no permission check needed)
			if (connectionData == null || connectionData.Ref == null)
			{
				validPorts.Add(port);
				continue;
			}

			// Get the player object connected to this port
			var playerResult = await mediator.Send(new GetObjectNodeQuery(connectionData.Ref.Value));
			if (playerResult.IsNone())
			{
				// Player not found, but port exists - allow it
				validPorts.Add(port);
				continue;
			}

			var player = playerResult.WithoutNone();

			// Check if executor can interact with the player
			if (await permissionService.CanInteract(player, executor, InteractType.Hear))
			{
				validPorts.Add(port);
			}
		}

		if (validPorts.Count > 0)
		{
			await notifyService.Notify(validPorts.ToArray(), message, executor, notificationType);
		}
	}
}
