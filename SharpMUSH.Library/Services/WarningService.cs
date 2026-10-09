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
/// <remarks>
/// Every attribute a check looks for is read with <c>atr_get</c> (<c>src/warnings.c:76-195</c>), so a
/// description or message inherited from a parent or the type ancestor counts as present.
/// </remarks>
public class WarningService(
	INotifyService notifyService,
	IAttributeService attributeService,
	ILockService lockService,
	IMediator mediator) : IWarningService
{
	/// <summary>One problem a check found: the warning's name, as <c>@warnings</c> spells it, and what is wrong.</summary>
	private readonly record struct Warning(string Name, string Message);

	/// <summary>
	/// Check warnings on a specific object
	/// </summary>
	public async Task<bool> CheckObjectAsync(AnySharpObject checker, AnySharpObject target)
	{
		var targetObj = target.Object();

		if (await targetObj.IsGoingAsync() || await targetObj.HasNoWarnFlagAsync())
		{
			return false;
		}

		// An object with no owner edge is skipped rather than reported.
		if (await OwnerOfAsync(targetObj) is not SharpPlayer owner || await owner.Object.HasNoWarnFlagAsync())
		{
			return false;
		}

		var warnings = await GetWarningsForCheck(checker, targetObj, owner.Object);
		if (warnings == WarningType.None)
		{
			return false;
		}

		var found = false;
		await foreach (var warning in FindWarnings(checker, target, warnings))
		{
			await Complain(checker, target, warning);
			found = true;
		}

		return found;
	}

	/// <summary>
	/// Check warnings on all objects owned by a player
	/// </summary>
	public async Task<int> CheckOwnedObjectsAsync(AnySharpObject owner)
	{
		var ownerRef = owner.Object().DBRef;

		// The owner index names the owner's objects, in ascending dbref order, so only those are typed
		// and checked; the world is not scanned. Checking writes nothing, so the stream is read as it goes.
		var warningCount = await mediator.CreateStream(new GetFilteredObjectsQuery(new ObjectSearchFilter { Owner = ownerRef }))
			.Select(async (found, ct) => await mediator.Send(new GetObjectNodeQuery(found.DBRef), ct))
			.OfType<AnySharpObject>()
			.Where(async (obj, _) => await OwnerOfAsync(obj.Object()) is SharpPlayer objectOwner
				&& objectOwner.Object.DBRef.Equals(ownerRef))
			.Where(async (obj, _) => await CheckObjectAsync(owner, obj))
			.CountAsync();

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

		// Use GetAllTypedObjectsQuery to get fully-typed objects directly, avoiding two secondary
		// per-object GetObjectNodeQuery calls inside the loop (which route through FusionCache
		// per-key locks and contend with — or even deadlock against — active player commands).
		await foreach (var obj in mediator.CreateStream(new GetAllTypedObjectsQuery()))
		{
			checkedCount++;

			if (await OwnerOfAsync(obj.Object()) is not SharpPlayer owner)
			{
				continue;
			}

			var ownerAny = new AnySharpObject(owner);
			if (!warningsByOwner.TryGetValue(owner.Object.DBRef, out var entry))
			{
				warningsByOwner[owner.Object.DBRef] = entry = (ownerAny, []);
			}

			if (await CheckObjectAsync(ownerAny, obj))
			{
				var objBase = obj.Object();
				entry.Warnings.Add($"{objBase.Name}(#{objBase.Key})");
			}
		}

		foreach (var (owner, warnings) in warningsByOwner.Values.Where(x => x.Warnings.Count > 0))
		{
			// Notify connected owners only (already filtered via ConnectionService)
			await notifyService.Notify(owner, $"Warning check complete: {warnings.Count} warnings found on your objects:");
			foreach (var warning in warnings)
			{
				await notifyService.Notify(owner, $"  - {warning}");
			}
		}

		return checkedCount;
	}

	/// <summary>
	/// The owner of <paramref name="obj"/>, or <see cref="NotFound"/> when its owner edge is missing or does not
	/// name a player. <c>SharpObject.Owner</c> throws for that; the checks here walk objects that may be damaged.
	/// </summary>
	private async ValueTask<Found<SharpPlayer>> OwnerOfAsync(SharpObject obj)
		=> await mediator.Send(new GetOwnerOfQuery(obj.Key.ToString(), obj.Key)) switch
		{
			AnySharpObject and SharpPlayer owner => owner,
			_ => new NotFound()
		};

	/// <summary>
	/// Determine which warnings to use for a check
	/// </summary>
	private async Task<WarningType> GetWarningsForCheck(AnySharpObject checker, SharpObject target, SharpObject owner)
	{
		var checkerObj = checker.Object();

		return await OwnerOfAsync(checkerObj) switch
		{
			// Checker has no owner edge - fall back to checker's own warnings or None
			NotFound => checkerObj.Warnings,
			// The checker's owner owns the target: the target's warnings, falling back to its owner's
			SharpPlayer checkerOwner when checkerOwner.Object.DBRef.Equals(owner.DBRef)
				=> target.Warnings != WarningType.None ? target.Warnings : owner.Warnings,
			// Otherwise (admin checking), the checker's warnings, falling back to its owner's
			SharpPlayer checkerOwner
				=> checkerObj.Warnings != WarningType.None ? checkerObj.Warnings : checkerOwner.Object.Warnings
		};
	}

	/// <summary>
	/// Every warning <paramref name="target"/> earns under <paramref name="warnings"/>: the lock checks every
	/// object gets, then the checks for its type.
	/// </summary>
	private IAsyncEnumerable<Warning> FindWarnings(AnySharpObject checker, AnySharpObject target, WarningType warnings)
		=> LockWarnings(target, warnings).Concat(target switch
		{
			SharpRoom => RoomWarnings(checker, target, warnings),
			SharpExit exit => ExitWarnings(checker, target, exit, warnings),
			SharpThing thing => ThingWarnings(checker, target, thing, warnings),
			SharpPlayer => PlayerWarnings(checker, target, warnings)
		});

	/// <summary>
	/// Each lock that does not validate: invalid syntax, references to objects that do not exist or are GOING,
	/// eval locks naming missing attributes, indirect locks that are not there.
	/// </summary>
	private IAsyncEnumerable<Warning> LockWarnings(AnySharpObject target, WarningType warnings)
		=> warnings.HasFlag(WarningType.LockProbs)
			? target.Object().Locks
				.Where(entry => !string.IsNullOrWhiteSpace(entry.Value.LockString))
				.Where(entry => !lockService.Validate(entry.Value.LockString, target))
				.Select(entry => new Warning("lock-checks", $"Lock '{entry.Key}' has problems: invalid syntax or references."))
				.ToAsyncEnumerable()
			: AsyncEnumerable.Empty<Warning>();

	private async IAsyncEnumerable<Warning> RoomWarnings(AnySharpObject checker, AnySharpObject target, WarningType warnings)
	{
		if (warnings.HasFlag(WarningType.RoomDesc) && await LacksAnyAsync(checker, target, "DESCRIBE"))
		{
			yield return new Warning("room-desc", "Room has no description.");
		}
	}

	private async IAsyncEnumerable<Warning> ExitWarnings(AnySharpObject checker, AnySharpObject target, SharpExit exit,
		WarningType warnings)
	{
		// One read of the destination edge serves the link and topology checks.
		var destination = await exit.Home.WithCancellation(CancellationToken.None);

		if (warnings.HasFlag(WarningType.ExitUnlinked))
		{
			await foreach (var warning in ExitLinkWarnings(checker, target, destination))
			{
				yield return warning;
			}
		}

		if (warnings.HasFlag(WarningType.ExitDesc) && await LacksAnyAsync(checker, target, "DESCRIBE"))
		{
			yield return new Warning("exit-desc", "Exit has no description.");
		}

		if (warnings.HasFlag(WarningType.ExitMsgs))
		{
			if (await LacksAnyAsync(checker, target, "SUCCESS", "OSUCCESS", "ODROP"))
			{
				yield return new Warning("exit-msgs", "Exit is missing messages (SUCCESS, OSUCCESS, or ODROP).");
			}

			if (await LacksAnyAsync(checker, target, "FAILURE"))
			{
				yield return new Warning("exit-msgs", "Exit is missing FAILURE message.");
			}
		}

		if (warnings.HasFlag(WarningType.ExitOneway) || warnings.HasFlag(WarningType.ExitMultiple))
		{
			await foreach (var warning in ExitTopologyWarnings(exit, destination, warnings))
			{
				yield return warning;
			}
		}
	}

	/// <summary>
	/// An @open'd or @unlink'd exit has no destination edge at all; a linked one may still point at NOTHING.
	/// #0 is the master room, a real destination, so only negative dbrefs are invalid. A variable exit (one
	/// linked to HOME, #-1) needs a DESTINATION or EXITTO attribute to say where it leads.
	/// </summary>
	private async IAsyncEnumerable<Warning> ExitLinkWarnings(AnySharpObject checker, AnySharpObject target,
		AnyOptionalSharpContainer destination)
	{
		int? destinationNumber = destination is AnySharpContainer linked ? linked.Object().DBRef.Number : null;

		if (destinationNumber is null or < 0)
		{
			yield return new Warning("exit-unlinked", "Exit is unlinked (no destination set). This exit can be stolen.");
		}

		if (destinationNumber == -1 && await LacksAllAsync(checker, target, "DESTINATION", "EXITTO"))
		{
			yield return new Warning("exit-unlinked", "Variable exit lacks DESTINATION or EXITTO attribute.");
		}
	}

	/// <summary>
	/// How many exits lead back from the destination to the exit's source. An unlinked exit, or one between
	/// negative dbrefs, has no topology to analyse: it is neither one-way nor duplicated.
	/// </summary>
	private async IAsyncEnumerable<Warning> ExitTopologyWarnings(SharpExit exit, AnyOptionalSharpContainer maybeDestination,
		WarningType warnings)
	{
		var source = (await exit.Location.WithCancellation(CancellationToken.None)).Object().DBRef;
		if (maybeDestination is not AnySharpContainer destination
				|| destination.Object().DBRef.Number < 0
				|| source.Number < 0)
		{
			yield break;
		}

		var returnExitCount = await mediator.CreateStream(new GetExitsQuery(destination))
			.CountAsync(async (returnExit, ct) => await returnExit.Home.WithCancellation(ct) is AnySharpContainer returnDestination
				&& returnDestination.Object().DBRef.Equals(source));

		if (warnings.HasFlag(WarningType.ExitOneway) && returnExitCount == 0)
		{
			yield return new Warning("exit-oneway", "Exit has no return path from destination back to source.");
		}

		if (warnings.HasFlag(WarningType.ExitMultiple) && returnExitCount > 1)
		{
			yield return new Warning("exit-multiple", $"Exit has {returnExitCount} return paths from destination back to source.");
		}
	}

	private async IAsyncEnumerable<Warning> ThingWarnings(AnySharpObject checker, AnySharpObject target, SharpThing thing,
		WarningType warnings)
	{
		// A thing in a player's inventory needs no description, as in PennMUSH.
		if (warnings.HasFlag(WarningType.ThingDesc)
				&& await LacksAnyAsync(checker, target, "DESCRIBE")
				&& !(await thing.Location.WithCancellation(CancellationToken.None)).IsPlayer)
		{
			yield return new Warning("thing-desc", "Thing has no description.");
		}

		if (warnings.HasFlag(WarningType.ThingMsgs))
		{
			if (await LacksAnyAsync(checker, target, "SUCCESS", "OSUCCESS", "DROP", "ODROP"))
			{
				yield return new Warning("thing-msgs", "Thing is missing messages (SUCCESS, OSUCCESS, DROP, or ODROP).");
			}

			if (await LacksAnyAsync(checker, target, "FAILURE"))
			{
				yield return new Warning("thing-msgs", "Thing is missing FAILURE message.");
			}
		}
	}

	private async IAsyncEnumerable<Warning> PlayerWarnings(AnySharpObject checker, AnySharpObject target, WarningType warnings)
	{
		if (warnings.HasFlag(WarningType.PlayerDesc) && await LacksAnyAsync(checker, target, "DESCRIBE"))
		{
			yield return new Warning("my-desc", "Player is missing description.");
		}
	}

	/// <summary>Whether any of <paramref name="names"/> is unset on <paramref name="target"/>, parents included.</summary>
	private async ValueTask<bool> LacksAnyAsync(AnySharpObject checker, AnySharpObject target, params string[] names)
		=> await names.ToAsyncEnumerable().AnyAsync(async (name, _) => await LacksAsync(checker, target, name));

	/// <summary>Whether every one of <paramref name="names"/> is unset on <paramref name="target"/>, parents included.</summary>
	private async ValueTask<bool> LacksAllAsync(AnySharpObject checker, AnySharpObject target, params string[] names)
		=> await names.ToAsyncEnumerable().AllAsync(async (name, _) => await LacksAsync(checker, target, name));

	private async ValueTask<bool> LacksAsync(AnySharpObject checker, AnySharpObject target, string name)
		=> (await attributeService.GetAttributeAsync(checker, target, name, IAttributeService.AttributeMode.Read, parent: true)).IsNone;

	/// <summary>
	/// Send a warning message to the checker
	/// </summary>
	private async Task Complain(AnySharpObject checker, AnySharpObject target, Warning warning)
	{
		var targetObj = target.Object();
		await notifyService.Notify(checker, $"Warning '{warning.Name}' for {targetObj.Name}(#{targetObj.Key}):");
		await notifyService.Notify(checker, warning.Message);
	}
}
