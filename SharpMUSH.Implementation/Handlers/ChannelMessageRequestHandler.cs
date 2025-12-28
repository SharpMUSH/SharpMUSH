using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
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
		
		// Determine chat type character
		var chatType = notification.MessageType switch
		{
			INotifyService.NotificationType.Pose => ":",
			INotifyService.NotificationType.SemiPose => ";",
			INotifyService.NotificationType.Emit => "@",
			_ => "\""
		};
		
		// Initialize mogrified values with defaults
		var mogrifiedChanName = MModule.multiple([MModule.single("<"), chanName, MModule.single(">")]);
		var mogrifiedTitle = notification.Title;
		var mogrifiedPlayerName = notification.PlayerName;
		var mogrifiedSays = notification.Says;
		var mogrifiedMessage = notification.Message;
		var skipChatFormat = false;
		var skipBuffer = false;
		MString? blockMessage = null;
		MString? formatOverride = null;
		
		// Process mogrification if mogrifier is set
		if (!string.IsNullOrEmpty(notification.Channel.Mogrifier))
		{
			var mogrifierResult = await mediator.Send(new GetObjectNodeQuery(DBRef.Parse(notification.Channel.Mogrifier)), cancellationToken);
			if (mogrifierResult != null && !mogrifierResult.IsNone)
			{
				var mogrifierObj = mogrifierResult.Known();
				var source = notification.Source.IsNone ? mogrifierObj : notification.Source.Known();
				
				// Check if speaker passes Use lock on mogrifier
				var passesUseLock = permissionService.PassesLock(source, mogrifierObj, LockType.Use);
				
				if (passesUseLock)
				{
					// Common arguments for control mogrifiers (BLOCK, OVERRIDE, NOBUFFER)
					var controlArgs = new Dictionary<string, CallState>
					{
						["0"] = new CallState(MModule.single(chatType)),
						["1"] = new CallState(chanName),
						["2"] = new CallState(notification.Message),
						["3"] = new CallState(notification.PlayerName),
						["4"] = new CallState(notification.Title)
					};
					
					// Check MOGRIFY`BLOCK
					var blockResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`BLOCK", controlArgs);
					if (MModule.getLength(blockResult) > 0)
					{
						blockMessage = blockResult;
					}
					
					// Only proceed with mogrification if not blocked
					if (blockMessage == null)
					{
						// Check MOGRIFY`OVERRIDE
						var overrideResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`OVERRIDE", controlArgs);
						if (MModule.getLength(overrideResult) > 0 && !IsEmpty(overrideResult.ToPlainText()))
						{
							skipChatFormat = true;
						}
						
						// Check MOGRIFY`NOBUFFER
						var nobufferResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`NOBUFFER", controlArgs);
						if (MModule.getLength(nobufferResult) > 0 && !IsEmpty(nobufferResult.ToPlainText()))
						{
							skipBuffer = true;
						}
						
						// Common arguments for part mogrifiers
						var partArgs = new Dictionary<string, CallState>
						{
							// %0 varies by mogrifier (set individually)
							["1"] = new CallState(chanName),
							["2"] = new CallState(MModule.single(chatType)),
							["3"] = new CallState(notification.Message),
							["4"] = new CallState(notification.Title),
							["5"] = new CallState(notification.PlayerName),
							["6"] = new CallState(notification.Says),
							["7"] = new CallState(MModule.single(string.Join(" ", notification.Options)))
						};
						
						// MOGRIFY`CHANNAME
						partArgs["0"] = new CallState(mogrifiedChanName);
						var chanNameResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`CHANNAME", partArgs);
						if (MModule.getLength(chanNameResult) > 0)
						{
							mogrifiedChanName = chanNameResult;
						}
						
						// MOGRIFY`TITLE
						partArgs["0"] = new CallState(mogrifiedTitle);
						var titleResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`TITLE", partArgs);
						if (MModule.getLength(titleResult) > 0)
						{
							mogrifiedTitle = titleResult;
						}
						
						// MOGRIFY`PLAYERNAME
						partArgs["0"] = new CallState(mogrifiedPlayerName);
						var playerNameResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`PLAYERNAME", partArgs);
						if (MModule.getLength(playerNameResult) > 0)
						{
							mogrifiedPlayerName = playerNameResult;
						}
						
						// MOGRIFY`SPEECHTEXT
						partArgs["0"] = new CallState(mogrifiedSays);
						var saysResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`SPEECHTEXT", partArgs);
						if (MModule.getLength(saysResult) > 0)
						{
							mogrifiedSays = saysResult;
						}
						
						// MOGRIFY`MESSAGE
						partArgs["0"] = new CallState(mogrifiedMessage);
						var messageResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`MESSAGE", partArgs);
						if (MModule.getLength(messageResult) > 0)
						{
							mogrifiedMessage = messageResult;
						}
						
						// MOGRIFY`FORMAT - channel-wide format (like @chatformat)
						// Arguments match @chatformat: %0=type, %1=channel, %2=message, %3=name, %4=title, %5=default, %6=says, %7=options
						var defaultMessage = BuildDefaultMessage(chatType, mogrifiedChanName, mogrifiedPlayerName, mogrifiedTitle, mogrifiedSays, mogrifiedMessage);
						var formatArgs = new Dictionary<string, CallState>
						{
							["0"] = new CallState(MModule.single(chatType)),
							["1"] = new CallState(chanName),
							["2"] = new CallState(mogrifiedMessage),
							["3"] = new CallState(mogrifiedPlayerName),
							["4"] = new CallState(mogrifiedTitle),
							["5"] = new CallState(defaultMessage),
							["6"] = new CallState(mogrifiedSays),
							["7"] = new CallState(MModule.single(string.Join(" ", notification.Options)))
						};
						var formatResult = await EvaluateMogrifyAttribute(source, mogrifierObj, "MOGRIFY`FORMAT", formatArgs);
						if (MModule.getLength(formatResult) > 0)
						{
							formatOverride = formatResult;
						}
					}
				}
			}
		}
		
		// If message was blocked, send block message to speaker only and return
		if (blockMessage != null && !notification.Source.IsNone)
		{
			await notifyService.Notify(notification.Source.Known(), blockMessage, notification.Source.Known, notification.MessageType);
			return;
		}
		
		// Build the final message
		var message = formatOverride ?? BuildDefaultMessage(chatType, mogrifiedChanName, mogrifiedPlayerName, mogrifiedTitle, mogrifiedSays, mogrifiedMessage);

		using (logger.BeginScope(new Dictionary<string, string>
		       {
			       ["ChannelId"] = notification.Channel.Id ?? string.Empty,
			       ["MessageType"] = notification.MessageType.ToString(),
			       ["Category"] = "logs"
		       }))
		{
			foreach (var (member, status) in channelMembers)
			{
				var isGagged = status.Gagged ?? false;
				var wantsToHear = notification.Source.IsNone ||
				                  await permissionService.CanInteract(member, notification.Source.Known(),
					                  IPermissionService.InteractType.Hear);

				if (!isGagged && wantsToHear)
				{
					// Apply individual @chatformat unless MOGRIFY`OVERRIDE was set
					var finalMessage = message;
					if (!skipChatFormat)
					{
						// TODO: Apply individual player's @chatformat here
						// For now, just use the mogrified message
					}
					
					await notifyService.Notify(member, finalMessage, notification.Source.Known, notification.MessageType);
				}
			}

			if (!skipBuffer)
			{
				logger.LogInformation("{ChannelMessage}", MModule.serialize(message));
				// TODO: Add to channel recall buffer
			}
		}
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
			return MModule.empty();
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
			"@" => MModule.multiple([chanName, MModule.single(" "), message]),
			":" => MModule.multiple([chanName, MModule.single(" "), MModule.getLength(title) > 0 ? MModule.multiple([title, MModule.single(" ")]) : MModule.empty(), playerName, MModule.single(" "), message]),
			";" => MModule.multiple([chanName, MModule.single(" "), MModule.getLength(title) > 0 ? MModule.multiple([title, MModule.single(" ")]) : MModule.empty(), playerName, message]),
			_ => MModule.multiple([chanName, MModule.single(" "), MModule.getLength(title) > 0 ? MModule.multiple([title, MModule.single(" ")]) : MModule.empty(), playerName, MModule.single(" "), says, MModule.single(", \""), message, MModule.single("\"")])
		};
	}
}