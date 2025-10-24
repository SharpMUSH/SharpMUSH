using Mediator;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
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

	public async ValueTask SendToRoomAsync(
		AnySharpObject executor,
		AnySharpContainer room,
		OneOf<MString, string> message,
		INotifyService.NotificationType notificationType,
		AnySharpObject? sender = null)
	{
		var contents = await room.Content(mediator);
		var actualSender = sender ?? executor;

		var interactableContents = contents
			.Where(async (obj, _) =>
				await permissionService.CanInteract(obj.WithRoomOption(), executor, InteractType.Hear));

		await foreach (var obj in interactableContents)
		{
			await notifyService.Notify(
				obj.WithRoomOption(),
				message,
				actualSender,
				notificationType);
		}
	}

	public async ValueTask<bool> SendToObjectAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject enactor,
		string targetName,
		OneOf<MString, string> message,
		INotifyService.NotificationType notificationType,
		bool notifyOnPermissionFailure = true)
	{
		var maybeLocateTarget = await locateService.LocateAndNotifyIfInvalidWithCallState(
			parser, enactor, enactor, targetName, LocateFlags.All);

		if (maybeLocateTarget.IsError)
		{
			await notifyService.Notify(executor, maybeLocateTarget.AsError.Message!);
			return false;
		}

		var target = maybeLocateTarget.AsSharpObject;

		if (!await permissionService.CanInteract(target, executor, InteractType.Hear))
		{
			if (notifyOnPermissionFailure)
			{
				await notifyService.Notify(executor, $"{target.Object().Name} does not want to hear from you.");
			}
			return false;
		}

		await notifyService.Notify(target, message);
		return true;
	}

	public async ValueTask SendToMultipleObjectsAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject enactor,
		IEnumerable<OneOf<DBRef, string>> targets,
		OneOf<MString, string> message,
		INotifyService.NotificationType notificationType,
		bool notifyOnPermissionFailure = true)
	{
		foreach (var target in targets)
		{
			var targetString = target.Match(dbref => dbref.ToString(), str => str);
			await SendToObjectAsync(parser, executor, enactor, targetString, message, notificationType, notifyOnPermissionFailure);
		}
	}
}
