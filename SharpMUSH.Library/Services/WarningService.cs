using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Service for checking topology and integrity warnings on MUSH objects
/// </summary>
public class WarningService(
	INotifyService notifyService,
	IAttributeService attributeService,
	IMediator mediator) : IWarningService
{
	/// <summary>
	/// Check warnings on a specific object
	/// </summary>
	public async Task<bool> CheckObjectAsync(AnySharpObject checker, AnySharpObject target)
	{
		var targetObj = target.Object();
		
		// TODO: Skip GOING objects
		// TODO: Skip NO_WARN objects  
		// TODO: Skip if owner has NO_WARN

		// Determine which warnings to check
		var owner = await targetObj.Owner.WithCancellation(CancellationToken.None);
		var warnings = await GetWarningsForCheck(checker, targetObj, owner.Object);
		
		if (warnings == WarningType.None)
		{
			return false;
		}

		// Perform the checks
		var hasWarnings = false;

		// Generic checks for all objects
		hasWarnings |= await CheckGenericWarnings(checker, target, warnings);

		// Type-specific checks
		hasWarnings |= targetObj.Type switch
		{
			"ROOM" => await CheckRoomWarnings(checker, target, warnings),
			"EXIT" => await CheckExitWarnings(checker, target, warnings),
			"THING" => await CheckThingWarnings(checker, target, warnings),
			"PLAYER" => await CheckPlayerWarnings(checker, target, warnings),
			_ => false
		};

		return hasWarnings;
	}

	/// <summary>
	/// Check warnings on all objects owned by a player
	/// </summary>
	public async Task<int> CheckOwnedObjectsAsync(AnySharpObject owner)
	{
		var warningCount = 0;
		var ownerObj = owner.Object();
		
		// Get all objects in the database and filter by owner
		var allObjects = mediator.CreateStream(new GetAllObjectsQuery());
		
		await foreach (var obj in allObjects)
		{
			// Check if this object is owned by the player
			var objectOwner = await obj.Owner.WithCancellation(CancellationToken.None);
			if (!objectOwner.Object.DBRef.Equals(ownerObj.DBRef))
			{
				continue;
			}
			
			// Get the full object node to get AnySharpObject
			var objectNode = await mediator.Send(new GetObjectNodeQuery(obj.DBRef));
			if (objectNode.IsNone)
			{
				continue;
			}
			
			// Check warnings on this object
			var hadWarnings = await CheckObjectAsync(owner, objectNode.WithoutNone());
			if (hadWarnings)
			{
				warningCount++;
			}
		}
		
		await notifyService.Notify(owner, $"@wcheck complete. Found {warningCount} warnings on your objects.");
		return warningCount;
	}

	/// <summary>
	/// Check warnings on all objects in the database
	/// Notifies connected owners of warnings found
	/// </summary>
	public async Task<int> CheckAllObjectsAsync()
	{
		var checkedCount = 0;
		var warningsByOwner = new Dictionary<string, List<string>>();
		
		// Get all objects in the database
		var allObjects = mediator.CreateStream(new GetAllObjectsQuery());
		
		await foreach (var obj in allObjects)
		{
			checkedCount++;
			
			// Get the owner for this object
			var owner = await obj.Owner.WithCancellation(CancellationToken.None);
			var ownerId = owner.Object.Id ?? owner.Object.Key.ToString();
			
			// Track warnings per owner
			if (!warningsByOwner.ContainsKey(ownerId))
			{
				warningsByOwner[ownerId] = [];
			}
			
			// Get the full object nodes
			var ownerNode = await mediator.Send(new GetObjectNodeQuery(owner.Object.DBRef));
			var objectNode = await mediator.Send(new GetObjectNodeQuery(obj.DBRef));
			
			if (ownerNode.IsNone || objectNode.IsNone)
			{
				continue;
			}
			
			// Check warnings (using owner as checker for their own objects)
			var hadWarnings = await CheckObjectAsync(ownerNode.WithoutNone(), objectNode.WithoutNone());
			
			if (hadWarnings)
			{
				warningsByOwner[ownerId].Add($"{obj.Name}(#{obj.Key})");
			}
		}
		
		// Notify connected owners of their warnings
		// TODO: Check if owners are connected before notifying
		// For now, we just return the count
		
		return checkedCount;
	}

	/// <summary>
	/// Determine which warnings to use for a check
	/// </summary>
	private static async Task<WarningType> GetWarningsForCheck(AnySharpObject checker, SharpObject target, SharpObject owner)
	{
		var checkerObj = checker.Object();
		var checkerOwner = await checkerObj.Owner.WithCancellation(CancellationToken.None);

		// If the checker's owner is the target's owner, use target warnings (fallback to owner)
		if (checkerOwner.Object.DBRef.Equals(owner.DBRef))
		{
			return target.Warnings != WarningType.None ? target.Warnings : owner.Warnings;
		}

		// Otherwise (admin checking), use checker's warnings
		return checkerObj.Warnings != WarningType.None ? checkerObj.Warnings : checkerOwner.Object.Warnings;
	}

	/// <summary>
	/// Check generic warnings that apply to all objects
	/// </summary>
	private async Task<bool> CheckGenericWarnings(AnySharpObject checker, AnySharpObject target, WarningType warnings)
	{
		var hasWarnings = false;

		if (warnings.HasFlag(WarningType.LockProbs))
		{
			// TODO: Implement lock checking
			// This would check for:
			// - Invalid object references in locks
			// - References to GOING/garbage objects
			// - Missing attributes in eval locks
			// - Indirect locks that aren't present
		}

		return hasWarnings;
	}

	/// <summary>
	/// Check room-specific warnings
	/// </summary>
	private async Task<bool> CheckRoomWarnings(AnySharpObject checker, AnySharpObject target, WarningType warnings)
	{
		var hasWarnings = false;

		if (warnings.HasFlag(WarningType.RoomDesc))
		{
			var desc = await attributeService.GetAttributeAsync(checker, target, "DESCRIBE", IAttributeService.AttributeMode.Read, false);
			if (desc.IsT1)
			{
				await Complain(checker, target, "room-desc", "Room has no description.");
				hasWarnings = true;
			}
		}

		return hasWarnings;
	}

	/// <summary>
	/// Check exit-specific warnings
	/// </summary>
	private async Task<bool> CheckExitWarnings(AnySharpObject checker, AnySharpObject target, WarningType warnings)
	{
		var hasWarnings = false;

		// Check for unlinked exits
		if (warnings.HasFlag(WarningType.ExitUnlinked))
		{
			if (target.IsExit)
			{
				// For exits, check if the destination is valid
				// TODO: Need to determine how to check if an exit is unlinked
				// This may require checking if destination is set to a special "NOTHING" value
				// For now, we'll skip this check until we understand the data model better
			}
		}

		// Check for missing description
		if (warnings.HasFlag(WarningType.ExitDesc))
		{
			var desc = await attributeService.GetAttributeAsync(checker, target, "DESCRIBE", IAttributeService.AttributeMode.Read, false);
			if (desc.IsT1)
			{
				await Complain(checker, target, "exit-desc", "Exit has no description.");
				hasWarnings = true;
			}
		}

		// Check for missing messages
		if (warnings.HasFlag(WarningType.ExitMsgs))
		{
			// Check unlocked exit messages: SUCCESS, OSUCCESS, ODROP
			var success = await attributeService.GetAttributeAsync(checker, target, "SUCCESS", IAttributeService.AttributeMode.Read, false);
			var osuccess = await attributeService.GetAttributeAsync(checker, target, "OSUCCESS", IAttributeService.AttributeMode.Read, false);
			var odrop = await attributeService.GetAttributeAsync(checker, target, "ODROP", IAttributeService.AttributeMode.Read, false);
			
			if (success.IsT1 || osuccess.IsT1 || odrop.IsT1)
			{
				await Complain(checker, target, "exit-msgs", "Exit is missing messages (SUCCESS, OSUCCESS, or ODROP).");
				hasWarnings = true;
			}
			
			// Check locked exit messages: FAILURE
			var failure = await attributeService.GetAttributeAsync(checker, target, "FAILURE", IAttributeService.AttributeMode.Read, false);
			if (failure.IsT1)
			{
				await Complain(checker, target, "exit-msgs", "Exit is missing FAILURE message.");
				hasWarnings = true;
			}
		}

		// Check for one-way and multiple return exits
		// These require topology analysis which is more complex
		// TODO: Implement topology checks (exit-oneway, exit-multiple)

		return hasWarnings;
	}

	/// <summary>
	/// Check thing-specific warnings
	/// </summary>
	private async Task<bool> CheckThingWarnings(AnySharpObject checker, AnySharpObject target, WarningType warnings)
	{
		var hasWarnings = false;

		// Check for missing description
		if (warnings.HasFlag(WarningType.ThingDesc))
		{
			var desc = await attributeService.GetAttributeAsync(checker, target, "DESCRIBE", IAttributeService.AttributeMode.Read, false);
			if (desc.IsT1)
			{
				// Skip things in player inventory as per PennMUSH behavior
				if (target.IsThing)
				{
					var thing = target.AsThing;
					var location = await thing.Location.WithCancellation(CancellationToken.None);
					var isInInventory = location.IsPlayer;
					
					if (!isInInventory)
					{
						await Complain(checker, target, "thing-desc", "Thing has no description.");
						hasWarnings = true;
					}
				}
				else
				{
					await Complain(checker, target, "thing-desc", "Thing has no description.");
					hasWarnings = true;
				}
			}
		}

		// Check for missing messages
		if (warnings.HasFlag(WarningType.ThingMsgs))
		{
			// Check unlocked thing messages: SUCCESS, OSUCCESS, DROP, ODROP
			var success = await attributeService.GetAttributeAsync(checker, target, "SUCCESS", IAttributeService.AttributeMode.Read, false);
			var osuccess = await attributeService.GetAttributeAsync(checker, target, "OSUCCESS", IAttributeService.AttributeMode.Read, false);
			var drop = await attributeService.GetAttributeAsync(checker, target, "DROP", IAttributeService.AttributeMode.Read, false);
			var odrop = await attributeService.GetAttributeAsync(checker, target, "ODROP", IAttributeService.AttributeMode.Read, false);
			
			if (success.IsT1 || osuccess.IsT1 || drop.IsT1 || odrop.IsT1)
			{
				await Complain(checker, target, "thing-msgs", "Thing is missing messages (SUCCESS, OSUCCESS, DROP, or ODROP).");
				hasWarnings = true;
			}
			
			// Check locked thing messages: FAILURE
			var failure = await attributeService.GetAttributeAsync(checker, target, "FAILURE", IAttributeService.AttributeMode.Read, false);
			if (failure.IsT1)
			{
				await Complain(checker, target, "thing-msgs", "Thing is missing FAILURE message.");
				hasWarnings = true;
			}
		}

		return hasWarnings;
	}

	/// <summary>
	/// Check player-specific warnings
	/// </summary>
	private async Task<bool> CheckPlayerWarnings(AnySharpObject checker, AnySharpObject target, WarningType warnings)
	{
		var hasWarnings = false;

		if (warnings.HasFlag(WarningType.PlayerDesc))
		{
			var desc = await attributeService.GetAttributeAsync(checker, target, "DESCRIBE", IAttributeService.AttributeMode.Read, false);
			if (desc.IsT1)
			{
				await Complain(checker, target, "my-desc", "Player is missing description.");
				hasWarnings = true;
			}
		}

		return hasWarnings;
	}

	/// <summary>
	/// Send a warning message to the checker
	/// </summary>
	private async Task Complain(AnySharpObject checker, AnySharpObject target, string warningName, string message)
	{
		var targetObj = target.Object();
		await notifyService.Notify(checker, $"Warning '{warningName}' for {targetObj.Name}(#{targetObj.Key}):");
		await notifyService.Notify(checker, message);
	}
}
