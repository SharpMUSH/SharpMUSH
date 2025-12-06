using Mediator;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class MoveService(
	IMediator mediator,
	IAttributeService attributeService,
	IPermissionService permissionService,
	INotifyService notifyService) : IMoveService
{
	/// <summary>
	/// Standard attribute names for move hooks
	/// </summary>
	private static class MoveAttributes
	{
		// Enter hooks - triggered when entering a new location
		public const string Enter = "ENTER";       // Seen by the object entering
		public const string OEnter = "OENTER";     // Seen by others in the destination
		public const string OXEnter = "OXENTER";   // Seen by the object entering (from others' perspective)
		
		// Leave hooks - triggered when leaving a location
		public const string Leave = "LEAVE";       // Seen by the object leaving
		public const string OLeave = "OLEAVE";     // Seen by others in the old location
		public const string OXLeave = "OXLEAVE";   // Seen by the object leaving (from others' perspective)
		
		// Teleport hooks - triggered on teleportation specifically
		public const string OTeleport = "OTELEPORT";   // Seen by others in destination
		public const string OXTeleport = "OXTELEPORT"; // Seen by the teleported object
	}

	/// <summary>
	/// Checks if moving an object to a destination would create a containment loop.
	/// This prevents scenarios like: A contains B, B contains C, then moving A into C would create a loop.
	/// </summary>
	public async ValueTask<bool> WouldCreateLoop(AnySharpContent objectToMove, AnySharpContainer destination)
	{
		// If we're not moving into a container, no loop is possible
		if (!destination.IsThing && !destination.IsPlayer)
		{
			return false;
		}

		// Get the object's DBRef for comparison
		var objectDBRef = objectToMove.Object().DBRef;
		
		// Traverse up the containment chain from the destination
		// If we find the object we're trying to move, it would create a loop
		var current = destination;
		var visited = new HashSet<string> { current.Object().DBRef.ToString() };
		
		while (true)
		{
			// Check if the current container is the object we're trying to move
			if (current.Object().DBRef.Equals(objectDBRef))
			{
				return true; // Found a loop
			}
			
			// Get the location of the current container
			var location = await current.Match<ValueTask<AnySharpContainer>>(
				async player => await player.Location.WithCancellation(CancellationToken.None),
				room => ValueTask.FromResult<AnySharpContainer>(room),
				async thing => await thing.Location.WithCancellation(CancellationToken.None)
			);
			
			// If we've reached a room (rooms don't have locations other than themselves)
			// or if we've already visited this location (another way to detect loops),
			// we're done and no loop exists
			if (location.IsRoom || visited.Contains(location.Object().DBRef.ToString()))
			{
				return false;
			}
			
			// Continue traversing up the chain
			visited.Add(location.Object().DBRef.ToString());
			current = location;
		}
	}
	
	/// <summary>
	/// Executes a complete move operation including permission checks, cost calculation,
	/// hook triggering, and notifications.
	/// </summary>
	public async ValueTask<OneOf<Success, Error<string>>> ExecuteMoveAsync(
		IMUSHCodeParser parser,
		AnySharpContent objectToMove,
		AnySharpContainer destination,
		DBRef? enactor = null,
		string cause = "move",
		bool silent = false)
	{
		var targetObj = objectToMove.Object();
		var destObj = destination.Object();
		var enactorRef = enactor ?? targetObj.DBRef;
		
		// Get enactor object for permission checks
		var enactorQuery = await mediator.Send(new GetObjectNodeQuery(enactorRef));
		if (enactorQuery.IsNone)
		{
			return new Error<string>("Invalid enactor");
		}
		var enactorObj = enactorQuery.Known;
		
		// 1. Check for containment loops
		if (await WouldCreateLoop(objectToMove, destination))
		{
			return new Error<string>("Cannot move - it would create a containment loop.");
		}
		
		// 2. Check permissions
		if (!await CanMoveAsync(enactorObj, objectToMove, destination))
		{
			return new Error<string>("Permission denied.");
		}
		
		// 3. Calculate and check move cost
		// TODO: Implement quota checking and deduction when quota system is available
		
		// 4. Get old location for hooks
		var oldLocation = await objectToMove.Match<ValueTask<DBRef>>(
			async player =>
			{
				var location = await player.Location.WithCancellation(CancellationToken.None);
				return location.Object().DBRef;
			},
			async exit =>
			{
				var location = await exit.Location.WithCancellation(CancellationToken.None);
				return location.Object().DBRef;
			},
			async thing =>
			{
				var location = await thing.Location.WithCancellation(CancellationToken.None);
				return location.Object().DBRef;
			});
		
		// 5. Trigger LEAVE hooks on old location (if not silent)
		if (!silent && oldLocation != destObj.DBRef)
		{
			var oldLocQuery = await mediator.Send(new GetObjectNodeQuery(oldLocation));
			if (!oldLocQuery.IsNone)
			{
				var oldLocObj = oldLocQuery.Known;
				await TriggerLeaveHooksAsync(parser, objectToMove, oldLocObj, enactorRef, cause);
			}
		}
		
		// 6. Execute the actual database move
		await mediator.Send(new MoveObjectCommand(
			objectToMove,
			destination,
			enactor,
			silent,
			cause));
		
		// 7. Trigger ENTER hooks on new location (if not silent)
		if (!silent)
		{
			var destObjAny = destination.Match<AnySharpObject>(
				player => player,
				room => room,
				thing => thing);
			await TriggerEnterHooksAsync(parser, objectToMove, destObjAny, enactorRef, cause);
		}
		
		// 8. Trigger teleport hooks if this is a teleport
		if (!silent && cause.Equals("teleport", StringComparison.OrdinalIgnoreCase))
		{
			var destObjAny = destination.Match<AnySharpObject>(
				player => player,
				room => room,
				thing => thing);
			await TriggerTeleportHooksAsync(parser, objectToMove, destObjAny, enactorRef);
		}
		
		// 9. Notify contents if the moved object is a container
		if (!silent && (objectToMove.IsPlayer || objectToMove.IsThing))
		{
			await NotifyContentsOfMoveAsync(parser, objectToMove, oldLocation, destObj.DBRef);
		}
		
		return new Success();
	}
	
	/// <summary>
	/// Checks if a move is permitted based on locks and permissions.
	/// </summary>
	public async ValueTask<bool> CanMoveAsync(
		AnySharpObject who,
		AnySharpContent objectToMove,
		AnySharpContainer destination)
	{
		// Convert to AnySharpObject for permission checks
		var target = objectToMove.Match<AnySharpObject>(
			player => player,
			exit => exit,
			thing => thing);
			
		var dest = destination.Match<AnySharpObject>(
			player => player,
			room => room,
			thing => thing);
		
		// Check if the enactor controls the object being moved
		if (!await permissionService.Controls(who, target))
		{
			return false;
		}
		
		// Check ENTER lock on the destination
		if (!permissionService.PassesLock(who, dest, LockType.Enter))
		{
			return false;
		}
		
		// If moving from a location, check LEAVE lock on current location
		var currentLocation = await objectToMove.Match<ValueTask<DBRef?>>(
			async player =>
			{
				var location = await player.Location.WithCancellation(CancellationToken.None);
				return (DBRef?)location.Object().DBRef;
			},
			async exit =>
			{
				var location = await exit.Location.WithCancellation(CancellationToken.None);
				return (DBRef?)location.Object().DBRef;
			},
			async thing =>
			{
				var location = await thing.Location.WithCancellation(CancellationToken.None);
				return (DBRef?)location.Object().DBRef;
			});
		
		if (currentLocation.HasValue)
		{
			var locQuery = await mediator.Send(new GetObjectNodeQuery(currentLocation.Value));
			if (!locQuery.IsNone)
			{
				var locObj = locQuery.Known;
				if (!permissionService.PassesLock(who, locObj, LockType.Leave))
				{
					return false;
				}
			}
		}
		
		return true;
	}
	
	/// <summary>
	/// Calculates the cost of moving an object.
	/// </summary>
	public ValueTask<int> CalculateMoveCostAsync(
		AnySharpContent objectToMove,
		AnySharpContainer destination)
	{
		// In PennMUSH, basic moves are typically free
		// Cost might apply for teleporting or special circumstances
		// For now, return 0 - this can be extended later
		return ValueTask.FromResult(0);
	}
	
	/// <summary>
	/// Triggers LEAVE-related hooks when an object leaves a location.
	/// </summary>
	private async ValueTask TriggerLeaveHooksAsync(
		IMUSHCodeParser parser,
		AnySharpContent objectToMove,
		AnySharpObject oldLocation,
		DBRef enactor,
		string cause)
	{
		var targetObj = objectToMove.Match<AnySharpObject>(
			player => player,
			exit => exit,
			thing => thing);
		var targetDBRef = objectToMove.Object().DBRef;
		
		// @LEAVE - message seen by the object leaving
		var leaveAttr = await attributeService.GetAttributeAsync(
			targetObj, oldLocation, MoveAttributes.Leave,
			IAttributeService.AttributeMode.Execute, parent: false);
		
		if (leaveAttr.IsAttribute)
		{
			await attributeService.EvaluateAttributeFunctionAsync(
				parser, targetObj, oldLocation, MoveAttributes.Leave,
				new Dictionary<string, CallState>
				{
					["0"] = new CallState(targetDBRef.ToString()),
					["1"] = new CallState(cause)
				},
				evalParent: false);
		}
		
		// @OLEAVE - message seen by others in the old location
		var oleaveAttr = await attributeService.GetAttributeAsync(
			targetObj, oldLocation, MoveAttributes.OLeave,
			IAttributeService.AttributeMode.Execute, parent: false);
		
		if (oleaveAttr.IsAttribute)
		{
			// Get contents of old location to notify
			var contents = new List<AnySharpContent>();
			await foreach (var content in mediator.CreateStream(new GetContentsQuery(oldLocation.Object().DBRef)))
			{
				if (!content.Object().DBRef.Equals(targetDBRef))
				{
					contents.Add(content);
				}
			}
			
			foreach (var content in contents)
			{
				var contentObj = content.Match<AnySharpObject>(
					player => player,
					exit => exit,
					thing => thing);
					
				var message = await attributeService.EvaluateAttributeFunctionAsync(
					parser, contentObj, oldLocation, MoveAttributes.OLeave,
					new Dictionary<string, CallState>
					{
						["0"] = new CallState(targetDBRef.ToString()),
						["1"] = new CallState(cause)
					},
					evalParent: false);
				
				if (!string.IsNullOrEmpty(message.ToPlainText()))
				{
					await notifyService.Notify(content.Object().DBRef, message);
				}
			}
		}
		
		// @OXLEAVE - message seen by the object leaving (from others' perspective)
		var oxleaveAttr = await attributeService.GetAttributeAsync(
			targetObj, oldLocation, MoveAttributes.OXLeave,
			IAttributeService.AttributeMode.Execute, parent: false);
		
		if (oxleaveAttr.IsAttribute)
		{
			var message = await attributeService.EvaluateAttributeFunctionAsync(
				parser, targetObj, oldLocation, MoveAttributes.OXLeave,
				new Dictionary<string, CallState>
				{
					["0"] = new CallState(targetDBRef.ToString()),
					["1"] = new CallState(cause)
				},
				evalParent: false);
			
			if (!string.IsNullOrEmpty(message.ToPlainText()))
			{
				await notifyService.Notify(targetDBRef, message);
			}
		}
	}
	
	/// <summary>
	/// Triggers ENTER-related hooks when an object enters a location.
	/// </summary>
	private async ValueTask TriggerEnterHooksAsync(
		IMUSHCodeParser parser,
		AnySharpContent objectToMove,
		AnySharpObject newLocation,
		DBRef enactor,
		string cause)
	{
		var targetObj = objectToMove.Match<AnySharpObject>(
			player => player,
			exit => exit,
			thing => thing);
		var targetDBRef = objectToMove.Object().DBRef;
		
		// @ENTER - message seen by the object entering
		var enterAttr = await attributeService.GetAttributeAsync(
			targetObj, newLocation, MoveAttributes.Enter,
			IAttributeService.AttributeMode.Execute, parent: false);
		
		if (enterAttr.IsAttribute)
		{
			await attributeService.EvaluateAttributeFunctionAsync(
				parser, targetObj, newLocation, MoveAttributes.Enter,
				new Dictionary<string, CallState>
				{
					["0"] = new CallState(targetDBRef.ToString()),
					["1"] = new CallState(cause)
				},
				evalParent: false);
		}
		
		// @OENTER - message seen by others in the new location
		var oenterAttr = await attributeService.GetAttributeAsync(
			targetObj, newLocation, MoveAttributes.OEnter,
			IAttributeService.AttributeMode.Execute, parent: false);
		
		if (oenterAttr.IsAttribute)
		{
			// Get contents of new location to notify
			var contents = new List<AnySharpContent>();
			await foreach (var content in mediator.CreateStream(new GetContentsQuery(newLocation.Object().DBRef)))
			{
				if (!content.Object().DBRef.Equals(targetDBRef))
				{
					contents.Add(content);
				}
			}
			
			foreach (var content in contents)
			{
				var contentObj = content.Match<AnySharpObject>(
					player => player,
					exit => exit,
					thing => thing);
					
				var message = await attributeService.EvaluateAttributeFunctionAsync(
					parser, contentObj, newLocation, MoveAttributes.OEnter,
					new Dictionary<string, CallState>
					{
						["0"] = new CallState(targetDBRef.ToString()),
						["1"] = new CallState(cause)
					},
					evalParent: false);
				
				if (!string.IsNullOrEmpty(message.ToPlainText()))
				{
					await notifyService.Notify(content.Object().DBRef, message);
				}
			}
		}
		
		// @OXENTER - message seen by the object entering (from others' perspective)
		var oxenterAttr = await attributeService.GetAttributeAsync(
			targetObj, newLocation, MoveAttributes.OXEnter,
			IAttributeService.AttributeMode.Execute, parent: false);
		
		if (oxenterAttr.IsAttribute)
		{
			var message = await attributeService.EvaluateAttributeFunctionAsync(
				parser, targetObj, newLocation, MoveAttributes.OXEnter,
				new Dictionary<string, CallState>
				{
					["0"] = new CallState(targetDBRef.ToString()),
					["1"] = new CallState(cause)
				},
				evalParent: false);
			
			if (!string.IsNullOrEmpty(message.ToPlainText()))
			{
				await notifyService.Notify(targetDBRef, message);
			}
		}
	}
	
	/// <summary>
	/// Triggers TELEPORT-related hooks when an object is teleported.
	/// </summary>
	private async ValueTask TriggerTeleportHooksAsync(
		IMUSHCodeParser parser,
		AnySharpContent objectToMove,
		AnySharpObject newLocation,
		DBRef enactor)
	{
		var targetObj = objectToMove.Match<AnySharpObject>(
			player => player,
			exit => exit,
			thing => thing);
		var targetDBRef = objectToMove.Object().DBRef;
		
		// @OTELEPORT - message seen by others in destination
		var oteleportAttr = await attributeService.GetAttributeAsync(
			targetObj, newLocation, MoveAttributes.OTeleport,
			IAttributeService.AttributeMode.Execute, parent: false);
		
		if (oteleportAttr.IsAttribute)
		{
			var contents = new List<AnySharpContent>();
			await foreach (var content in mediator.CreateStream(new GetContentsQuery(newLocation.Object().DBRef)))
			{
				if (!content.Object().DBRef.Equals(targetDBRef))
				{
					contents.Add(content);
				}
			}
			
			foreach (var content in contents)
			{
				var contentObj = content.Match<AnySharpObject>(
					player => player,
					exit => exit,
					thing => thing);
					
				var message = await attributeService.EvaluateAttributeFunctionAsync(
					parser, contentObj, newLocation, MoveAttributes.OTeleport,
					new Dictionary<string, CallState>
					{
						["0"] = new CallState(targetDBRef.ToString()),
						["1"] = new CallState(enactor.ToString())
					},
					evalParent: false);
				
				if (!string.IsNullOrEmpty(message.ToPlainText()))
				{
					await notifyService.Notify(content.Object().DBRef, message);
				}
			}
		}
		
		// @OXTELEPORT - message seen by the teleported object
		var oxteleportAttr = await attributeService.GetAttributeAsync(
			targetObj, newLocation, MoveAttributes.OXTeleport,
			IAttributeService.AttributeMode.Execute, parent: false);
		
		if (oxteleportAttr.IsAttribute)
		{
			var message = await attributeService.EvaluateAttributeFunctionAsync(
				parser, targetObj, newLocation, MoveAttributes.OXTeleport,
				new Dictionary<string, CallState>
				{
					["0"] = new CallState(targetDBRef.ToString()),
					["1"] = new CallState(enactor.ToString())
				},
				evalParent: false);
			
			if (!string.IsNullOrEmpty(message.ToPlainText()))
			{
				await notifyService.Notify(targetDBRef, message);
			}
		}
	}
	
	/// <summary>
	/// Notifies contents of a container when the container moves.
	/// </summary>
	private async ValueTask NotifyContentsOfMoveAsync(
		IMUSHCodeParser parser,
		AnySharpContent container,
		DBRef oldLocation,
		DBRef newLocation)
	{
		// Get contents of the container
		var contents = new List<AnySharpContent>();
		await foreach (var content in mediator.CreateStream(new GetContentsQuery(container.Object().DBRef)))
		{
			contents.Add(content);
		}
		
		if (contents.Count == 0)
		{
			return;
		}
		
		// Get location names for notification
		var oldLocQuery = await mediator.Send(new GetObjectNodeQuery(oldLocation));
		var newLocQuery = await mediator.Send(new GetObjectNodeQuery(newLocation));
		
		if (!oldLocQuery.IsNone && !newLocQuery.IsNone)
		{
			var oldLocName = oldLocQuery.Known.Object().Name;
			var newLocName = newLocQuery.Known.Object().Name;
			
			foreach (var content in contents)
			{
				// Notify each content that they have moved
				// This is typically used for players inside vehicles or containers
				await notifyService.Notify(
					content.Object().DBRef,
					$"You sense that you have moved from {oldLocName} to {newLocName}.");
			}
		}
	}
}
