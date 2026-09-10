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
		var validPorts = await ports
			.ToAsyncEnumerable()
			.Where(async (port, _) => await CanHearOnPortAsync(executor, port))
			.ToArrayAsync();

		if (validPorts.Length > 0)
		{
			var message = messageFunc(executor);
			await notifyService.Notify(validPorts, message, executor, notificationType);
		}
	}

	/// <summary>
	/// A port is delivered to unless it is bound to a player who exists and cannot hear
	/// <paramref name="executor"/>.
	/// </summary>
	private async ValueTask<bool> CanHearOnPortAsync(AnySharpObject executor, long port)
	{
		var connectionData = connectionService.Get(port);
		if (connectionData?.Ref is null)
		{
			return true;
		}

		var playerResult = await mediator.Send(new GetObjectNodeQuery(connectionData.Ref.Value));

		return playerResult.IsNone()
			|| await permissionService.CanInteract(executor, playerResult.WithoutNone(), InteractType.Hear);
	}

	public async ValueTask SendToRoomAsync(
		AnySharpObject executor,
		AnySharpContainer room,
		Func<AnySharpObject, OneOf<MString, string>> messageFunc,
		INotifyService.NotificationType notificationType,
		AnySharpObject? sender = null,
		IEnumerable<AnySharpObject>? excludeObjects = null)
	{
		var contents = room.Content(mediator);
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

				return actualSender.Object().DBRef == executor.Object().DBRef
					? await permissionService.CanInteract(executor, objWithRoom, InteractType.Hear)
					: await permissionService.CanInteract(executor, objWithRoom, InteractType.Hear, actualSender);
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

	public async ValueTask<OneOf<AnySharpObject, DeliveryFailure>> SendToObjectAsync(
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
			return new DeliveryFailure(DeliveryFailure.Cause.TargetNotFound);
		}

		var target = maybeLocateTarget.AsSharpObject;

		if (!await permissionService.CanInteract(executor, target, InteractType.Hear))
		{
			if (notifyOnPermissionFailure)
			{
				await notifyService.Notify(executor, $"{target.Object().Name} does not want to hear from you.");
			}

			return new DeliveryFailure(DeliveryFailure.Cause.TargetWillNotHear);
		}

		var message = messageFunc(target);
		await notifyService.Notify(target, message, executor, notificationType);
		return target;
	}

	public async ValueTask<IReadOnlyList<AnySharpObject>> SendToMultipleObjectsAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject enactor,
		IAsyncEnumerable<OneOf<DBRef, string>> targets,
		Func<AnySharpObject, OneOf<MString, string>> messageFunc,
		INotifyService.NotificationType notificationType,
		bool notifyOnPermissionFailure = true)
	{
		var notified = new List<AnySharpObject>();

		await foreach (var target in targets)
		{
			var targetString = target.Match(dbref => dbref.ToString(), str => str);
			var delivery = await SendToObjectAsync(parser, executor, enactor, targetString, messageFunc,
				notificationType, notifyOnPermissionFailure);

			if (delivery.IsT0)
			{
				notified.Add(delivery.AsT0);
			}
		}

		return notified;
	}
}