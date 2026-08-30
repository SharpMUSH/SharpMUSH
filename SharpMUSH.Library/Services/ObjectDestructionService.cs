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
		await target.Match<ValueTask>(
			player => ClearPlayerAsync(parser, player, cancellationToken),
			room => ClearRoomAsync(parser, room, cancellationToken),
			// clear_exit() only detaches the exit from its source's exit list and refunds the deposit.
			// The detach is the AtLocation edge, which the storage delete removes, and SharpMUSH has no
			// money to refund (money() is unsupported), so nothing is left to do here.
			_ => ValueTask.CompletedTask,
			thing => ClearThingAsync(parser, thing, cancellationToken));

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
			parent.IsNone ? NothingDbRef : parent.Known.Object().DBRef.ToString(),
			zone.IsNone ? NothingDbRef : zone.Known.Object().DBRef.ToString()
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
			var node = await mediator.Send(new GetObjectNodeQuery(doomedObject.DBRef), cancellationToken);
			if (node.IsNone) continue;

			var candidate = node.Known;

			// Belt and braces over the pushdown, deliberately kept despite being redundant with the
			// query above. A provider that silently ignores HasFlag hands back the entire database, and
			// with no second opinion this loop would then mark every object in the game GOING_TWICE and
			// start freeing them on the following pass. All three providers ignored or broke that
			// predicate until it was fixed and pinned (ObjectSearchFilterPushdownTests); the cost of not
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
	private ValueTask ClearThingAsync(IMUSHCodeParser parser, SharpThing thing, CancellationToken ct)
		=> EmptyContentsAsync(parser, thing, ct);

	/// <summary>
	/// PennMUSH <c>clear_room()</c>. Exits sourced in the room are destroyed with it; in SharpMUSH
	/// they are contents of the room, so <see cref="EmptyContentsAsync"/> already handles them.
	/// </summary>
	private ValueTask ClearRoomAsync(IMUSHCodeParser parser, SharpRoom room, CancellationToken ct)
		=> EmptyContentsAsync(parser, room, ct);

	/// <summary>
	/// PennMUSH <c>clear_player()</c>: hand everything the player still owns to the probate player,
	/// then do the <c>clear_thing()</c> work.
	/// </summary>
	/// <remarks>
	/// <c>@destroy</c> already chowned or marked these at pre-destroy time
	/// (<c>HandlePlayerPossessionsAsync</c>). Repeating it here is not redundant: possessions marked
	/// <c>GOING</c> are deliberately left owned by the doomed player until they are purged, and
	/// deleting the player would sever their ownership edge and make every later read of them throw.
	/// PennMUSH has the same split and resolves it the same way — the probate judge exists for this.
	/// </remarks>
	private async ValueTask ClearPlayerAsync(IMUSHCodeParser parser, SharpPlayer player, CancellationToken ct)
	{
		var probate = await ResolveProbatePlayerAsync(ct);
		if (probate is not null)
		{
			var playerDbRefNumber = player.Object.DBRef.Number;

			await foreach (var channel in mediator.CreateStream(new GetChannelListQuery(), ct))
			{
				var channelOwner = await channel.Owner.WithCancellation(ct);
				// A channel nobody owns is not this player's to hand on.
				if (channelOwner?.Object.DBRef.Number != playerDbRefNumber) continue;

				await mediator.Send(new UpdateChannelOwnerCommand(channel, probate), ct);
			}

			await foreach (var owned in mediator.CreateStream(new GetAllTypedObjectsQuery(), ct))
			{
				if (owned.Object().DBRef.Number == playerDbRefNumber) continue;

				var owner = await owned.Object().Owner.WithCancellation(ct);
				if (owner.Object.DBRef.Number != playerDbRefNumber) continue;

				await mediator.Send(new SetObjectOwnerCommand(owned, probate), ct);
			}

			await mediator.Send(new ReassignAttributeOwnerCommand(player, probate), ct);
		}

		await EmptyContentsAsync(parser, player, ct);
	}

	/// <summary>
	/// PennMUSH <c>empty_contents()</c>: warn everyone inside, destroy any exits being carried, and
	/// send everything else home — to <c>default_home</c> when its own home is missing, is the
	/// container being destroyed, or is itself an exit.
	/// </summary>
	private async ValueTask EmptyContentsAsync(IMUSHCodeParser parser, AnySharpContainer container,
		CancellationToken ct)
	{
		var containerDbRefNumber = container.Object().DBRef.Number;
		var contents = await mediator.CreateStream(new GetContentsQuery(container), ct).ToListAsync(ct);

		foreach (var content in contents)
		{
			await notifyService.NotifyLocalized(content.Object().DBRef,
				nameof(ErrorMessages.Notifications.FloorDisappearsNothingness), sender: null);
		}

		foreach (var content in contents)
		{
			if (content.IsExit)
			{
				// An exit cannot be sent anywhere — PennMUSH frees exits found in contents outright.
				await FreeObjectAsync(parser, content.AsExit, ct);
				continue;
			}

			var destination = await ResolveEvacuationTargetAsync(content, containerDbRefNumber, ct);
			if (destination is null) continue;

			await moveService.ExecuteMoveAsync(parser, content, destination, cause: "container destroyed",
				silent: true);
		}
	}

	/// <summary>
	/// Where a piece of content goes when the thing holding it is destroyed: its home, or
	/// <c>default_home</c> when that home is unusable.
	/// </summary>
	private async ValueTask<AnySharpContainer?> ResolveEvacuationTargetAsync(AnySharpContent content,
		int containerDbRefNumber, CancellationToken ct)
	{
		var home = await content.Home();

		if (!home.IsNone)
		{
			var candidate = home.WithoutNone();
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
		var dependents = await mediator.CreateStream(new GetHomedAtQuery(dbref), ct).ToListAsync(ct);

		// Exits are handled by RelinkEntrancesAsync — for an exit the home edge is its destination.
		var homeless = dependents.Where(dependent => !dependent.IsExit).ToList();
		if (homeless.Count == 0) return;

		var defaultHome = await ResolveDefaultHomeAsync(ct);
		if (defaultHome is null)
		{
			logger.LogError(
				"default_home (#{DefaultHome}) is not a valid container; {Count} object(s) homed at "
				+ "#{DbRef} will be left without a home.",
				configuration.CurrentValue.Database.DefaultHome, homeless.Count, dbref.Number);
			return;
		}

		foreach (var dependent in homeless)
		{
			await mediator.Send(new SetObjectHomeCommand(dependent, defaultHome), ct);
		}
	}

	private async ValueTask<AnySharpContainer?> ResolveDefaultHomeAsync(CancellationToken ct)
	{
		var configured = new DBRef((int)configuration.CurrentValue.Database.DefaultHome);
		var node = await mediator.Send(new GetObjectNodeQuery(configured), ct);

		return !node.IsNone && node.Known.IsContainer ? node.Known.AsContainer : null;
	}

	private async ValueTask<SharpPlayer?> ResolveProbatePlayerAsync(CancellationToken ct)
	{
		var configured = new DBRef((int)configuration.CurrentValue.Command.ProbateJudge);
		var node = await mediator.Send(new GetObjectNodeQuery(configured), ct);

		if (!node.IsNone && node.Known.IsPlayer)
		{
			return node.Known.AsPlayer;
		}

		logger.LogWarning(
			"probate_judge config option (#{ProbateDbRef}) is set to an invalid object; falling back to God (#1).",
			configured.Number);

		var god = await mediator.Send(new GetObjectNodeQuery(new DBRef(GodDbRefNumber)), ct);
		if (!god.IsNone && god.Known.IsPlayer)
		{
			return god.Known.AsPlayer;
		}

		logger.LogError("God (#1) is not a valid player; possessions cannot be handed to a probate player.");
		return null;
	}
}
