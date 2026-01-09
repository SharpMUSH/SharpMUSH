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
/// <remarks>
/// TODO: Complete implementation
/// - Add @listen attribute processing (requires IAttributeService integration)
/// - Add queue integration for triggered ^-listen actions
/// - Add @prefix attribute support for puppets
/// </remarks>
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
		var actualSender = sender ?? location;
		
		// Get all objects in the location
		await foreach (var obj in mediator.CreateStream(new GetContentsQuery(location.Object().DBRef)))
		{
			var objAsObject = obj.WithRoomOption();
			
			// Skip excluded objects
			if (context.ExcludedObjects.Contains(objAsObject.Object().DBRef))
				continue;
			
			// Skip if can't interact
			if (!await permissionService.CanInteract(objAsObject, actualSender, IPermissionService.InteractType.Hear))
				continue;
			
			// Process ^-listen patterns (if MONITOR flag)
			await ProcessListenPatternsAsync(objAsObject, messageText, actualSender);
			
			// Process puppet relaying (if PUPPET flag)
			await ProcessPuppetRelayAsync(objAsObject, message, actualSender, type);
		}
	}
	
	private async ValueTask ProcessListenPatternsAsync(
		AnySharpObject listener,
		string message,
		AnySharpObject speaker)
	{
		// Check if object has MONITOR flag
		var hasMonitor = await listener.Object().Flags.Value.AnyAsync(f => f.Name == "MONITOR");
		if (!hasMonitor)
			return;
		
		// Check locks: Must pass BOTH @lock/use AND @lock/listen
		var passesUseLock = lockService.Evaluate(LockType.Use, listener, speaker);
		var passesListenLock = lockService.Evaluate(LockType.Listen, listener, speaker);
		
		if (!passesUseLock || !passesListenLock)
			return;
		
		// Match against ^-listen patterns
		var matches = await patternMatcher.MatchListenPatternsAsync(listener, message, speaker);
		
		// TODO: Queue matched patterns for execution with captured groups
		// This would integrate with the existing queue system
		// For now, patterns are matched but not executed
		foreach (var match in matches)
		{
			// Future: Queue attribute execution with registers set from captured groups
			// Example: Set %0-%9 from match.CapturedGroups, %# = speaker, %! = listener
		}
	}
	
	private async ValueTask ProcessPuppetRelayAsync(
		AnySharpObject puppet,
		OneOf<MString, string> message,
		AnySharpObject speaker,
		NotificationType type)
	{
		// Check if object has PUPPET flag
		var hasPuppet = await puppet.Object().Flags.Value.AnyAsync(f => f.Name == "PUPPET");
		if (!hasPuppet)
			return;
		
		// Get owner
		var owner = await puppet.Object().Owner.WithCancellation(CancellationToken.None);
		
		// Check if owner is connected
		var connections = connectionService.Get(owner.Object.DBRef);
		var isConnected = await connections.AnyAsync();
		if (!isConnected)
			return;
		
		// Check if puppet and owner are in same location (unless VERBOSE)
		var hasVerbose = await puppet.Object().Flags.Value.AnyAsync(f => f.Name == "VERBOSE");
		if (!hasVerbose)
		{
			// Get puppet's location  
			var puppetLocation = await puppet.Match<ValueTask<AnySharpContainer?>>(
				async player => await player.Location.WithCancellation(CancellationToken.None),
				room => ValueTask.FromResult<AnySharpContainer?>(room),
				async exit => await exit.Location.WithCancellation(CancellationToken.None),
				async thing => await thing.Location.WithCancellation(CancellationToken.None)
			);
			
			// Get owner's location
			var ownerLocation = await owner.Location.WithCancellation(CancellationToken.None);
			
			// Don't relay if in same room
			if (puppetLocation?.Object().DBRef == ownerLocation.Object().DBRef)
				return;
		}
		
		// Get prefix (default to puppet name)
		// TODO: Use @prefix attribute when IAttributeService integration is complete
		var prefix = $"[{puppet.Object().Name}] ";
		
		// Relay message to owner with prefix
		var relayedText = message.Match(
			markupString => prefix + markupString.ToString(),
			str => prefix + str
		);
		
		var bytes = System.Text.Encoding.UTF8.GetBytes(relayedText);
		
		// Send directly to owner's connections
		await foreach (var conn in connectionService.Get(owner.Object.DBRef))
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
