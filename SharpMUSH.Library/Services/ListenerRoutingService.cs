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
/// Phase 3 implementation status:
/// - ✅ @prefix attribute support for puppets
/// - ⏳ @listen attribute matching (needs parser integration)
/// - ⏳ Action queue for ^-listen patterns (needs parser integration)
/// 
/// Note: Full attribute execution requires IMUSHCodeParser which is not available
/// in the notification context. This will be addressed in a future phase when
/// we can create parsers on-demand or integrate with the command queue system.
/// </remarks>
public class ListenerRoutingService(
	IMediator mediator,
	IListenPatternMatcher patternMatcher,
	IPermissionService permissionService,
	ILockService lockService,
	IConnectionService connectionService,
	IAttributeService attributeService,
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
			
			// Process @listen attribute
			await ProcessListenAttributeAsync(objAsObject, messageText, actualSender);
			
			// Process puppet relaying (if PUPPET flag)
			await ProcessPuppetRelayAsync(objAsObject, message, actualSender, type);
		}
	}
	
	private async ValueTask ProcessListenAttributeAsync(
		AnySharpObject listener,
		string message,
		AnySharpObject speaker)
	{
		// Get @listen attribute
		var listenAttr = await attributeService.GetAttributeAsync(
			listener, listener, "LISTEN",
			IAttributeService.AttributeMode.Read,
			parent: false);
		
		if (!listenAttr.IsAttribute)
			return;
		
		var listenPattern = listenAttr.AsAttribute.Last().Value.ToPlainText();
		if (string.IsNullOrWhiteSpace(listenPattern))
			return;
		
		// Check @lock/listen
		var passesListenLock = lockService.Evaluate(LockType.Listen, listener, speaker);
		if (!passesListenLock)
			return;
		
		// Match message against pattern using wildcard matching
		var regex = new System.Text.RegularExpressions.Regex(
			MModule.getWildcardMatchAsRegex(MModule.single(listenPattern)),
			System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		
		if (!regex.IsMatch(message))
			return;
		
		// Determine which action attribute would be triggered
		// Note: Actual execution requires a parser, which we don't have in this context
		// For now, we document which attribute would be triggered
		var isSelf = listener.Object().DBRef == speaker.Object().DBRef;
		
		// Priority: AAHEAR > (AMHEAR if self) > AHEAR
		// These would be executed if we had a parser available
#pragma warning disable CS0219 // Variable assigned but not used - documented for future parser integration
		string triggerAttrName;
#pragma warning restore CS0219
		if (await AttributeExistsAsync(listener, "AAHEAR"))
		{
			triggerAttrName = "AAHEAR";
		}
		else if (isSelf && await AttributeExistsAsync(listener, "AMHEAR"))
		{
			triggerAttrName = "AMHEAR";
		}
		else
		{
			triggerAttrName = "AHEAR";
		}
		
		// TODO: Queue attribute execution when parser integration is available
		// For now, @listen patterns are matched but actions are not executed
		// This requires integration with the command queue or parser factory
	}
	
	private async ValueTask<bool> AttributeExistsAsync(AnySharpObject obj, string attributeName)
	{
		var result = await attributeService.GetAttributeAsync(
			obj, obj, attributeName,
			IAttributeService.AttributeMode.Read,
			parent: false);
		return result.IsAttribute;
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
		
		// Get prefix from @prefix attribute or use default
		var prefixAttr = await attributeService.GetAttributeAsync(
			puppet, puppet, "PREFIX",
			IAttributeService.AttributeMode.Read,
			parent: false);
		
		var prefix = prefixAttr.IsAttribute
			? prefixAttr.AsAttribute.Last().Value.ToPlainText()
			: $"[{puppet.Object().Name}] ";
		
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
