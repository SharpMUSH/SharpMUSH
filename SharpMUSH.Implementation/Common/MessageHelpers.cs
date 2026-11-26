using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSH.Library.Services.Interfaces.IPermissionService;
namespace SharpMUSH.Implementation.Common;

/// <summary>The kind of speech an @message or message() call is, with /say, /pose, /semipose or /emit; None for a plain one.</summary>
public enum MessageSpeech { None, Say, Pose, SemiPose, Emit }

public static class MessageHelpers
{
	/// <summary>
	/// The speech kind a switch list names, or None. Two kinds at once is an error: the caller refuses it.
	/// </summary>
	public static Result<MessageSpeech> SpeechOf(IEnumerable<string> switches)
	{
		var named = switches
			.Select(name => name.ToUpperInvariant() switch
			{
				"SAY" => MessageSpeech.Say,
				"POSE" => MessageSpeech.Pose,
				"SEMIPOSE" => MessageSpeech.SemiPose,
				"EMIT" => MessageSpeech.Emit,
				_ => MessageSpeech.None
			})
			.Where(kind => kind != MessageSpeech.None)
			.Distinct()
			.ToList();
		return named.Count switch
		{
			0 => MessageSpeech.None,
			1 => named[0],
			_ => new Error<string>(nameof(ErrorMessages.Notifications.MessageOneSpeechKind))
		};
	}

	private const string SpecialRecipientDbref = "#-2";
	private const string RecipientReplacementToken = "##";

