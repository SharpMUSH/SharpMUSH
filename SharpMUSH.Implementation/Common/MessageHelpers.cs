using Mediator;
using MarkupString;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSH.Library.Services.Interfaces.IPermissionService;
namespace SharpMUSH.Implementation.Common;

public static class MessageHelpers
{
	private const string SpecialRecipientDbref = "#-2";
	private const string RecipientReplacementToken = "##";

	/// <summary>
	/// Determines the notification type based on the prefix character of the message.
	/// Used for commands like @pemit, say, etc.
	/// </summary>
	/// <param name="message">The message to analyze</param>
	/// <returns>The appropriate notification type</returns>
	public static INotifyService.NotificationType DetermineMessageType(string message)
	{
		return message switch
		{
			[':', .. _] => INotifyService.NotificationType.Pose,
			[';', .. _] => INotifyService.NotificationType.SemiPose,
			['|', .. _] => INotifyService.NotificationType.Emit,
			_ => INotifyService.NotificationType.Say
		};
	}

	public static async ValueTask<CallState> ProcessMessageAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		ILocateService locateService,
		IAttributeService attributeService,
		INotifyService notifyService,
		IPermissionService permissionService,
		ICommunicationService communicationService,
		AnySharpObject executor,
		MString recipientsArg,
		MString defmsg,
		string objectAttrArg,
		IEnumerable<KeyValuePair<string, CallState>> functionArgs,
		bool isRemit = false,
		bool isOemit = false,
		bool isNospoof = false,
		bool isSpoof = false,
		bool isSilent = true)
	{
		var recipientNamelist = ArgHelpers.NameList(recipientsArg.ToString());

		var attrObjSplit = objectAttrArg.Split('/', 2);
		AnySharpObject? objToEvaluate = null;
		string attrToEvaluate;
		SharpAttribute[]? pinnedAttribute = null;

		if (attrObjSplit.Length > 1 && attrObjSplit[0] != SpecialRecipientDbref)
		{
			var maybeLocateTarget = await locateService.LocateAndNotifyIfInvalidWithCallState(
				parser, executor, executor, attrObjSplit[0], LocateFlags.All);

			if (maybeLocateTarget.IsError)
			{
				await notifyService.Notify(executor, maybeLocateTarget.AsError.Message!);
				return new CallState(ErrorMessages.Returns.NotVisible);
			}

			objToEvaluate = maybeLocateTarget.AsSharpObject;
			attrToEvaluate = attrObjSplit[1];

			var attr = await attributeService.GetAttributeAsync(
				executor, objToEvaluate, attrToEvaluate, IAttributeService.AttributeMode.Execute);

			// Only pin a real attribute. attr is an OptionalSharpAttributeOrError: when the
			// attribute is absent (None), !IsError is still true but AsAttribute throws,
			// which silently aborts the whole @message (no recipients notified). Guard on IsAttribute
			// so a missing format attr falls through to the per-recipient default (defmsg) instead.
			if (attr.IsAttribute)
			{
				pinnedAttribute = attr.AsAttribute;
			}
		}
		else
		{
			attrToEvaluate = attrObjSplit.Length > 1 ? attrObjSplit[1] : objectAttrArg;
		}

		var notificationType = isNospoof
			? (await permissionService.CanNoSpoof(executor)
				? INotifyService.NotificationType.NSAnnounce
				: INotifyService.NotificationType.Announce)
			: INotifyService.NotificationType.Announce;

		var enactor = isSpoof
			? (await parser.CurrentState.EnactorObject(mediator)).WithoutNone()
			: executor;

		if (isSpoof && !await permissionService.CanNoSpoof(executor))
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LackSpoofingPermissions), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		int recipientCount = 0;
		var hadErrors = false;

		foreach (var target in recipientNamelist)
		{
			var targetString = target.Match(dbref => dbref.ToString(), str => str);
			var maybeLocateTarget = await locateService.LocateAndNotifyIfInvalidWithCallState(
				parser, executor, executor, targetString, LocateFlags.All);

			if (maybeLocateTarget.IsError)
			{
				continue;
			}

			var locateTarget = maybeLocateTarget.AsSharpObject;

			if (isRemit)
			{
				if (!locateTarget.IsContainer)
				{
					continue;
				}

				var container = locateTarget.AsContainer;
				var evaluatedMessage = await EvaluateMessageForRecipient(
					parser, attributeService, executor, enactor,
					locateTarget, objToEvaluate, attrToEvaluate,
					pinnedAttribute, defmsg, functionArgs);

				await communicationService.SendToRoomAsync(
					enactor, container, _ => evaluatedMessage.Message ?? defmsg, notificationType);
				hadErrors |= evaluatedMessage.HadErrors;

				recipientCount++;
				continue;
			}

			if (isOemit)
			{
				continue;
			}

			if (!await permissionService.CanInteract(executor, locateTarget, InteractType.Hear))
			{
				continue;
			}

			var message = await EvaluateMessageForRecipient(
				parser, attributeService, executor, enactor,
				locateTarget, objToEvaluate, attrToEvaluate,
				pinnedAttribute, defmsg, functionArgs);

			hadErrors |= message.HadErrors;
			await notifyService.Notify(locateTarget, message.Message ?? defmsg, enactor, notificationType);
			recipientCount++;
		}

		if (isOemit)
		{
			var excludeObjects = new HashSet<AnySharpObject>();
			foreach (var target in recipientNamelist)
			{
				var targetString = target.Match(dbref => dbref.ToString(), str => str);
				var maybeLocateTarget = await locateService.LocateAndNotifyIfInvalidWithCallState(
					parser, executor, executor, targetString, LocateFlags.All);

				if (!maybeLocateTarget.IsError)
				{
					excludeObjects.Add(maybeLocateTarget.AsSharpObject);
				}
			}

			var executorLocation = await executor.Where();
			var message = await EvaluateMessageForRecipient(
				parser, attributeService, executor, enactor,
				executor, objToEvaluate, attrToEvaluate,
				pinnedAttribute, defmsg, functionArgs);

			await communicationService.SendToRoomAsync(
				enactor, executorLocation, _ => message.Message ?? defmsg, notificationType, excludeObjects: excludeObjects);
			hadErrors |= message.HadErrors;

			recipientCount = 1;
		}

		if (!isSilent && recipientCount > 0)
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MessageSentToRecipientsFormat), executor, recipientCount);
		}

		return CallState.Empty with { HadErrors = hadErrors };
	}

	private static async ValueTask<CallState> EvaluateMessageForRecipient(
		IMUSHCodeParser parser,
		IAttributeService attributeService,
		AnySharpObject executor,
		AnySharpObject enactor,
		AnySharpObject recipient,
		AnySharpObject? objToEvaluate,
		string attrToEvaluate,
		SharpAttribute[]? pinnedAttribute,
		MString defmsg,
		IEnumerable<KeyValuePair<string, CallState>> functionArgs)
	{
		var finalObjToEvaluate = objToEvaluate ?? recipient;

		var processedArgs = functionArgs.Select(kvp =>
		{
			var value = kvp.Value.Message?.ToPlainText() ?? string.Empty;
			if (value == RecipientReplacementToken)
			{
				return new KeyValuePair<string, CallState>(
					kvp.Key,
					new CallState(MarkupText.Plain(recipient.Object().DBRef.ToString()!)));
			}
			return kvp;
		}).ToDictionary();

		if (pinnedAttribute != null)
		{
			var result = await parser.With(
				state => state with
				{
					Executor = finalObjToEvaluate.Object().DBRef,
					Enactor = enactor.Object().DBRef,
					Caller = state.Executor,
					Arguments = processedArgs,
					// %0..%9 resolve from EnvironmentRegisters (see Substitutions), not Arguments,
					// so the per-recipient format args (message text + the ## recipient-dbref token)
					// must be placed here too — mirroring EvaluateAttributeFunctionAsync, which sets
					// both. Without this, an explicit <obj>/<attr> format (the pinned path) renders
					// with empty %N, dropping the message and recipient token.
					EnvironmentRegisters = processedArgs
				},
				newParser => newParser.FunctionParse(pinnedAttribute.Last().Value));

			return result is null ? new CallState(defmsg) : result with { Message = result.Message ?? defmsg };
		}
		else
		{
			var maybeAttr = await attributeService.GetAttributeAsync(
				executor, finalObjToEvaluate,
				attrToEvaluate, IAttributeService.AttributeMode.Execute);

			if (maybeAttr.IsError || maybeAttr.IsNone)
			{
				return new CallState(defmsg);
			}
			else
			{
				var result = await parser.With(
					state => state with
					{
						Enactor = enactor.Object().DBRef,
						Caller = state.Executor
					},
					newParser => attributeService.EvaluateAttributeFunctionResultAsync(
						newParser, recipient, recipient, attrToEvaluate, processedArgs));

				return result with { Message = result.Message ?? defmsg };
			}
		}
	}
}
