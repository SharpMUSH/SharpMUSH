using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

public class ChannelMessageRequestHandler(
	IPermissionService permissionService,
	INotifyService notifyService,
	IMediator mediator,
	IAttributeService attributeService,
	IMUSHCodeParser parser,
	ILogger<ChannelMessageRequestHandler> logger)
	: INotificationHandler<ChannelMessageNotification>
{
	public async ValueTask Handle(ChannelMessageNotification notification, CancellationToken cancellationToken)
	{
		var chanName = notification.Channel.Name;
		var sender = notification.Source is AnySharpObject found ? found : null;
		// extchat.c:3944-3948 - %7 is one of two literals for every send, never the raw switch list:
		// a /silent send reports "silent", everything else reports "noisy".
		var options = notification.Options.Contains("silent", StringComparer.OrdinalIgnoreCase)
			? "silent"
			: "noisy";

		var chatType = notification.MessageType switch
		{
			INotifyService.NotificationType.Pose => ":",
			INotifyService.NotificationType.NSPose => ":",
			INotifyService.NotificationType.SemiPose => ";",
			INotifyService.NotificationType.NSSemiPose => ";",
			INotifyService.NotificationType.Emit => "@",
			INotifyService.NotificationType.NSEmit => "@",
			INotifyService.NotificationType.Announce => "|",
			INotifyService.NotificationType.NSAnnounce => "|",
			INotifyService.NotificationType.Say => "\"",
			INotifyService.NotificationType.NSSay => "\"",
			_ => throw new ArgumentOutOfRangeException()
		};

		var mogrifiedChanName = MarkupText.Concat([MarkupText.Plain("<"), chanName, MarkupText.Plain(">")]);
		var mogrifiedTitle = notification.Title;
		var mogrifiedPlayerName = notification.PlayerName;
		var mogrifiedSays = notification.Says;
		var mogrifiedMessage = notification.Message;
		var skipChatFormat = false;
		var skipBuffer = false;
		MString? blockMessage = null;
		MString? formatOverride = null;

		if (!string.IsNullOrEmpty(notification.Channel.Mogrifier))
		{
			var mogrifierResult = await mediator.Send(new GetObjectNodeQuery(DBRef.Parse(notification.Channel.Mogrifier)), cancellationToken);
			if (mogrifierResult is AnySharpObject mogrifierObj)
			{
				var source = sender ?? mogrifierObj;

				var passesUseLock = await permissionService.PassesLock(source, mogrifierObj, LockType.Use);

				if (passesUseLock)
				{
					// Common arguments for control mogrifiers (BLOCK, OVERRIDE, NOBUFFER)
					var controlArgs = new Dictionary<string, CallState>
					{
						["0"] = new CallState(MarkupText.Plain(chatType)),
						["1"] = new CallState(chanName),
						["2"] = new CallState(notification.Message),
						["3"] = new CallState(notification.PlayerName),
						["4"] = new CallState(notification.Title)
					};

					var blockResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`BLOCK", controlArgs);
					if (blockResult.Length > 0)
					{
						blockMessage = blockResult;
					}

					if (blockMessage == null)
					{
						var overrideResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`OVERRIDE", controlArgs);
						if (overrideResult.Length > 0 && !IsEmpty(overrideResult.ToPlainText()))
						{
							skipChatFormat = true;
						}

						var nobufferResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`NOBUFFER", controlArgs);
						if (nobufferResult.Length > 0 && !IsEmpty(nobufferResult.ToPlainText()))
						{
							skipBuffer = true;
						}

						// Common arguments for part mogrifiers
						var partArgs = new Dictionary<string, CallState>
						{
							// %0 varies by mogrifier (set individually)
							["1"] = new CallState(chanName),
							["2"] = new CallState(MarkupText.Plain(chatType)),
							["3"] = new CallState(notification.Message),
							["4"] = new CallState(notification.Title),
							["5"] = new CallState(notification.PlayerName),
							["6"] = new CallState(notification.Says),
							["7"] = new CallState(MarkupText.Plain(options))
						};

						partArgs["0"] = new CallState(mogrifiedChanName);
						var chanNameResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`CHANNAME", partArgs);
						if (chanNameResult.Length > 0)
						{
							mogrifiedChanName = chanNameResult;
						}

						partArgs["0"] = new CallState(mogrifiedTitle);
						var titleResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`TITLE", partArgs);
						if (titleResult.Length > 0)
						{
							mogrifiedTitle = titleResult;
						}

						partArgs["0"] = new CallState(mogrifiedPlayerName);
						var playerNameResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`PLAYERNAME", partArgs);
						if (playerNameResult.Length > 0)
						{
							mogrifiedPlayerName = playerNameResult;
						}

						partArgs["0"] = new CallState(mogrifiedSays);
						var saysResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`SPEECHTEXT", partArgs);
						if (saysResult.Length > 0)
						{
							mogrifiedSays = saysResult;
						}

						partArgs["0"] = new CallState(mogrifiedMessage);
						var messageResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`MESSAGE", partArgs);
						if (messageResult.Length > 0)
						{
							mogrifiedMessage = messageResult;
						}

						// MOGRIFY`FORMAT - channel-wide format (like @chatformat)
						// Arguments match @chatformat: %0=type, %1=channel, %2=message, %3=name, %4=title, %5=default, %6=says, %7=options
						var defaultMessage = BuildDefaultMessage(chatType, mogrifiedChanName, mogrifiedPlayerName, mogrifiedTitle, mogrifiedSays, mogrifiedMessage);
						var formatArgs = new Dictionary<string, CallState>
						{
							["0"] = new CallState(MarkupText.Plain(chatType)),
							["1"] = new CallState(chanName),
							["2"] = new CallState(mogrifiedMessage),
							["3"] = new CallState(mogrifiedPlayerName),
							["4"] = new CallState(mogrifiedTitle),
							["5"] = new CallState(defaultMessage),
							["6"] = new CallState(mogrifiedSays),
							["7"] = new CallState(MarkupText.Plain(options))
						};
						var formatResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`FORMAT", formatArgs);
						if (formatResult.Length > 0)
						{
							formatOverride = formatResult;
						}
					}
				}
			}
		}

		if (blockMessage != null && sender is not null)
		{
			await notifyService.Notify(sender, blockMessage, sender, notification.MessageType);
			return;
		}

		var message = formatOverride ?? BuildDefaultMessage(chatType, mogrifiedChanName, mogrifiedPlayerName, mogrifiedTitle, mogrifiedSays, mogrifiedMessage);

		using (logger.BeginScope(new Dictionary<string, string>
		{
			["ChannelId"] = notification.Channel.Id ?? string.Empty,
			["MessageType"] = notification.MessageType.ToString(),
			["Category"] = "logs"
		}))
		{
			var sourceNumber = sender?.Object().DBRef.Number;

			await foreach (var (member, status) in notification.Channel.Members.Value.WithCancellation(cancellationToken))
			{
				// CB_SEEALL (src/extchat.c:3958): a privileged-only line reaches See_All members and the
				// source, nobody else. Used for the connect/disconnect announcement of a hidden player.
				// Checked before the interaction lock so a skipped member costs no permission query.
				if (notification.SeeAllOnly
						&& member.Object().DBRef.Number != sourceNumber
						&& !await member.IsSee_All())
				{
					continue;
				}

				// CB_CHECKQUIET (src/extchat.c:3957): a presence announcement is withheld from a member who
				// muted the channel. This is the only reader of that flag.
				if (notification.CheckQuiet && (status.Mute ?? false))
				{
					continue;
				}

				var isGagged = status.Gagged ?? false;
				var wantsToHear = sender is null ||
													await permissionService.CanInteract(sender, member,
														IPermissionService.InteractType.Hear);

				if (!isGagged && wantsToHear)
				{
					// Apply individual @chatformat unless MOGRIFY`OVERRIDE was set
					var finalMessage = message;
					if (!skipChatFormat)
					{
						finalMessage = await ApplyPlayerChatFormat(
							member,
							sender,
							chatType,
							notification.Channel.Name,
							mogrifiedMessage,
							mogrifiedPlayerName,
							mogrifiedTitle,
							message,
							mogrifiedSays,
							options);
					}

					await notifyService.Notify(member, finalMessage, sender, notification.MessageType);
				}
			}

			if (!skipBuffer)
			{
				logger.LogInformation("{ChannelMessage}", MarkupTextSerializer.Serialize(message));

				// Add to channel recall buffer - only if there's an actual source
				if (sender is not null)
				{
					var sourceDbRef = sender.Object().DBRef;

					var channelMessage = new SharpChannelMessage
					{
						ChannelId = notification.Channel.Id ?? string.Empty,
						Timestamp = DateTimeOffset.UtcNow,
						Sender = sourceDbRef,
						Message = message,
						MessageType = notification.MessageType.ToString(),
						SeeAllOnly = notification.SeeAllOnly
					};
					await mediator.Send(new AddChannelMessageCommand(channelMessage), cancellationToken);
				}
			}
		}
	}

	/// <summary>
	/// Applies the receiving player's own <c>@chatformat</c> to a channel line.
	///
	/// <para>PennMUSH reads one attribute for this, <c>CHATFORMAT</c> (<c>src/extchat.c:3935</c>,
	/// <c>format.attr = "CHATFORMAT"</c>, <c>checkprivs = 0</c>), on each member in turn. It is not
	/// qualified by channel: a player who wants per-channel formatting branches on %1 inside the one
	/// attribute.</para>
	/// </summary>
	private async ValueTask<MString> ApplyPlayerChatFormat(
		AnySharpObject player,
		AnySharpObject? source,
		string chatType,
		MString channelName,
		MString message,
		MString playerName,
		MString title,
		MString defaultFormat,
		MString says,
		string options)
	{
		// Evaluate the chatformat attribute with standard arguments:
		// %0 = chat type character (", :, ;, @)
		// %1 = channel name
		// %2 = message
		// %3 = player name
		// %4 = title
		// %5 = default formatted message
		// %6 = says text
		// %7 = options (space-separated)
		var formatArgs = new Dictionary<string, CallState>
		{
			["0"] = new CallState(MarkupText.Plain(chatType)),
			["1"] = new CallState(channelName),
			["2"] = new CallState(message),
			["3"] = new CallState(playerName),
			["4"] = new CallState(title),
			["5"] = new CallState(defaultFormat),
			["6"] = new CallState(says),
			["7"] = new CallState(MarkupText.Plain(options))
		};

		// checkprivs = 0 (extchat.c:3935): the member's own CHATFORMAT is read without asking whether the
		// speaker could have evaluated it, so the member is its own executor for the permission check.
		// Asking as the speaker would drop a wizard's CHATFORMAT whenever a mortal spoke, since
		// CanEval refuses a mortal evaluating anything on a privileged object.
		//
		// The speaker still supplies the frame the body runs in, so %# and %@ are the speaker; the
		// attribute's own holder becomes %! when it runs.
		var sourceObj = source ?? player;
		return await AttributeHelpers.EvaluateFormatAttribute(
			attributeService,
			EvaluationContextFor(sourceObj),
			player,
			player,
			"CHATFORMAT",
			formatArgs,
			defaultFormat,
			checkParents: true);
	}

	/// <summary>
	/// A fresh evaluation context for softcode this pipeline runs on someone else's behalf.
	///
	/// <para><c>Mediator.Publish</c> hands a notification handler no parse frame, so the injected
	/// parser's state stack is empty and <c>CurrentState</c> would throw. <see cref="ParserState.RootFor"/>
	/// is the same answer <c>EventService</c> and the HTTP handler give to the same problem. The actor
	/// is the speaker, matching PennMUSH's <c>call_attrib(thing, attr, buff, player, …)</c>: the speaker
	/// is the enactor, and the attribute's holder becomes the executor once the body runs
	/// (<c>AttributeService.RunAsOwnerAsync</c>), which leaves the speaker as %@.</para>
	/// </summary>
	private IMUSHCodeParser EvaluationContextFor(AnySharpObject actor)
		=> parser.FromState(ParserState.RootFor(actor.Object().DBRef) with
		{
			ExecutionBudget = ExecutionBudget.Current
		});

	/// <summary>
	/// Evaluates one <c>MOGRIFY`*</c> attribute on the channel's mogrifier object.
	///
	/// <para>A mogrifier that is absent or that yields nothing leaves the message alone - that is the
	/// normal path, and it needs no exception. A mogrifier whose softcode actually throws must not take
	/// the channel send down with it, so the line still goes out unmogrified, but the failure is logged
	/// rather than discarded. Budget exhaustion is not a mogrifier fault and propagates.</para>
	/// </summary>
	private async ValueTask<MString> EvaluateMogrifyAttribute(AnySharpObject executor, AnySharpObject mogrifier, string attributeName, Dictionary<string, CallState> args)
	{
		try
		{
			return await attributeService.EvaluateAttributeFunctionAsync(
				EvaluationContextFor(executor),
				executor,
				mogrifier,
				attributeName,
				args,
				evalParent: true,
				ignorePermissions: false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Mogrifier {Mogrifier} failed to evaluate {Attribute}",
				mogrifier.Object().DBRef, attributeName);
			return MarkupText.Empty;
		}
	}

	/// <summary>
	/// Checks if a string value should be considered "empty" for mogrification purposes
	/// </summary>
	private static bool IsEmpty(string value)
	{
		return string.IsNullOrWhiteSpace(value) || value == "0" || value == "#-1" || value.ToLower() == "false";
	}

	/// <summary>
	/// Builds the default channel message format
	/// </summary>
	private static MString BuildDefaultMessage(string chatType, MString chanName, MString playerName, MString title, MString says, MString message)
	{
		return chatType switch
		{
			"@" or "|" => MarkupText.Concat([chanName, MarkupText.Space, message]),
			":" => MarkupText.Concat([chanName, MarkupText.Space, title.Length > 0 ? MarkupText.Concat([title, MarkupText.Space]) : MarkupText.Empty, playerName, MarkupText.Space, message]),
			";" => MarkupText.Concat([chanName, MarkupText.Space, title.Length > 0 ? MarkupText.Concat([title, MarkupText.Space]) : MarkupText.Empty, playerName, message]),
			_ => MarkupText.Concat([chanName, MarkupText.Space, title.Length > 0 ? MarkupText.Concat([title, MarkupText.Space]) : MarkupText.Empty, playerName, MarkupText.Space, says, MarkupText.Plain(", \""), message, MarkupText.Plain("\"")])
		};
	}
}