	public static async ValueTask<CallState> ProcessMessageAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		ILocateService locateService,
		IAttributeService attributeService,
		INotifyService notifyService,
		IPermissionService permissionService,
		ICommunicationService communicationService,
		ILockService lockService,
		IDidItService didItService,
		AnySharpObject executor,
		MString recipientsArg,
		MString defmsg,
		string objectAttrArg,
		IEnumerable<KeyValuePair<string, CallState>> functionArgs,
		bool isRemit = false,
		bool isOemit = false,
		bool isNospoof = false,
		bool isSpoof = false,
		bool isSilent = true,
		MessageSpeech speech = MessageSpeech.None)
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

			attrToEvaluate = attrObjSplit[1];

			switch (maybeLocateTarget)
			{
				case Error<CallState> error:
					await notifyService.Notify(executor, error.Value.Message);
					return new CallState(ErrorMessages.Returns.NotVisible);
				case AnySharpObject located:
					objToEvaluate = located;
					var attr = await attributeService.GetAttributeAsync(
						executor, located, attrToEvaluate, IAttributeService.AttributeMode.Execute);

					// Only pin a real attribute: a missing format attribute falls through to the
					// per-recipient default (defmsg) rather than aborting the whole @message.
					if (attr is SharpAttribute[] found)
					{
						pinnedAttribute = found;
					}

					break;
			}
		}
		else
		{
			attrToEvaluate = attrObjSplit.Length > 1 ? attrObjSplit[1] : objectAttrArg;
		}

		// PennMUSH's do_message hands the message to do_pemit, do_remit or do_oemit_list, so it is heard the way
		// those are: listeners (@listen, ^-patterns, puppets) hear it, as they hear @pemit and @remit.
		var noSpoof = isNospoof && await permissionService.CanNoSpoof(executor);
		// A speech kind is heard as that speech: a say, pose, semipose or emit, never a private @pemit.
		var notificationType = (speech, isRemit || isOemit, noSpoof) switch
		{
			(MessageSpeech.Say, _, true) => INotifyService.NotificationType.NSSay,
			(MessageSpeech.Say, _, false) => INotifyService.NotificationType.Say,
			(MessageSpeech.Pose, _, true) => INotifyService.NotificationType.NSPose,
			(MessageSpeech.Pose, _, false) => INotifyService.NotificationType.Pose,
			(MessageSpeech.SemiPose, _, true) => INotifyService.NotificationType.NSSemiPose,
			(MessageSpeech.SemiPose, _, false) => INotifyService.NotificationType.SemiPose,
			(MessageSpeech.Emit, _, true) or (MessageSpeech.None, true, true) => INotifyService.NotificationType.NSEmit,
			(MessageSpeech.Emit, _, false) or (MessageSpeech.None, true, false) => INotifyService.NotificationType.Emit,
			(MessageSpeech.None, false, true) => INotifyService.NotificationType.NSPrivateEmit,
			_ => INotifyService.NotificationType.PrivateEmit
		};

		var enactor = isSpoof
			? await parser.CurrentState.KnownEnactorObject(mediator)
			: executor;

		if (isSpoof && !await permissionService.CanNoSpoof(executor))
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LackSpoofingPermissions), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// Speech is admitted as the built-in's is: the speaker (the enactor with /spoof) must pass the Speech lock of
		// where the line is spoken, which LOUD passes. That is the speaker's own location, or with /oemit the
		// executor's, where /oemit sends it; each is checked once. With /remit each room is checked, and a refused
		// room is skipped.
		if (speech != MessageSpeech.None && !isRemit)
		{
			var speakerLocation = await enactor.Where();
			var spokenIn = isOemit ? await executor.Where() : speakerLocation;
			if (!await MaySpeakInAsync(parser, lockService, didItService, enactor, spokenIn,
					here: spokenIn.Object().DBRef == speakerLocation.Object().DBRef))
			{
				return CallState.Empty;
			}
		}

		int recipientCount = 0;
		AnySharpObject? lastRecipient = null;
		var hadErrors = false;

		foreach (var target in recipientNamelist)
		{
			var targetString = target switch { DBRef dbref => dbref.ToString(), string name => name };
			var maybeLocateTarget = await locateService.LocateAndNotifyIfInvalidWithCallState(
				parser, executor, executor, targetString, LocateFlags.All);

			if (maybeLocateTarget is not AnySharpObject locateTarget)
			{
				continue;
			}

			if (isRemit)
			{
				if (locateTarget.AsOptionalContainer is not AnySharpContainer container)
				{
					continue;
				}

				if (speech != MessageSpeech.None
						&& !await MaySpeakInAsync(parser, lockService, didItService, enactor, container, here: false))
				{
					continue;
				}

				var evaluatedMessage = await EvaluateMessageForRecipient(
					parser, attributeService, executor, enactor,
					locateTarget, objToEvaluate, attrToEvaluate,
					pinnedAttribute, defmsg, functionArgs);

				await communicationService.SendToRoomAsync(
					enactor, container, _ => evaluatedMessage.Message, notificationType);
				hadErrors |= evaluatedMessage.HadErrors;

				// do_one_remit: each room but the one the executor is in is confirmed by name.
				if (!isSilent && (await executor.Where()).Object().DBRef != locateTarget.Object().DBRef)
				{
					await notifyService.NotifyLocalizedMarkup(executor, nameof(ErrorMessages.Notifications.YouRemitInFormat), executor,
						defmsg, MString.Plain($"{locateTarget.Object().Name}(#{locateTarget.Object().DBRef.Number})"));
				}

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
			await notifyService.Notify(locateTarget, message.Message, enactor, notificationType);
			recipientCount++;
			lastRecipient = locateTarget;
		}

		if (isOemit)
		{
			var excludeObjects = new HashSet<AnySharpObject>();
			foreach (var target in recipientNamelist)
			{
				var targetString = target switch { DBRef dbref => dbref.ToString(), string name => name };
				var maybeLocateTarget = await locateService.LocateAndNotifyIfInvalidWithCallState(
					parser, executor, executor, targetString, LocateFlags.All);

				if (maybeLocateTarget is AnySharpObject excluded)
				{
					excludeObjects.Add(excluded);
				}
			}

			var executorLocation = await executor.Where();
			var message = await EvaluateMessageForRecipient(
				parser, attributeService, executor, enactor,
				executor, objToEvaluate, attrToEvaluate,
				pinnedAttribute, defmsg, functionArgs);

			await communicationService.SendToRoomAsync(
				enactor, executorLocation, _ => message.Message, notificationType, excludeObjects: excludeObjects);
			hadErrors |= message.HadErrors;

		}

		// do_pemit's confirmation, with the default message as PennMUSH gives it; @message/remit confirmed each room
		// above, and @message/oemit, as do_oemit_list, says nothing.
		if (!isSilent && lastRecipient is not null
				&& (recipientCount > 1 || lastRecipient.Object().DBRef != executor.Object().DBRef))
		{
			await notifyService.NotifyLocalizedMarkup(executor, recipientCount > 1
					? nameof(ErrorMessages.Notifications.YouPemitToCountFormat)
					: nameof(ErrorMessages.Notifications.YouPemitToObjectFormat),
				executor, defmsg, MString.Plain(recipientCount > 1 ? recipientCount.ToString() : lastRecipient.Object().Name));
		}

		return CallState.Empty with { HadErrors = hadErrors };
	}

	private static async ValueTask<bool> MaySpeakInAsync(IMUSHCodeParser parser, ILockService lockService,
		IDidItService didItService, AnySharpObject speaker, AnySharpContainer location, bool here)
	{
		if (await speaker.IsLoud() || await lockService.Evaluate(LockType.Speech, location.WithExitOption(), speaker)) return true;
		await didItService.FailLockLocalized(parser, speaker, location.WithExitOption(), LockType.Speech,
			new LocalizedNotification(here ? nameof(ErrorMessages.Notifications.MayNotSpeakHere) : nameof(ErrorMessages.Notifications.MayNotSpeakThere)));
		return false;
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
			var value = kvp.Value.Message.ToPlainText();
			if (value == RecipientReplacementToken)
			{
				// PennMUSH substitutes unparse_dbref(target) -- the short "#N", not the objid
				// (src/notify.c) -- so a format can compare it against %#.
				return new KeyValuePair<string, CallState>(
					kvp.Key,
					new CallState(MarkupText.Plain($"#{recipient.Object().DBRef.Number}")));
			}
			return kvp;
		}).ToDictionary();

		if (pinnedAttribute != null)
		{
			var result = await parser.With(
				state => state.ForAttributeArguments(ArgumentList.From(processedArgs)) with
				{
					Actors = state.Actors with { Executor = finalObjToEvaluate.Object().DBRef, Enactor = enactor.Object().DBRef, Caller = state.Executor }
				},
				newParser => newParser.FunctionParse(pinnedAttribute.Last().Value));

			return result ?? new CallState(defmsg);
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
						Actors = state.Actors with { Enactor = enactor.Object().DBRef, Caller = state.Executor }
					},
					newParser => attributeService.EvaluateAttributeFunctionResultAsync(
						newParser, recipient, recipient, attrToEvaluate, processedArgs));

				return result;
			}
		}
	}
}
