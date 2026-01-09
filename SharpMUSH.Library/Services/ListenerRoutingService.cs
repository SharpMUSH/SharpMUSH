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
/// TODO: Complete implementation with proper API usage
/// - Fix flag checking (Flags is Lazy&lt;IAsyncEnumerable&gt;)
/// - Use IAttributeService for getting @listen attribute
/// - Complete lock evaluation using ILockService.Evaluate
/// - Add queue integration for triggered actions
/// - Complete puppet relaying with proper location checking
/// </remarks>
#pragma warning disable CS9113 // Parameter is unread
public class ListenerRoutingService(
	IMediator mediator,
	IListenPatternMatcher patternMatcher,
	IPermissionService permissionService,
	ILockService lockService,
	IConnectionService connectionService,
	IBus publishEndpoint) : IListenerRoutingService
#pragma warning restore CS9113
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
		
		// TODO: Implement listener discovery and processing
		// Steps:
		// 1. Get location object
		// 2. Iterate through contents
		// 3. Check interaction permissions
		// 4. Process ^-listen patterns (if MONITOR flag)
		// 5. Process @listen attribute
		// 6. Handle puppet relaying (if PUPPET flag)
		
		await ValueTask.CompletedTask;
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
