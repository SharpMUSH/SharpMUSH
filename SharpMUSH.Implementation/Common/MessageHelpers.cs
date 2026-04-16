using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
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

	/// <summary>
	/// Strips the message type prefix from the message if present.
	/// </summary>
	/// <param name="message">The message to strip</param>
	/// <returns>The message without the prefix</returns>
	public static string StripMessageTypePrefix(string message)
	{
		return message switch
		{
			[':', .. var rest] => new string(rest.ToArray()),
			[';', .. var rest] => new string(rest.ToArray()),
			['|', .. var rest] => new string(rest.ToArray()),
			_ => message
		};
	}

	/// <summary>
	/// Formats a list of strings using Oxford comma style.
	/// Examples: ["East"] → "East"; ["East", "West"] → "East and West";
	/// ["East", "West", "North"] → "East, West, and North"
	/// </summary>
	/// <param name="items">The items to format</param>
	/// <returns>The formatted string</returns>
	public static string FormatWithOxfordComma(IReadOnlyList<string> items) => items.Count switch
	{
		0 => string.Empty,
		1 => items[0],
		2 => $"{items[0]} and {items[1]}",
		_ => string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[^1]
	};

	/// <summary>
	/// Formats an object name with its dbref and flag symbols, matching PennMUSH display format.
	/// Example: "Chest(#5Tn)"
	/// </summary>
	/// <param name="obj">The sharp object to format</param>
	/// <returns>A task yielding the formatted string</returns>
	public static async ValueTask<string> FormatObjectWithDbref(Library.Models.SharpObject obj)
	{
		var flags = await obj.Flags.Value.ToArrayAsync();
		var flagSymbols = string.Join(string.Empty, flags.Select(x => x.Symbol));
		return $"{obj.Name}(#{obj.DBRef.Number}{flagSymbols})";
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
				return new CallState(Errors.ErrorNotVisible);
			}

			objToEvaluate = maybeLocateTarget.AsSharpObject;
			attrToEvaluate = string.Join("/", attrObjSplit.Skip(1));

			var attr = await attributeService.GetAttributeAsync(
				executor, objToEvaluate, attrToEvaluate, IAttributeService.AttributeMode.Execute);

			if (!attr.IsError)
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
			return new CallState(Errors.ErrorPerm);
		}

		int recipientCount = 0;

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
					enactor, container, _ => evaluatedMessage, notificationType);

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

			await notifyService.Notify(locateTarget, message, enactor, notificationType);
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
				enactor, executorLocation, _ => message, notificationType, excludeObjects: excludeObjects);

			recipientCount = 1;
		}

		if (!isSilent && recipientCount > 0)
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MessageSentToRecipientsFormat), executor, recipientCount);
		}

		return CallState.Empty;
	}

	private static async ValueTask<MString> EvaluateMessageForRecipient(
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
					new CallState(MModule.single(recipient.Object().DBRef.ToString()!)));
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
					Arguments = processedArgs
				},
				newParser => newParser.FunctionParse(pinnedAttribute.Last().Value));

			return result?.Message ?? defmsg;
		}
		else
		{
			var maybeAttr = await attributeService.GetAttributeAsync(
				executor, finalObjToEvaluate,
				attrToEvaluate, IAttributeService.AttributeMode.Execute);

			if (maybeAttr.IsError || maybeAttr.IsNone)
			{
				return defmsg;
			}
			else
			{
				var result = await parser.With(
					state => state with
					{
						Enactor = enactor.Object().DBRef,
						Caller = state.Executor
					},
					newParser => attributeService.EvaluateAttributeFunctionAsync(
						newParser, recipient, recipient, attrToEvaluate, processedArgs));

				return result ?? defmsg;
			}
		}
	}
}
