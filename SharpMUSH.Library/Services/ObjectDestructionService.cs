using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public class ObjectDestructionService(
	IMediator mediator,
	INotifyService notifyService,
	IMoveService moveService,
	IEventService eventService,
	IOptionsMonitor<SharpMUSHOptions> configuration,
	ILogger<ObjectDestructionService> logger) : IObjectDestructionService
{
	private const int GodDbRefNumber = 1;
	private const string GoingFlag = "GOING";
	private const string GoingTwiceFlag = "GOING_TWICE";
	private const string ObjectDestroyEvent = "OBJECT`DESTROY";
	private const string NothingDbRef = "#-1";

	/// <inheritdoc />
	public bool IsSpecialObject(DBRef dbref)
	{
		var database = configuration.CurrentValue.Database;
		var command = configuration.CurrentValue.Command;
		var number = dbref.Number;

		return number == GodDbRefNumber
			|| number == database.PlayerStart
			|| number == database.MasterRoom
			|| number == database.BaseRoom
			|| number == database.DefaultHome
			|| number == command.ProbateJudge;
	}

	/// <inheritdoc />
	public async ValueTask<bool> FreeObjectAsync(IMUSHCodeParser parser, AnySharpObject target,
		CancellationToken cancellationToken = default)
	{
		var dbref = target.Object().DBRef;

		if (IsSpecialObject(dbref))
		{
			logger.LogWarning(
				"Refusing to destroy #{DbRef} ({Name}): it is a special object (player_start / master_room / "
				+ "base_room / default_home / God / probate_judge).", dbref.Number, target.Object().Name);
			return false;
		}

		// Stop anything the object has queued or is waiting on — PennMUSH free_object()'s do_halt()
		// plus its @drain/any/all-equivalent dequeue_semaphores().
		await mediator.Send(new HaltObjectQueueRequest(dbref), cancellationToken);

		// Type-specific teardown, in PennMUSH's order: clear_* runs before the object is unlinked.
		var cleared = await (target switch
		{
			SharpPlayer player => ClearPlayerAsync(parser, player, cancellationToken),
			SharpRoom room => ClearRoomAsync(parser, room, cancellationToken),
			// clear_exit() only detaches the exit from its source's exit list and refunds the deposit.
			// The detach is the AtLocation edge, which the storage delete removes, and SharpMUSH has no
			// money to refund (money() is unsupported), so nothing is left to do here.
			SharpExit => ValueTask.FromResult(true),
			SharpThing thing => ClearThingAsync(parser, thing, cancellationToken)
		});

		// DEVIATION: PennMUSH's empty_contents cannot fail — moveto is a pointer rewrite over an
		// in-memory database. Here evacuating is a move that can be refused (a containment loop, the
		// move-recursion ceiling, a provider write that did not land), and DeleteObjectCommand would
		// then take the location edge of content still standing inside, leaving every later read of
		// that content invalid. Nothing has been unlinked yet, so refusing here leaves the object
		// exactly as it was: still GOING, and retried on the next purge pass.
		if (!cleared)
		{
			logger.LogError(
				"Refusing to destroy #{DbRef} ({Name}): its contents could not be evacuated, and deleting it "
				+ "would strand them without a location.", dbref.Number, target.Object().Name);
			return false;
		}

		// Exits that led here point at their own source instead of into limbo, and anything that
		// called this home falls back to default_home. Both stand in for the pass in PennMUSH
		// free_object() that walks db_top fixing every reference to the doomed dbref.
		await RelinkEntrancesAsync(dbref, cancellationToken);
		await RehomeDependentsAsync(dbref, cancellationToken);

		// Read while the object still exists; the event fires once it does not.
		var eventArguments = await DescribeForDestroyEventAsync(target, cancellationToken);

		var deleted = await mediator.Send(new DeleteObjectCommand(dbref), cancellationToken);
		if (!deleted)
		{
			return false;
		}

		// PennMUSH free_object() queues OBJECT`DESTROY with everything about the object it can still
		// name, "since the event will deal with an object that doesn't exist anymore".
		await eventService.TriggerEventAsync(parser, ObjectDestroyEvent, null, eventArguments);

		return true;
	}

	/// <summary>
	/// The <c>OBJECT`DESTROY</c> argument list, in PennMUSH's order: objid, name, type, owner objid,
	/// parent objid, zone objid. Gathered before the delete, because none of it is readable after.
	/// </summary>
	private static async ValueTask<string[]> DescribeForDestroyEventAsync(AnySharpObject target,
		CancellationToken ct)
	{
		var obj = target.Object();
		var owner = await obj.Owner.WithCancellation(ct);
		var parent = await obj.Parent.WithCancellation(ct);
		var zone = await obj.Zone.WithCancellation(ct);

		return
		[
			obj.DBRef.ToString(),
			obj.Name,
			obj.Type,
			owner.Object.DBRef.ToString(),
			parent is AnySharpObject parentObject ? parentObject.Object().DBRef.ToString() : NothingDbRef,
			zone is AnySharpObject zoneObject ? zoneObject.Object().DBRef.ToString() : NothingDbRef
		];
	}

	/// <inheritdoc />
	public async ValueTask<int> PurgeAsync(IMUSHCodeParser parser, CancellationToken cancellationToken = default)
	{
		var goingTwice = await mediator.Send(new GetObjectFlagQuery(GoingTwiceFlag), cancellationToken);
		if (goingTwice is null)
		{
			logger.LogError("The {Flag} flag is not defined; purge cannot run.", GoingTwiceFlag);
			return 0;
		}

		// GOING is pushed down to the database. PennMUSH purge() can afford to walk db_top because the
		// whole database is in memory; this runs on a timer against a remote store, where "fetch every
		// object, then fetch each one's flags" is a full scan plus a round-trip per object, every ten
		// minutes, to find a set that is usually empty.
		//
		// Materialised before mutating: the stream is a live read, and freeing an object (which cascades
		// into the exits of a room) deletes rows out from under it.
		var doomed = await mediator
			.CreateStream(new GetFilteredObjectsQuery(new ObjectSearchFilter { HasFlag = GoingFlag }),
				cancellationToken)
			.ToListAsync(cancellationToken);

		var freed = 0;

		foreach (var doomedObject in doomed)
		{
			// Re-resolved because a cascade earlier in this pass may already have taken it (a room takes
			// its exits with it), and a stale dbref reads as None.
			if (await mediator.Send(new GetObjectNodeQuery(doomedObject.DBRef), cancellationToken) is not AnySharpObject candidate) continue;

			// Belt and braces over the pushdown, deliberately kept despite being redundant with the
			// query above. A provider that silently ignores HasFlag hands back the entire database, and
			// with no second opinion this loop would then mark every object in the game GOING_TWICE and
			// start freeing them on the following pass. The predicate is pinned
			// (ObjectSearchFilterPushdownTests), but the cost of not
			// trusting it here is one flag read on an already-small set.
			if (!await candidate.HasFlag(GoingFlag)) continue;

			if (!await candidate.HasFlag(GoingTwiceFlag))
			{
				// First pass: advance it. set_flag_internal in PennMUSH — no permission check, because
				// the purge is the server acting, not a player.
				await mediator.Send(new SetObjectFlagCommand(candidate, goingTwice), cancellationToken);
				continue;
			}

			if (await FreeObjectAsync(parser, candidate, cancellationToken))
			{
				freed++;
			}
		}

		logger.LogInformation("Purge freed {Freed} object(s).", freed);

		return freed;
	}

	/// <summary>
	/// PennMUSH <c>clear_thing()</c>, minus the deposit refund (SharpMUSH tracks no money).
	/// </summary>
	/// <returns><see langword="false"/> when a piece of content could not be evacuated.</returns>
	private ValueTask<bool> ClearThingAsync(IMUSHCodeParser parser, SharpThing thing, CancellationToken ct)
		=> EmptyContentsAsync(parser, thing, ct);

	/// <summary>
	/// PennMUSH <c>clear_room()</c>. Exits sourced in the room are destroyed with it; in SharpMUSH
	/// they are contents of the room, so <see cref="EmptyContentsAsync"/> already handles them.
	/// </summary>
	/// <returns><see langword="false"/> when a piece of content could not be evacuated.</returns>
	private ValueTask<bool> ClearRoomAsync(IMUSHCodeParser parser, SharpRoom room, CancellationToken ct)
		=> EmptyContentsAsync(parser, room, ct);

	/// <summary>
	/// PennMUSH <c>clear_player()</c>: the <c>clear_thing()</c> work, then probate — channels and
	/// surviving possessions go to the probate judge, the rest are freed, and attributes the player
	/// wrote change hands. This is the only place a destroyed player's belongings change owner:
	/// <c>@destroy</c> merely marks them, so <c>@undestroy</c> has nothing to give back.
	/// </summary>
	/// <remarks>
	/// The two steps that can fail run first: resolving the probate player, then evacuating. Refusing at
	/// either leaves the player and everything they own untouched, still GOING, and the whole of it is
	/// retried on the next purge pass. Penn runs <c>chan_chownall</c> before <c>clear_thing</c>, and
	/// neither can fail there.
	/// </remarks>
	/// <returns>
	/// <see langword="false"/> when a piece of content could not be evacuated, or no probate player resolves.
	/// </returns>
	private async ValueTask<bool> ClearPlayerAsync(IMUSHCodeParser parser, SharpPlayer player, CancellationToken ct)
	{
		// With nobody to hand them to, deleting the player would sever the ownership edge of everything
		// they own. Checked before anything moves, so the player stays GOING exactly as they were and the
		// next purge retries once the configuration is fixed.
		if (await ResolveProbatePlayerAsync(ct) is not { } probate)
		{
			return false;
		}

		if (!await EmptyContentsAsync(parser, player, ct))
		{
			return false;
		}

		var playerDbRef = player.Object.DBRef;

		await foreach (var channel in mediator.CreateStream(new GetChannelsOwnedByQuery(playerDbRef), ct))
		{
			await mediator.Send(new UpdateChannelOwnerCommand(channel, probate), ct);
		}

		// Materialised: freeing a possession deletes rows the live stream would still be reading.
		var owned = await mediator
			.CreateStream(new GetFilteredObjectsQuery(new ObjectSearchFilter { Owner = playerDbRef }), ct)
			.Select(candidate => candidate.DBRef)
			.Where(ownedDbRef => ownedDbRef.Number != playerDbRef.Number)
			.ToListAsync(ct);

		var command = configuration.CurrentValue.Command;

		foreach (var ownedDbRef in owned)
		{
			// Resolved one at a time because freeing an earlier possession may already have taken this one
			// (a room takes its exits with it), and acting on a stale copy would write to a deleted dbref.
			if (await mediator.Send(new GetObjectNodeQuery(ownedDbRef), ct) is not AnySharpObject possession) continue;

			// Belt and braces over the pushdown, as in PurgeAsync: a provider that ignored the owner
			// predicate would otherwise have this free the whole database.
			if (!await IsOwnedByAsync(possession, playerDbRef, ct)) continue;

			var survives = IsSpecialObject(possession.Object().DBRef)
				|| !command.DestroyPossessions
				|| (command.ReallySafe && await possession.HasFlag("SAFE"));

			// A possession whose own teardown is refused is handed over instead: deleting the player
			// would otherwise sever its ownership edge and make every later read of it throw.
			if (survives || !await FreeObjectAsync(parser, possession, ct))
			{
				await mediator.Send(new SetObjectOwnerCommand(possession, probate), ct);
			}
		}

		await mediator.Send(new ReassignAttributeOwnerCommand(player, probate), ct);

		return true;
	}

	/// <summary>
	/// PennMUSH <c>empty_contents()</c>: warn everyone inside, destroy any exits being carried, and
	/// send everything else home — to <c>default_home</c> when its own home is missing, is the
	/// container being destroyed, or is itself an exit.
	/// </summary>
	/// <returns>
	/// <see langword="false"/> when a piece of content is still standing inside the container — its
	/// home refused it and so did <c>default_home</c>. The caller must not delete the container then.
	/// </returns>
	private async ValueTask<bool> EmptyContentsAsync(IMUSHCodeParser parser, AnySharpContainer container,
		CancellationToken ct)
	{
		var containerDbRefNumber = container.Object().DBRef.Number;
		var contents = await mediator.CreateStream(new GetContentsQuery(container), ct).ToListAsync(ct);

		foreach (var content in contents)
		{
			await notifyService.NotifyLocalized(content.Object().DBRef,
				nameof(ErrorMessages.Notifications.FloorDisappearsNothingness), sender: null);
		}

		var emptied = true;

		foreach (var content in contents)
		{
			if (content is SharpExit exit)
			{
				// An exit cannot be sent anywhere — PennMUSH frees exits found in contents outright.
				await FreeObjectAsync(parser, exit, ct);
				continue;
			}

			var destination = await ResolveEvacuationTargetAsync(content, containerDbRefNumber, ct);
			if (destination is null)
			{
				emptied = false;
				continue;
			}

			// PennMUSH's empty_contents calls moveto with nomovemsgs = 0, "so that AENTER and such are
			// all triggered properly" (destroy.c:821), and SYSEVENT — #-1 (externs.h:168) — as the
			// enactor, which this codebase resolves to God the same way EventService does. moveto is
			// enter_room (move.c:52-56), so the evacuee gets the automatic look at where it landed.
			var moved = await moveService.EnterRoom(parser, content, destination, noMoveMsgs: false,
				new DBRef(-1), "container destroyed");

			if (moved is not Error<string> refused)
			{
				continue;
			}

			// Its own home refused it — a containment loop, a lock, the move-recursion ceiling.
			// default_home is where empty_contents already sends anything whose home is unusable, so
			// it is the one retry worth making before giving up on this content.
			var fallback = await ResolveDefaultHomeAsync(ct);

			if (fallback is null || fallback.Object().DBRef.Equals(destination.Object().DBRef))
			{
				logger.LogError(
					"#{Content} could not be evacuated from #{Container}: {Reason}",
					content.Object().DBRef.Number, containerDbRefNumber, refused.Value);
				emptied = false;
				continue;
			}

			var rehoused = await moveService.EnterRoom(parser, content, fallback, noMoveMsgs: false,
				new DBRef(-1), "container destroyed");

			if (rehoused is Error<string> fallbackRefused)
			{
				logger.LogError(
					"#{Content} could not be evacuated from #{Container} to its home ({Reason}) nor to "
					+ "default_home (#{DefaultHome}: {FallbackReason}).",
					content.Object().DBRef.Number, containerDbRefNumber, refused.Value,
					fallback.Object().DBRef.Number, fallbackRefused.Value);
				emptied = false;
			}
		}

		return emptied;
	}

	/// <summary>
	/// Where a piece of content goes when the thing holding it is destroyed: its home, or
	/// <c>default_home</c> when that home is unusable.
	/// </summary>
	private async ValueTask<AnySharpContainer?> ResolveEvacuationTargetAsync(AnySharpContent content,
		int containerDbRefNumber, CancellationToken ct)
	{
		if (await content.Home() is AnySharpContainer candidate)
		{
			// Sending it to the container that is being destroyed would only strand it again.
			if (candidate.Object().DBRef.Number != containerDbRefNumber)
			{
				return candidate;
			}
		}

		return await ResolveDefaultHomeAsync(ct);
	}

	/// <summary>
	/// Exits whose destination was the destroyed object are relinked to their own source rather than
	/// left dangling — PennMUSH <c>free_object()</c>: "If our destination is destroyed, then we relink
	/// to the source room (so that the exit can't be stolen)."
	/// </summary>
	private async ValueTask RelinkEntrancesAsync(DBRef dbref, CancellationToken ct)
	{
		var entrances = await mediator.CreateStream(new GetEntrancesQuery(dbref), ct).ToListAsync(ct);

		foreach (var entrance in entrances)
		{
			var source = await entrance.Location.WithCancellation(ct);
			await mediator.Send(new LinkExitCommand(entrance, source), ct);
		}
	}

	/// <summary>
	/// Anything that called the destroyed object home is rehomed to <c>default_home</c> — PennMUSH
	/// <c>free_object()</c>'s <c>Home(i) = DEFAULT_HOME</c>. Without this the storage delete severs
	/// the home edge and every later read of the dependent throws.
	/// </summary>
	private async ValueTask RehomeDependentsAsync(DBRef dbref, CancellationToken ct)
	{
		// Exits are handled by RelinkEntrancesAsync — for an exit the home edge is its destination.
		// The stream is a snapshot (Lightning reads it inside one transaction), so rehoming while
		// walking it is safe. The default
		// home is resolved on the first dependent, so an object nothing is homed at costs no lookup.
		var homeless = mediator.CreateStream(new GetHomedAtQuery(dbref), ct).Where(dependent => !dependent.IsExit);
		AnySharpContainer? defaultHome = null;
		var resolved = false;
		var abandoned = 0;

		await foreach (var dependent in homeless.WithCancellation(ct))
		{
			if (!resolved)
			{
				defaultHome = await ResolveDefaultHomeAsync(ct);
				resolved = true;
			}

			if (defaultHome is null)
			{
				abandoned++;
				continue;
			}

			await mediator.Send(new SetObjectHomeCommand(dependent, defaultHome), ct);
		}

		if (abandoned > 0)
		{
			logger.LogError(
				"default_home (#{DefaultHome}) is not a valid container; {Count} object(s) homed at "
				+ "#{DbRef} will be left without a home.",
				configuration.CurrentValue.Database.DefaultHome, abandoned, dbref.Number);
		}
	}

	private static async ValueTask<bool> IsOwnedByAsync(AnySharpObject target, DBRef owner, CancellationToken ct)
		=> (await target.Object().Owner.WithCancellation(ct)).Object.DBRef.Number == owner.Number;

	private async ValueTask<AnySharpContainer?> ResolveDefaultHomeAsync(CancellationToken ct)
	{
		var configured = new DBRef((int)configuration.CurrentValue.Database.DefaultHome);
		var node = await mediator.Send(new GetObjectNodeQuery(configured), ct);

		return node is AnySharpObject found && found.IsContainer ? found.AsContainer : null;
	}

	private async ValueTask<SharpPlayer?> ResolveProbatePlayerAsync(CancellationToken ct)
	{
		var configured = new DBRef((int)configuration.CurrentValue.Command.ProbateJudge);
		if (await mediator.Send(new GetObjectNodeQuery(configured), ct) is AnySharpObject and SharpPlayer judge)
		{
			return judge;
		}

		logger.LogWarning(
			"probate_judge config option (#{ProbateDbRef}) is set to an invalid object; falling back to God (#1).",
			configured.Number);

		if (await mediator.Send(new GetObjectNodeQuery(new DBRef(GodDbRefNumber)), ct) is AnySharpObject and SharpPlayer god)
		{
			return god;
		}

		logger.LogError("God (#1) is not a valid player; possessions cannot be handed to a probate player.");
		return null;
	}
}
