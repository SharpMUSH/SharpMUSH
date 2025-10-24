using Mediator;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSH.Library.Services.Interfaces.IPermissionService;

namespace SharpMUSH.Library.Services;

public class CommunicationService(
	IMediator mediator,
	INotifyService notifyService,
	IConnectionService connectionService,
	IPermissionService permissionService,
	ILocateService locateService) : ICommunicationService
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
			if (!playerResult.IsT0 && !playerResult.IsT1 && !playerResult.IsT2 && !playerResult.IsT3)
			{
				// Player not found, but port exists - allow it
				validPorts.Add(port);
				continue;
			}

			var player = playerResult.Match<AnySharpObject>(
				t0 => t0,
				t1 => t1,
				t2 => t2,
				t3 => t3,
				t4 => throw new InvalidOperationException()
			);

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

	public async ValueTask SendToRecipientAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		OneOf<DBRef, string> recipient,
		OneOf<MString, string> message,
		INotifyService.NotificationType notificationType)
	{
		AnySharpObject target;

		// Try to locate the target based on type
		if (recipient.IsT0)
		{
			// DBRef
			var targetResult = await mediator.Send(new GetObjectNodeQuery(recipient.AsT0));
			if (!targetResult.IsT0 && !targetResult.IsT1 && !targetResult.IsT2 && !targetResult.IsT3)
			{
				return;
			}
			target = targetResult.Match<AnySharpObject>(
				t0 => t0,
				t1 => t1,
				t2 => t2,
				t3 => t3,
				t4 => throw new InvalidOperationException()
			);
		}
		else
		{
			// Name string
			var playerResult = await locateService.LocatePlayer(parser, executor, executor, recipient.AsT1);
			if (!playerResult.TryPickT0(out var player, out var _))
			{
				return;
			}
			target = player;
		}

		// Check if executor can interact with the target
		if (!await permissionService.CanInteract(target, executor, InteractType.Hear))
		{
			return;
		}

		// Send the notification
		await notifyService.Notify(target, message, executor, notificationType);
	}
}
