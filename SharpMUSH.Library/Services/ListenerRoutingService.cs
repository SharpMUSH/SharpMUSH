using Mediator;
using MassTransit;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;
using static SharpMUSH.Library.Services.Interfaces.INotifyService;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Service for routing notifications to listening objects.
/// Handles @listen attributes, ^-listen patterns, and puppet relaying.
/// </summary>
public class ListenerRoutingService(
	IMediator mediator,
	IListenPatternMatcher patternMatcher,
	IPermissionService permissionService,
	ILockService lockService,
	IConnectionService connectionService,
	IBus publishEndpoint) : IListenerRoutingService
{
	public async ValueTask ProcessNotificationAsync(
		NotificationContext context,
		OneOf<MString, string> message,
		AnySharpObject? sender,
		NotificationType type)
	{
		// Only process certain notification types
		if (!ShouldProcessListeners(type))
			return;
		
		// If no location, we can't find listeners
		if (context.Location is null)
			return;
		
		var messageText = message.Match(
			markupString => markupString.ToString(),
			str => str
		);
		
		// Get the location object
		var locationResult = await mediator.Send(new GetObjectNodeQuery(context.Location.Value));
		if (locationResult.IsNone())
			return;
		
		var location = locationResult.WithoutNone();
		var actualSender = sender ?? locationResult.WithoutNone().WithObjectOption();
		
		// Get all objects in the location
		await foreach (var obj in location.WithRoomOption().Content(mediator))
		{
			// Skip excluded objects
			if (context.ExcludedObjects.Contains(obj.Object().DBRef))
				continue;
			
			// Skip if can't interact
			if (!await permissionService.CanInteract(obj, actualSender, IPermissionService.InteractType.Hear))
				continue;
			
			// Process listen patterns (^-listen)
			await ProcessListenPatternsAsync(obj, messageText, actualSender);
			
			// Process @listen attribute
			await ProcessListenAttributeAsync(obj, messageText, actualSender);
			
			// Process puppet relaying
			await ProcessPuppetRelayAsync(obj, message, actualSender, type);
		}
	}
	
	private async ValueTask ProcessListenPatternsAsync(
		AnySharpObject listener,
		string message,
		AnySharpObject speaker)
	{
		// Check if object has MONITOR flag
		var flags = listener.Object().Flags;
		if (!flags.Any(f => f.Name == "MONITOR"))
			return;
		
		// Check locks: Must pass BOTH @lock/use AND @lock/listen
		var passesUseLock = await lockService.CheckLock(speaker, listener, LockType.Use);
		var passesListenLock = await lockService.CheckLock(speaker, listener, LockType.Listen);
		
		if (!passesUseLock || !passesListenLock)
			return;
		
		// Match against ^-listen patterns
		var matches = await patternMatcher.MatchListenPatternsAsync(listener, message, speaker);
		
		// TODO: Queue matched patterns for execution with captured groups
		foreach (var match in matches)
		{
			// Queue the attribute for execution
			// This would integrate with the existing queue system
			// For now, just a placeholder
		}
	}
	
	private async ValueTask ProcessListenAttributeAsync(
		AnySharpObject listener,
		string message,
		AnySharpObject speaker)
	{
		// Get @listen attribute
		var listenAttr = listener.Object().GetAttribute("LISTEN");
		if (listenAttr is null || string.IsNullOrWhiteSpace(listenAttr.Value.ToPlainText()))
			return;
		
		// Check @lock/listen
		var passesListenLock = await lockService.CheckLock(speaker, listener, LockType.Listen);
		if (!passesListenLock)
			return;
		
		// Match message against pattern (simple wildcard matching)
		var pattern = listenAttr.Value.ToPlainText();
		// TODO: Implement wildcard matching
		
		// Determine which action attribute to trigger
		var isSelf = listener.Object().DBRef == speaker.Object().DBRef;
		string actionAttrName;
		
		// Check for AAHEAR first (anyone)
		var aahearAttr = listener.Object().GetAttribute("AAHEAR");
		if (aahearAttr is not null)
		{
			actionAttrName = "AAHEAR";
		}
		else if (isSelf)
		{
			// Self speaking - use AMHEAR
			var amhearAttr = listener.Object().GetAttribute("AMHEAR");
			actionAttrName = amhearAttr is not null ? "AMHEAR" : "AHEAR";
		}
		else
		{
			// Others speaking - use AHEAR
			actionAttrName = "AHEAR";
		}
		
		// TODO: Queue the action attribute for execution
	}
	
	private async ValueTask ProcessPuppetRelayAsync(
		AnySharpObject puppet,
		OneOf<MString, string> message,
		AnySharpObject speaker,
		NotificationType type)
	{
		// Check if object has PUPPET flag
		var flags = puppet.Object().Flags;
		if (!flags.Any(f => f.Name == "PUPPET"))
			return;
		
		// Get owner
		var ownerDbRef = puppet.Object().Owner;
		if (ownerDbRef is null)
			return;
		
		var ownerResult = await mediator.Send(new GetObjectNodeQuery(ownerDbRef.Value));
		if (ownerResult.IsNone())
			return;
		
		var owner = ownerResult.WithoutNone();
		
		// Check if owner is connected
		var connections = connectionService.Get(ownerDbRef.Value);
		var isConnected = await connections.AnyAsync();
		if (!isConnected)
			return;
		
		// Check if puppet and owner are in same location (unless VERBOSE)
		var hasVerbose = flags.Any(f => f.Name == "VERBOSE");
		if (!hasVerbose)
		{
			var puppetLocation = puppet.Object().Location;
			var ownerLocation = owner.WithPlayerOption().Location;
			
			if (puppetLocation == ownerLocation)
				return; // Don't relay in same room
		}
		
		// Get prefix (use @prefix attribute or default to puppet name)
		var prefixAttr = puppet.Object().GetAttribute("PREFIX");
		var prefix = prefixAttr?.Value.ToPlainText() ?? $"[{puppet.Object().Name}] ";
		
		// Relay message to owner with prefix
		var relayedText = message.Match(
			markupString => prefix + markupString.ToString(),
			str => prefix + str
		);
		
		var bytes = System.Text.Encoding.UTF8.GetBytes(relayedText);
		
		// Send directly to owner's connections
		await foreach (var conn in connectionService.Get(ownerDbRef.Value))
		{
			await publishEndpoint.Publish(new TelnetOutputMessage(conn.Handle, bytes));
		}
	}
	
	private static bool ShouldProcessListeners(NotificationType type)
	{
		// Only process for communication types, not private messages
		return type switch
		{
			NotificationType.Say => true,
			NotificationType.Pose => true,
			NotificationType.SemiPose => true,
			NotificationType.Emit => true,
			NotificationType.NSEmit => true,
			NotificationType.NSSay => true,
			NotificationType.NSPose => true,
			NotificationType.NSSemiPose => true,
			NotificationType.Announce => false, // Private messages don't trigger listeners
			NotificationType.NSAnnounce => false,
			_ => false
		};
	}
}
