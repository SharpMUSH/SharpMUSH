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
		Func<AnySharpObject, OneOf<MString, string>> messageFunc,
		INotifyService.NotificationType notificationType)
	{
		var validPorts = new List<long>();
		foreach (var port in ports)
		{
			var connectionData = connectionService.Get(port);

			if (connectionData?.Ref is null)
			{
				validPorts.Add(port);
				continue;
			}

			var playerResult = await mediator.Send(new GetObjectNodeQuery(connectionData.Ref.Value));

			if (playerResult.IsNone())
			{
				validPorts.Add(port);
				continue;
			}

			var player = playerResult.WithoutNone();

			if (await permissionService.CanInteract(player, executor, InteractType.Hear))
			{
				validPorts.Add(port);
			}
		}

		if (validPorts.Count > 0)
		{
			var message = messageFunc(executor);
			await notifyService.Notify(validPorts.ToArray(), message, executor, notificationType);
		}
	}

	public async ValueTask SendToRoomAsync(
		AnySharpObject executor,
		AnySharpContainer room,
		Func<AnySharpObject, OneOf<MString, string>> messageFunc,
		INotifyService.NotificationType notificationType,
		AnySharpObject? sender = null,
		IEnumerable<AnySharpObject>? excludeObjects = null)
	{
		var contents = await room.Content(mediator);
		var actualSender = sender ?? executor;
		var excludeSet = excludeObjects?.ToHashSet() ?? [];

		var interactableContents = contents
			.Where(async (obj, _) =>
			{
				var objWithRoom = obj.WithRoomOption();
				
				if (excludeSet.Contains(objWithRoom))
				{
					return false;
				}

				return await permissionService.CanInteract(objWithRoom, executor, InteractType.Hear);
			});

		await foreach (var obj in interactableContents)
		{
			var objWithRoom = obj.WithRoomOption();
			var message = messageFunc(objWithRoom);
			await notifyService.Notify(
				objWithRoom,
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
		Func<AnySharpObject, OneOf<MString, string>> messageFunc,
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

		var message = messageFunc(target);
		await notifyService.Notify(target, message, executor, notificationType);
		return true;
	}

	public async ValueTask SendToMultipleObjectsAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject enactor,
		IAsyncEnumerable<OneOf<DBRef, string>> targets,
		Func<AnySharpObject, OneOf<MString, string>> messageFunc,
		INotifyService.NotificationType notificationType,
		bool notifyOnPermissionFailure = true)
	{
		await foreach (var target in targets)
		{
			var targetString = target.Match(dbref => dbref.ToString(), str => str);
			await SendToObjectAsync(parser, executor, enactor, targetString, messageFunc, notificationType,
				notifyOnPermissionFailure);
		}
	}
}