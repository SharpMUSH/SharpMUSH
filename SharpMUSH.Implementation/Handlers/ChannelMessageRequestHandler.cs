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
	ILogger<ChannelMessageRequestHandler> logger)
	: INotificationHandler<ChannelMessageNotification>
{
	public async ValueTask Handle(ChannelMessageNotification notification, CancellationToken cancellationToken)
	{
		var channelMembers = await notification.Channel.Members.Value.ToArrayAsync(cancellationToken);
		var chanName = notification.Channel.Name;

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
			if (mogrifierResult != null && !mogrifierResult.IsNone)
			{
				var mogrifierObj = mogrifierResult.Known();
				var source = notification.Source.IsNone ? mogrifierObj : notification.Source.Known();

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
							["7"] = new CallState(MarkupText.Plain(string.Join(" ", notification.Options)))
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
							["7"] = new CallState(MarkupText.Plain(string.Join(" ", notification.Options)))
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

		if (blockMessage != null && !notification.Source.IsNone)
		{
			await notifyService.Notify(notification.Source.Known(), blockMessage, notification.Source.Known, notification.MessageType);
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
			var sourceNumber = notification.Source.IsNone
				? (int?)null
				: notification.Source.Known().Object().DBRef.Number;

			foreach (var (member, status) in channelMembers)
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
				var wantsToHear = notification.Source.IsNone ||
													await permissionService.CanInteract(notification.Source.Known(), member,
														IPermissionService.InteractType.Hear);

				if (!isGagged && wantsToHear)
				{
					// Apply individual @chatformat unless MOGRIFY`OVERRIDE was set
					var finalMessage = message;
					if (!skipChatFormat)
					{
						finalMessage = await ApplyPlayerChatFormat(
							member,
							notification.Source,
							chatType,
							notification.Channel.Name,
							mogrifiedMessage,
							mogrifiedPlayerName,
							mogrifiedTitle,
							message,
							mogrifiedSays,
							notification.Options);
					}

					await notifyService.Notify(member, finalMessage, notification.Source.Known, notification.MessageType);
				}
			}

			if (!skipBuffer)
			{
				logger.LogInformation("{ChannelMessage}", MarkupTextSerializer.Serialize(message));

				// Add to channel recall buffer - only if there's an actual source
				if (!notification.Source.IsNone)
				{
					var sourceDbRef = notification.Source.Match(
						player => player.Object.DBRef,
						room => room.Object.DBRef,
						exit => exit.Object.DBRef,
						thing => thing.Object.DBRef,
						_ => new DBRef(0));

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
	/// Applies individual player's @chatformat to channel messages.
	/// Checks for CHATFORMAT`<channel> attribute on the player.
	/// </summary>
	private async ValueTask<MString> ApplyPlayerChatFormat(
		AnySharpObject player,
		AnyOptionalSharpObject source,
		string chatType,
		MString channelName,
		MString message,
		MString playerName,
		MString title,
		MString defaultFormat,
		MString says,
		string[] options)
	{
		var chatFormatAttrName = $"CHATFORMAT`{channelName.ToPlainText().ToUpper()}";

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
			["7"] = new CallState(MarkupText.Plain(string.Join(" ", options)))
		};

		var sourceObj = source.IsNone ? player : source.Known();
		return await AttributeHelpers.EvaluateFormatAttribute(
			attributeService,
			null, // parser - not needed for attribute evaluation
			sourceObj,
			player,
			chatFormatAttrName,
			formatArgs,
			defaultFormat,
			checkParents: true);
	}

	/// <summary>
	/// Evaluates a mogrify attribute on the mogrifier object
	/// </summary>
	private async ValueTask<MString> EvaluateMogrifyAttribute(AnySharpObject executor, AnySharpObject mogrifier, string attributeName, Dictionary<string, CallState> args)
	{
		try
		{
			var result = await attributeService.EvaluateAttributeFunctionAsync(
				null!, // parser - not needed for attribute evaluation
				executor,
				mogrifier,
				attributeName,
				args,
				evalParent: true,
				ignorePermissions: false);

			return result;
		}
		catch
		{
			// If attribute doesn't exist or evaluation fails, return empty
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