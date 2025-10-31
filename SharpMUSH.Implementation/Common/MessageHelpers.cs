using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSH.Library.Services.Interfaces.IPermissionService;

namespace SharpMUSH.Implementation.Common;

public static class MessageHelpers
{
	private const string SpecialRecipientDbref = "#-2"; // When specified, attribute is on recipient
	private const string RecipientReplacementToken = "##"; // Replaced with recipient's dbref
	
	/// <summary>
	/// Core logic for @message command and message() function.
	/// </summary>
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

		// Parse object/attribute specification
		var attrObjSplit = objectAttrArg.Split('/', 2);
		AnySharpObject? objToEvaluate = null;
		string attrToEvaluate;
		SharpAttribute[]? pinnedAttribute = null;

		// Check if object is specified (obj/attr format)
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
			// If object is #-2 or not specified, attribute is on recipient
			attrToEvaluate = attrObjSplit.Length > 1 ? attrObjSplit[1] : objectAttrArg;
		}

		// Determine notification type based on switches
		var notificationType = isNospoof
			? (await permissionService.CanNoSpoof(executor)
				? INotifyService.NotificationType.NSAnnounce
				: INotifyService.NotificationType.Announce)
			: INotifyService.NotificationType.Announce;

		// Get enactor for spoof switch
		var enactor = isSpoof
			? (await parser.CurrentState.EnactorObject(mediator)).WithoutNone()
			: executor;

		// Check spoof permission - require CanNoSpoof for now (similar to nospoof check)
		if (isSpoof && !await permissionService.CanNoSpoof(executor))
		{
			await notifyService.Notify(executor, "Permission denied: You lack spoofing permissions.");
			return new CallState(Errors.ErrorPerm);
		}

		int recipientCount = 0;

		// Process each recipient
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

			// Handle /remit switch - send to room contents
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

			// Handle /oemit switch - exclude these objects
			if (isOemit)
			{
				// For oemit, we need to collect all objects to exclude
				// This is handled differently - we'll need to collect all recipients first
				continue;
			}

			// Standard recipient handling
			if (!await permissionService.CanInteract(locateTarget, executor, InteractType.Hear))
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

		// Handle /oemit switch separately
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

			recipientCount = 1; // For confirmation message
		}

		// Show confirmation message if not silent
		if (!isSilent && recipientCount > 0)
		{
			await notifyService.Notify(executor, $"Message sent to {recipientCount} recipient(s).");
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

		// Replace ## with recipient's dbref in arguments
		var processedArgs = functionArgs.Select(kvp =>
		{
			var value = kvp.Value.Message?.ToString() ?? string.Empty;
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
					Enactor = enactor.Object().DBRef,
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

			if (maybeAttr.IsError)
			{
				return defmsg;
			}
			else
			{
				var result = await parser.With(
					state => state with
					{
						Enactor = enactor.Object().DBRef,
						Arguments = processedArgs
					},
					newParser => attributeService.EvaluateAttributeFunctionAsync(
						newParser, recipient, recipient, attrToEvaluate, processedArgs));

				return result ?? defmsg;
			}
		}
	}
}
