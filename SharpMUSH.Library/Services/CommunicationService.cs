using Mediator;
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
		Func<AnySharpObject, SharpMessage> messageFunc,
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

		return await mediator.Send(new GetObjectNodeQuery(connectionData.Ref.Value)) is not AnySharpObject player
			|| await permissionService.CanInteract(executor, player, InteractType.Hear);
	}

	public async ValueTask SendToRoomAsync(
		AnySharpObject executor,
		AnySharpContainer room,
		Func<AnySharpObject, SharpMessage> messageFunc,
		INotifyService.NotificationType notificationType,
		AnySharpObject? sender = null,
		IEnumerable<AnySharpObject>? excludeObjects = null,
		IPermissionService.InteractType interact = InteractType.Hear)
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
					? await permissionService.CanInteract(executor, objWithRoom, interact)
					: await permissionService.CanInteract(executor, objWithRoom, interact, actualSender);
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

	public async ValueTask<DeliveryResult> SendToObjectAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject enactor,
		string targetName,
		Func<AnySharpObject, SharpMessage> messageFunc,
		INotifyService.NotificationType notificationType,
		bool notifyOnPermissionFailure = true)
		=> await locateService.LocateAndNotifyIfInvalidWithCallState(parser, enactor, enactor, targetName, LocateFlags.All) switch
		{
			AnySharpObject target => await DeliverToTargetAsync(executor, target, messageFunc, notificationType,
				notifyOnPermissionFailure),
			Error<CallState> error => await TargetNotFoundAsync(executor, error.Value)
		};

	private async ValueTask<DeliveryResult> TargetNotFoundAsync(AnySharpObject executor, CallState error)
	{
		await notifyService.Notify(executor, error.Message!);
		return new DeliveryFailure(DeliveryFailure.Cause.TargetNotFound);
	}

	private async ValueTask<DeliveryResult> DeliverToTargetAsync(
		AnySharpObject executor,
		AnySharpObject target,
		Func<AnySharpObject, SharpMessage> messageFunc,
		INotifyService.NotificationType notificationType,
		bool notifyOnPermissionFailure)
	{
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
		IAsyncEnumerable<DbRefOrName> targets,
		Func<AnySharpObject, SharpMessage> messageFunc,
		INotifyService.NotificationType notificationType,
		bool notifyOnPermissionFailure = true)
	{
		var notified = new List<AnySharpObject>();

		await foreach (var target in targets)
		{
			var targetString = target switch
			{
				DBRef dbref => dbref.ToString(),
				string name => name
			};
			var delivery = await SendToObjectAsync(parser, executor, enactor, targetString, messageFunc,
				notificationType, notifyOnPermissionFailure);

			if (delivery is AnySharpObject delivered)
			{
				notified.Add(delivered);
			}
		}

		return notified;
	}
}