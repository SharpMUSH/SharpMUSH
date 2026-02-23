using Mediator;
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
	ILockService lockService,
	IMediator mediator) : IWarningService
{
	/// <summary>
	/// Check warnings on a specific object
	/// </summary>
	public async Task<bool> CheckObjectAsync(AnySharpObject checker, AnySharpObject target)
	{
		var targetObj = target.Object();

		// Skip GOING objects
		if (await targetObj.IsGoingAsync())
		{
			return false;
		}

		// Skip NO_WARN objects
		if (await targetObj.HasNoWarnFlagAsync())
		{
			return false;
		}

		// Skip if owner has NO_WARN
		var owner = await targetObj.Owner.WithCancellation(CancellationToken.None);
		if (await owner.Object.HasNoWarnFlagAsync())
		{
			return false;
		}

		// Determine which warnings to check
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
		var warningsByOwner = new Dictionary<DBRef, (AnySharpObject Owner, List<string> Warnings)>();

		// Get all objects in the database
		var allObjects = mediator.CreateStream(new GetAllObjectsQuery());

		await foreach (var obj in allObjects)
		{
			checkedCount++;

			// Get the owner for this object
			// TODO: There's a failure here with: System.InvalidOperationException: Sequence contains no elements
			var owner = await obj.Owner.WithCancellation(CancellationToken.None);
			var ownerDbRef = owner.Object.DBRef;

			// Get the full object nodes
			var ownerNode = await mediator.Send(new GetObjectNodeQuery(ownerDbRef));
			var objectNode = await mediator.Send(new GetObjectNodeQuery(obj.DBRef));

			if (ownerNode.IsNone || objectNode.IsNone)
			{
				continue;
			}

			var ownerAny = ownerNode.WithoutNone();
			var objectAny = objectNode.WithoutNone();

			// Track warnings per owner - use TryAdd for efficiency
			if (!warningsByOwner.TryGetValue(ownerDbRef, out _))
			{
				warningsByOwner[ownerDbRef] = (ownerAny, []);
			}

			// Check warnings (using owner as checker for their own objects)
			var hadWarnings = await CheckObjectAsync(ownerAny, objectAny);

			if (hadWarnings)
			{
				warningsByOwner[ownerDbRef].Warnings.Add($"{obj.Name}(#{obj.Key})");
			}
		}

		// Notify connected owners of their warnings
		foreach (var (ownerDbRef, (owner, warnings)) in warningsByOwner)
		{
			if (warnings.Count > 0)
			{
				// Notify connected owners only (already filtered via ConnectionService)
				await notifyService.Notify(owner, $"Warning check complete: {warnings.Count} warnings found on your objects:");
				foreach (var warning in warnings)
				{
					await notifyService.Notify(owner, $"  - {warning}");
				}
			}
		}

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
			var targetObj = target.Object();
			var locks = targetObj.Locks;

			// Check each lock on the object
			foreach (var (lockName, lockData) in locks)
			{
				var lockString = lockData.LockString;
				// Skip empty locks
				if (string.IsNullOrWhiteSpace(lockString))
				{
					continue;
				}

				try
				{
					// Validate the lock - this checks for:
					// - Invalid syntax
					// - Invalid object references (non-existent dbrefs)
					// - References to GOING/garbage objects
					// - Missing attributes in eval locks
					// - Indirect locks that aren't present
					var isValid = lockService.Validate(lockString, target);

					if (!isValid)
					{
						await Complain(checker, target, "lock-checks",
							$"Lock '{lockName}' has problems: invalid syntax or references.");
						hasWarnings = true;
					}
				}
				catch (ArgumentException ex)
				{
					// Argument exceptions indicate invalid lock syntax or format
					await Complain(checker, target, "lock-checks",
						$"Lock '{lockName}' has problems: {ex.Message}");
					hasWarnings = true;
				}
				catch (Exception ex)
				{
					// Any other exception during validation indicates a problem with the lock
					await Complain(checker, target, "lock-checks",
						$"Lock '{lockName}' has problems: unable to parse or validate ({ex.GetType().Name}).");
					hasWarnings = true;
				}
			}
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
				var exit = target.AsExit;
				try
				{
					var destination = await exit.Location.WithCancellation(CancellationToken.None);
					var destObj = destination.Match(
						player => player.Object,
						room => room.Object,
						thing => thing.Object
					);

					// Check if destination DBRef is -1 (NOTHING) or 0 (invalid)
					if (destObj.DBRef.Number <= 0)
					{
						await Complain(checker, target, "exit-unlinked",
							"Exit is unlinked (destination is NOTHING). This exit can be stolen.");
						hasWarnings = true;
					}
				}
				catch
				{
					// If we can't get the location, consider it unlinked
					await Complain(checker, target, "exit-unlinked",
						"Exit is unlinked (no valid destination). This exit can be stolen.");
					hasWarnings = true;
				}

				// Check for variable exits without DESTINATION or EXITTO attribute
				// Variable exits are exits with a destination of HOME (#-1) that use
				// DESTINATION or EXITTO attributes to dynamically determine the target
				try
				{
					var exitLocation = await target.AsExit.Location.WithCancellation(CancellationToken.None);
					if (exitLocation.Object().DBRef.Number == -1)
					{
						var destAttr = await attributeService.GetAttributeAsync(checker, target, "DESTINATION", IAttributeService.AttributeMode.Read, false);
						var exitToAttr = await attributeService.GetAttributeAsync(checker, target, "EXITTO", IAttributeService.AttributeMode.Read, false);

						if (destAttr.IsNone && exitToAttr.IsNone)
						{
							await Complain(checker, target, "exit-unlinked",
								"Variable exit lacks DESTINATION or EXITTO attribute.");
							hasWarnings = true;
						}
					}
				}
				catch
				{
					// If we can't get the location for checking variable exit, skip this check
				}
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
		// These require topology analysis
		if (target.IsExit && (warnings.HasFlag(WarningType.ExitOneway) || warnings.HasFlag(WarningType.ExitMultiple)))
		{
			var exit = target.AsExit;
			try
			{
				var destination = await exit.Location.WithCancellation(CancellationToken.None);
				var source = await exit.Home.WithCancellation(CancellationToken.None);

				var destObj = destination.Match(
					player => player.Object,
					room => room.Object,
					thing => thing.Object
				);

				var sourceObj = source.Match(
					player => player.Object,
					room => room.Object,
					thing => thing.Object
				);

				// Only check if we have valid source and destination (not NOTHING)
				if (destObj.DBRef.Number > 0 && sourceObj.DBRef.Number > 0)
				{
					// Get all exits from the destination that lead back to the source
					var returnExitsQuery = mediator.CreateStream(new GetExitsQuery(destination));
					var returnExitCount = 0;

					await foreach (var returnExit in returnExitsQuery)
					{
						try
						{
							var returnDest = await returnExit.Location.WithCancellation(CancellationToken.None);
							var returnDestObj = returnDest.Match(
								player => player.Object,
								room => room.Object,
								thing => thing.Object
							);

							if (returnDestObj.DBRef.Equals(sourceObj.DBRef))
							{
								returnExitCount++;
							}
						}
						catch
						{
							// Ignore exits we can't check
						}
					}

					// Check for one-way exits (no return)
					if (warnings.HasFlag(WarningType.ExitOneway) && returnExitCount == 0)
					{
						await Complain(checker, target, "exit-oneway",
							"Exit has no return path from destination back to source.");
						hasWarnings = true;
					}

					// Check for multiple return exits
					if (warnings.HasFlag(WarningType.ExitMultiple) && returnExitCount > 1)
					{
						await Complain(checker, target, "exit-multiple",
							$"Exit has {returnExitCount} return paths from destination back to source.");
						hasWarnings = true;
					}
				}
			}
			catch
			{
				// If we can't check topology, skip this check silently
			}
		}

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
