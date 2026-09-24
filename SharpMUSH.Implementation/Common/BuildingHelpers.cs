using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// The building work <c>@create</c> and <c>create()</c> share.
/// </summary>
/// <remarks>
/// PennMUSH's <c>fun_create</c> is a one-line call to <c>do_create</c> (<c>src/fundb.c</c>), so
/// every step of the command is a step of the function: the default-home check, the name
/// validation, the object landing in the creator's inventory, the zone inherited from the creator,
/// the report, the <c>OBJECT`CREATE</c> event and the C# object-lifecycle hook. Written out twice,
/// the function had none of the last four and put the new object in the creator's <em>room</em>.
/// </remarks>
public static class BuildingHelpers
{
	/// <inheritdoc cref="BuildingHelpers"/>
	public static async ValueTask<Result<DBRef>> CreateThingAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		IObjectStore database,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IValidateService validateService,
		INotifyService notifyService,
		IEventService eventService,
		IPermissionService permissionService,
		AnySharpObject executor,
		MString name,
		MString? requestedDbref = null)
	{
		if (await HomeForNewObjectAsync(mediator, configuration, permissionService, executor) is not AnySharpContainer home)
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.DefaultHomeLocationInvalid), executor);
			return new Error<string>(ErrorMessages.Returns.NotARoom);
		}

		if (!await validateService.Valid(IValidateService.ValidationType.Name, name, new None()))
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.InvalidNameThing), executor);
			return new Error<string>(ErrorMessages.Returns.BadObjectName);
		}

		// PennMUSH do_create hands the new object to the executor (src/create.c). An exit cannot
		// hold anything — AnySharpObject.AsContainer throws for one — so code owned by an exit
		// builds into the room the exit is in. @CREATE threw outright in that case; create() had
		// the fallback and lost it when the two were merged.
		var into = executor.IsContainer ? executor.AsContainer : await executor.Where();
		var owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);

		if (Given(requestedDbref) is not { } requested)
		{
			return await WithBuildingQuotaAsync(mediator, configuration, notifyService, executor,
					async () => await mediator.Send(new CreateThingCommand(name.ToPlainText(), into, owner, home))) switch
			{
				DBRef thing => await CreatedAsync(parser, mediator, database, notifyService, eventService, executor,
					name, thing),
				Error<string> refused => refused
			};
		}

		// create.c:561 then :565 — the dbref is settled before can_pay_fees is asked for a slot.
		return await RequestedDbrefAsync(notifyService, executor, requested) switch
		{
			DBRef wanted => await ThingAtAsync(parser, mediator, database, configuration, notifyService, eventService,
				executor, name, into, owner, home, wanted),
			Error<string> refused => refused
		};
	}

	/// <summary>The rest of <see cref="CreateThingAsync"/> once a requested dbref has passed its gate.</summary>
	private static async ValueTask<Result<DBRef>> ThingAtAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		IObjectStore database,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		INotifyService notifyService,
		IEventService eventService,
		AnySharpObject executor,
		MString name,
		AnySharpContainer into,
		SharpPlayer owner,
		AnySharpContainer home,
		DBRef wanted)
	{
		return await ChargedAtAsync(mediator, configuration, notifyService, executor,
			async () => await mediator.Send(new CreateThingAtCommand(wanted, name.ToPlainText(), into, owner, home))) switch
		{
			DBRef at => await CreatedAsync(parser, mediator, database, notifyService, eventService, executor, name, at),
			Error<string> refused => refused
		};
	}

	/// <summary>
	/// Everything <c>do_create</c> does once the object exists, whichever dbref it landed on: the
	/// creator's zone, the report, the <c>OBJECT`CREATE</c> event and the object-lifecycle hook.
	/// </summary>
	private static async ValueTask<Result<DBRef>> CreatedAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		IObjectStore database,
		INotifyService notifyService,
		IEventService eventService,
		AnySharpObject executor,
		MString name,
		DBRef thing)
	{
		// A new object inherits its creator's zone, once the cycle guard allows it.
		if (await executor.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject zone &&
			await mediator.Send(new GetObjectNodeQuery(thing)) is AnySharpObject created &&
			await HelperFunctions.SafeToAddZone(mediator, database, created, zone))
		{
			await mediator.Send(new SetObjectZoneCommand(created, zone));
		}

		await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Created), executor, name, thing);

		await eventService.TriggerEventAsync(parser, "OBJECT`CREATE", executor.Object().DBRef,
			thing.ToString(),
			""); // null for cloned-from (not a clone)

		// Phase 2b: C# object-lifecycle hooks fire alongside the softcode OBJECT`CREATE event.
		if (parser.ServiceProvider.GetService<IPluginHookDispatcher>() is { } createHooks)
		{
			await createHooks.ObjectCreatedAsync(thing, executor.Object().DBRef);
		}

		return thing;
	}

	/// <summary>
	/// The whole of PennMUSH's <c>do_dig</c> (<c>src/create.c:466-522</c>): a room, optionally an exit
	/// to it from where the digger stands and an exit back, each charged on its own and each able to
	/// stop the dig where the quota runs out without taking back what was already paid for.
	/// <para><c>fun_dig</c> hands <c>args</c> straight to <c>do_dig</c> (<c>src/fundb.c:2177-2189</c>),
	/// so <c>dig()</c> is this same body and not the thinner copy it used to be — it opened no exits at
	/// all.</para>
	/// </summary>
	/// <remarks>
	/// <c>@dig/teleport</c> (<c>create.c:518-521</c>) is not here: Penn re-runs the whole of
	/// <c>@teleport</c> so NO_TEL and Z_TEL still apply, which belongs with the teleport seam rather
	/// than with the digging.
	/// </remarks>
	public static async ValueTask<Result<DBRef>> DigAsync(
		IMediator mediator,
		IObjectStore database,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		AnySharpObject executor,
		MString roomName,
		MString? exitTo,
		MString? exitFrom,
		MString? roomDbref,
		MString? toDbref,
		MString? fromDbref)
	{
		if (string.IsNullOrWhiteSpace(roomName.ToPlainText()))
		{
			await notifyService.NotifyLocalized(executor.Object().DBRef, nameof(ErrorMessages.Notifications.DigWhat),
				executor);
			return new Error<string>(ErrorMessages.Returns.NoRoomNameSpecified);
		}

		// create.c:480-490 settles all three requested dbrefs before new_object(), so one that cannot be
		// honoured digs nothing at all rather than leaving a room behind.
		return await WithRequestedDbrefsAsync(mediator, notifyService, executor,
			[roomDbref, toDbref, fromDbref],
			async at => await DugAsync(mediator, database, configuration, notifyService, permissionService,
				lockService, executor, roomName, exitTo, exitFrom, at[0], at[1], at[2]));
	}

	/// <summary>The digging itself, once every requested dbref has passed its gate.</summary>
	private static async ValueTask<Result<DBRef>> DugAsync(
		IMediator mediator,
		IObjectStore database,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		AnySharpObject executor,
		MString roomName,
		MString? exitTo,
		MString? exitFrom,
		DBRef? roomAt,
		DBRef? toAt,
		DBRef? fromAt)
	{
		var owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);

		// create.c:480 — do_dig charges the room before new_object(), and each exit below is charged
		// again on its own inside do_real_open (:130). An exhausted quota and a dbref the provider
		// would not give up are different answers, so the one actually handed back is the one reported.
		return await RoomChargedAsync(mediator, configuration, notifyService, executor, roomName.ToPlainText(),
			owner, roomAt) switch
		{
			DBRef dug => await RoomDugAsync(mediator, database, configuration, notifyService, permissionService,
				lockService, executor, roomName, exitTo, exitFrom, dug, toAt, fromAt),
			Error<string> refused => refused
		};
	}

	/// <summary>
	/// The zone, the report and the two exits, once the room exists — <c>do_dig</c> from
	/// <c>create.c:492</c> onwards. The answer is the room either way, as it is in Penn: an exit the
	/// quota or the permission check turns down stops the dig and leaves what was already paid for.
	/// </summary>
	private static async ValueTask<Result<DBRef>> RoomDugAsync(
		IMediator mediator,
		IObjectStore database,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		AnySharpObject executor,
		MString roomName,
		MString? exitTo,
		MString? exitFrom,
		DBRef dug,
		DBRef? toAt,
		DBRef? fromAt)
	{
		await notifyService.NotifyLocalized(executor.Object().DBRef,
			nameof(ErrorMessages.Notifications.RoomCreatedWithNumberFormat), executor, roomName, dug.Number);

		if (await mediator.Send(new GetObjectNodeQuery(dug)) is not (AnySharpObject and SharpRoom room))
		{
			throw new InvalidOperationException("The room just dug must exist.");
		}

		// Zone(room) = Zone(player) (create.c:494), once the cycle guard allows it.
		if (await executor.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject zone
			&& await HelperFunctions.SafeToAddZone(mediator, database, room, zone))
		{
			await mediator.Send(new SetObjectZoneCommand(room, zone));
		}

		var where = await executor.Where();

		// create.c:507-517 — the exit to the new room is sourced where the digger stands, and the exit
		// back is sourced in the new room and linked to "here", which parse_linkable_room resolves to
		// speech_loc(player). An exit that is refused stops the dig, and the room stays.
		if (Given(exitTo) is not null
			&& !await DugExitAsync(mediator, database, configuration, notifyService, permissionService, lockService,
				executor, exitTo!, where, room, toAt))
		{
			return dug;
		}

		if (Given(exitFrom) is not null)
		{
			await DugExitAsync(mediator, database, configuration, notifyService, permissionService, lockService,
				executor, exitFrom!, room, where, fromAt);
		}

		return dug;
	}

	/// <summary>
	/// One of <c>do_dig</c>'s two exits. It is a <c>do_real_open</c> like any other
	/// (<c>create.c:507</c>, <c>:518</c>), so it is held to <c>can_open_from</c> on its source and
	/// <c>can_link_to</c> on its destination (<c>parse_linkable_room</c>, <c>create.c:61</c>) — which
	/// <c>@dig</c> never asked, and which matters more now that <c>dig()</c> reaches the same code from
	/// softcode.
	/// </summary>
	private static async ValueTask<bool> DugExitAsync(
		IMediator mediator,
		IObjectStore database,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		AnySharpObject executor,
		MString exitName,
		AnySharpContainer from,
		AnySharpContainer to,
		DBRef? requestedDbref)
	{
		if (await OpenExitAsync(mediator, database, configuration, notifyService, permissionService, lockService,
				executor, exitName, from, requestedDbref) is not DBRef opened)
		{
			return false;
		}

		await notifyService.NotifyLocalized(executor.Object().DBRef, nameof(ErrorMessages.Notifications.TryingToLink),
			executor);

		// parse_linkable_room refuses a destination the digger may not link into and leaves the exit
		// unlinked, exactly as do_real_open's own link step does (create.c:165-171).
		if (!await permissionService.CanLinkToAsync(executor, to.WithExitOption()))
		{
			await notifyService.NotifyLocalized(executor.Object().DBRef,
				nameof(ErrorMessages.Notifications.CantLinkToThat), executor);
			return true;
		}

		if (await mediator.Send(new GetObjectNodeQuery(opened)) is not (AnySharpObject and SharpExit exit))
		{
			throw new InvalidOperationException("The exit just opened must exist.");
		}

		await mediator.Send(new LinkExitCommand(exit, to));
		await notifyService.NotifyLocalized(executor.Object().DBRef,
			nameof(ErrorMessages.Notifications.LinkedExitToRoom), executor, opened.Number, to.Object().DBRef.Number);

		return true;
	}

	/// <summary>
	/// PennMUSH <c>can_open_from</c> (<c>hdrs/mushdb.h:94</c>): may <paramref name="player"/> source an
	/// exit in <paramref name="room"/>? Control, wizardry, royalty and the <c>Open_Anywhere</c> power
	/// each say yes outright; otherwise the room has to be OPEN_OK and its <c>@lock/open</c> has to
	/// pass. A guest may source one nowhere, and neither may anybody in something that is not a room.
	/// </summary>
	/// <remarks>
	/// <c>Commands.CanOpenFrom</c> (<c>MovementCommands.cs</c>) is the same rule for <c>@teleport</c>'s
	/// exit relocation (<c>wiz.c:469</c>); the building path keeps its copy here because
	/// <c>Functions</c> is a different partial class and <c>open()</c> is held to the same standard as
	/// <c>@open</c>.
	/// </remarks>
	public static async ValueTask<bool> CanOpenFromAsync(
		IPermissionService permissionService,
		ILockService lockService,
		AnySharpObject player,
		AnySharpContainer room)
	{
		if (!room.IsRoom || await player.IsGuest())
		{
			return false;
		}

		var roomObject = room.WithExitOption();

		if (await permissionService.Controls(player, roomObject)
				|| await player.IsWizard()
				|| await player.IsRoyalty()
				|| await player.HasPower("Open_Anywhere"))
		{
			return true;
		}

		return await roomObject.HasFlag("OPEN_OK")
			&& await lockService.Evaluate(LockType.Open, roomObject, player);
	}

	/// <summary>
	/// PennMUSH's <c>do_real_open</c> (<c>src/create.c:96-180</c>) up to but not including the link:
	/// the source has to be a room (<c>:108-110</c>) the executor may open in (<c>:127</c>), a quota
	/// slot has to be payable (<c>:130</c>), the new exit inherits the executor's zone, and the opener
	/// is told which dbref it got.
	/// </summary>
	/// <remarks>
	/// The link itself stays with the callers: <c>@open</c> and <c>open()</c> resolve their destination
	/// through <see cref="ILocateService"/> with different reporting, and <c>@open</c> then reuses the
	/// destination as the second exit's source room (<c>create.c:236</c>).
	/// </remarks>
	public static async ValueTask<Result<DBRef>> OpenExitAsync(
		IMediator mediator,
		IObjectStore database,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		AnySharpObject executor,
		MString exitName,
		AnySharpContainer sourceRoom,
		DBRef? requestedDbref = null,
		long? modified = null)
	{
		if (!sourceRoom.IsRoom)
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ExitsOnlyFromRooms), executor);
			return new Error<string>(ErrorMessages.Returns.NotARoom);
		}

		// create.c:127 then :130 — who may open here is settled before a slot is charged for it.
		if (!await CanOpenFromAsync(permissionService, lockService, executor, sourceRoom))
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new Error<string>(ErrorMessages.Returns.PermissionDenied);
		}

		var parts = exitName.ToPlainText().Split(';');
		var owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);

		return await ExitChargedAsync(mediator, configuration, notifyService, executor, parts[0], parts[1..],
			sourceRoom, owner, requestedDbref, modified) switch
		{
			DBRef opened => await OpenedAsync(mediator, database, notifyService, executor, opened),
			Error<string> refused => refused
		};
	}

	/// <summary>
	/// Unwraps the two unions a build at a requested dbref produces — the quota's refusal outside, the
	/// provider's refusal to give up the id inside — into the one a caller cares about.
	/// </summary>
	private static async ValueTask<Result<DBRef>> ChargedAtAsync(
		IMediator mediator,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		INotifyService notifyService,
		AnySharpObject executor,
		Func<ValueTask<Result<DBRef>>> build)
	{
		switch (await WithBuildingQuotaAsync(mediator, configuration, notifyService, executor, build))
		{
			case Result<DBRef> and DBRef created:
				return created;
			case Error<string> refused:
				return refused;
			default:
				await notifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.CreateDbrefUnavailable), executor);
				return new Error<string>(ErrorMessages.Returns.InvalidDbref);
		}
	}

	/// <summary>A room, at the dbref asked for or at the next one the counter hands out, charged either way.</summary>
	private static ValueTask<Result<DBRef>> RoomChargedAsync(
		IMediator mediator,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		INotifyService notifyService,
		AnySharpObject executor,
		string name,
		SharpPlayer owner,
		DBRef? requestedDbref,
		long? modified = null)
		=> requestedDbref is { } wanted
			? ChargedAtAsync(mediator, configuration, notifyService, executor,
				async () => await mediator.Send(new CreateRoomAtCommand(wanted, name, owner, modified)))
			: WithBuildingQuotaAsync(mediator, configuration, notifyService, executor,
				async () => await mediator.Send(new CreateRoomCommand(name, owner, ModifiedTime: modified)));

	/// <summary>An exit, at the dbref asked for or at the next one the counter hands out, charged either way.</summary>
	private static ValueTask<Result<DBRef>> ExitChargedAsync(
		IMediator mediator,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		INotifyService notifyService,
		AnySharpObject executor,
		string name,
		string[] aliases,
		AnySharpContainer where,
		SharpPlayer owner,
		DBRef? requestedDbref,
		long? modified = null)
		=> requestedDbref is { } wanted
			? ChargedAtAsync(mediator, configuration, notifyService, executor,
				async () => await mediator.Send(new CreateExitAtCommand(wanted, name, aliases, where, owner, modified)))
			: WithBuildingQuotaAsync(mediator, configuration, notifyService, executor,
				async () => await mediator.Send(new CreateExitCommand(name, aliases, where, owner, ModifiedTime: modified)));

	/// <summary>A thing, at the dbref asked for or at the next one the counter hands out, charged either way.</summary>
	private static ValueTask<Result<DBRef>> ThingChargedAsync(
		IMediator mediator,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		INotifyService notifyService,
		AnySharpObject executor,
		string name,
		AnySharpContainer into,
		SharpPlayer owner,
		AnySharpContainer home,
		DBRef? requestedDbref,
		long? modified = null)
		=> requestedDbref is { } wanted
			? ChargedAtAsync(mediator, configuration, notifyService, executor,
				async () => await mediator.Send(new CreateThingAtCommand(wanted, name, into, owner, home, modified)))
			: WithBuildingQuotaAsync(mediator, configuration, notifyService, executor,
				async () => await mediator.Send(new CreateThingCommand(name, into, owner, home, ModifiedTime: modified)));

	/// <summary>The bookkeeping every freshly opened exit gets: the opener's zone, and the report.</summary>
	private static async ValueTask<Result<DBRef>> OpenedAsync(
		IMediator mediator,
		IObjectStore database,
		INotifyService notifyService,
		AnySharpObject executor,
		DBRef exit)
	{
		if (await executor.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject zone &&
			await mediator.Send(new GetObjectNodeQuery(exit)) is AnySharpObject opened &&
			await HelperFunctions.SafeToAddZone(mediator, database, opened, zone))
		{
			await mediator.Send(new SetObjectZoneCommand(opened, zone));
		}

		await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.OpenedExit), executor,
			$"#{exit.Number}");

		return exit;
	}

	/// <summary>
	/// One in-flight admission per owner. The count a build is measured against and the build it
	/// admits have to be taken together, or two callers spending an owner's last slot both see it
	/// free. PennMUSH needs no equivalent because it is single-threaded; SharpMUSH is single-process
	/// (<c>docs/design/engine-data-trunk.md</c>) but not single-threaded, so the serialization is per
	/// owner rather than global. The set is bounded by the number of players who have ever built.
	/// </summary>
	private static readonly ConcurrentDictionary<int, SemaphoreSlim> QuotaGates = new();

	/// <summary>
	/// PennMUSH's <c>can_pay_fees</c> (<c>src/predicat.c:435-463</c>) around one object's creation:
	/// guests may not build at all, and otherwise <c>pay_quota</c> (<c>:601-613</c>) must have a slot
	/// to charge. <paramref name="build"/> runs only if it does, so a refusal leaves nothing behind —
	/// Penn returns NOTHING from <c>do_create</c> rather than rolling anything back.
	/// <para>Every object is charged separately, as in Penn: <c>do_dig</c> charges the room
	/// (<c>create.c:480</c>) and each exit is charged again inside <c>do_real_open</c>
	/// (<c>:130</c>), so a dig that runs out mid-way keeps what it had already paid for.</para>
	/// <para>The money half of <c>can_pay_fees</c> is deliberately absent: SharpMUSH tracks no
	/// pennies. The database-size ceiling is absent for the same reason — there is no
	/// <c>max_dbref</c> option to bound it with.</para>
	/// </summary>
	public static async ValueTask<Result<T>> WithBuildingQuotaAsync<T>(
		IMediator mediator,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		INotifyService notifyService,
		AnySharpObject executor,
		Func<ValueTask<T>> build)
	{
		if (await executor.IsGuest())
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GuestCantBuild), executor);
			return new Error<string>(ErrorMessages.Returns.PermissionDenied);
		}

		var owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);
		var gate = QuotaGates.GetOrAdd(owner.Object.DBRef.Number, _ => new SemaphoreSlim(1, 1));

		await gate.WaitAsync();
		try
		{
			if (!await AdmitsBuildAsync(mediator, configuration, executor, owner))
			{
				await notifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.BuildingQuotaExhausted), executor);
				return new Error<string>(ErrorMessages.Returns.BuildingQuotaExhausted);
			}

			return await build();
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>
	/// <c>pay_quota</c> (<c>src/predicat.c:601-613</c>): <c>USE_QUOTA &amp;&amp; !NoQuota(who) &amp;&amp;
	/// (curr - cost &lt; 0)</c> is the refusal, so the system being off and the holder being exempt
	/// each let anything through.
	/// <para>Penn's <c>curr</c> is <c>get_current_quota</c>, the RQUOTA attribute it debits on every
	/// build and credits back on destruction (<c>destroy.c:642</c>) and <c>@chown</c>
	/// (<c>set.c:235</c>). SharpMUSH stores the limit instead and subtracts what the owner holds, which
	/// is Penn's own fallback when RQUOTA is missing (<c>predicat.c:561-572</c>). The two admit the
	/// same builds, and the derived form cannot drift out of step with reality, so nothing has to
	/// refund it.</para>
	/// </summary>
	private static async ValueTask<bool> AdmitsBuildAsync(
		IMediator mediator,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		AnySharpObject executor,
		SharpPlayer owner)
	{
		if (!configuration.CurrentValue.Limit.UseQuota || await NoQuotaAsync(executor, owner))
		{
			return true;
		}

		var cost = (int)configuration.CurrentValue.Cost.QuotaCost;
		var owned = await mediator.Send(new GetOwnedObjectCountQuery(owner));

		return owner.Quota - owned >= cost;
	}

	/// <summary>
	/// <c>NoQuota(x)</c> (<c>hdrs/mushdb.h:44-47</c>): privileged, or owned by someone privileged, or
	/// holding No_Quota, or — unless MISTRUSTed, which is what stops an object borrowing its owner's
	/// exemption — owned by a holder of it.
	/// </summary>
	private static async ValueTask<bool> NoQuotaAsync(AnySharpObject executor, SharpPlayer owner)
	{
		var ownerObject = new AnySharpObject(owner);

		return await executor.IsPriv()
			|| await ownerObject.IsPriv()
			|| await executor.HasPower("No_Quota")
			|| (!await executor.HasFlag("MISTRUST") && await ownerObject.HasPower("No_Quota"));
	}

	/// <summary>
	/// The whole of PennMUSH's <c>do_clone</c> (<c>src/create.c:679-812</c>) and the
	/// <c>clone_object</c> (<c>:614-668</c>) it calls. <c>fun_clone</c> is one line of <c>do_clone</c>
	/// (<c>src/fundb.c</c>), so <c>@clone</c> and <c>clone()</c> are the same body with two different
	/// ways of naming the target; written out twice, the function was the weaker of the two and the
	/// command was missing what neither had.
	/// </summary>
	/// <remarks>
	/// Two places this deliberately parts company with Penn's implementation, both because the
	/// difference there is an artifact of which C function does the work rather than a contract:
	/// <list type="bullet">
	/// <item><c>OBJECT`CREATE</c> names the cloned-from object for every type. Penn's exit branch
	/// reaches the event through <c>do_real_open</c>, which knows nothing about cloning and passes the
	/// new exit alone (<c>:177</c>), while the thing and room branches pass both (<c>:663</c>).</item>
	/// <item><c>ACLONE</c> is <em>not</em> queued for an exit, which does match Penn: its branch returns
	/// at <c>:806</c> without ever reaching a <c>real_did_it</c>, unlike <c>:727</c> and <c>:742</c>.
	/// Queueing one there would be an invention.</item>
	/// </list>
	/// </remarks>
	public static async ValueTask<Result<DBRef>> CloneAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		IObjectStore database,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		IAttributeService attributeService,
		IManipulateSharpObjectService manipulateSharpObjectService,
		IDidItService didItService,
		IEventService eventService,
		ILogger? logger,
		AnySharpObject executor,
		AnySharpObject target,
		MString? newName,
		bool preserve,
		MString? requestedDbref = null)
	{
		if (!await permissionService.Controls(executor, target))
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new Error<string>(ErrorMessages.Returns.PermissionDenied);
		}

		if (target.IsPlayer)
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotClonePlayers), executor);
			return new Error<string>(ErrorMessages.Returns.InvalidObjectType);
		}

		// create.c:711-714. Penn refuses the switch itself rather than quietly downgrading to a plain
		// clone, and refuses it before anything is built. Being allowed to set WIZARD is not the same
		// permission as being allowed to carry one across on someone else's object, which is why this
		// gate is wizardry and not whatever the flag setter would have accepted.
		if (preserve && !await executor.IsWizard())
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ClonePreserveWizardOnly), executor);
			return new Error<string>(ErrorMessages.Returns.PermissionDenied);
		}

		// cmd_clone passes args_right[2] as newdbref (cmds.c:382-386), and fun_clone args[2]
		// (fundb.c:2192-2212); both reach make_first_free_wrapper before do_clone builds anything.
		DBRef? cloneAt = null;
		if (Given(requestedDbref) is { } requested)
		{
			switch (await RequestedDbrefAsync(notifyService, executor, requested))
			{
				case DBRef at:
					cloneAt = at;
					break;
				case Error<string> refused:
					return refused;
			}
		}

		var name = newName?.ToPlainText() is { } given && !string.IsNullOrWhiteSpace(given)
			? given
			: target.Object().Name;
		var owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);

		// create.c:728-731 for a thing or a room — `if (IsRoom(player)) moveto(clone, player) else
		// moveto(clone, Location(player))` — and create.c:771-773 for an exit, whose do_real_open is
		// handed a pseudo of NOTHING and so sources from speech_loc(player) (create.c:97; speech.c:109:
		// a room is itself, an exit is its source, anything else is its location). The two agree, so one
		// expression serves both. @create's rule — hand it to the executor whenever the executor can hold
		// something — is the wrong one here: it is true of every player, so a player's clone landed in
		// their own inventory instead of beside the original.
		var into = executor.IsRoom ? executor.AsContainer : await executor.Where();

		// "We give the clone the same modification time that its other clone has, but update the
		// creation time" (create.c:653-655). A null creation time is now; the modification time is the
		// original's.
		var modified = target.Object().ModifiedTime;

		// create.c:725, :741 — each type's branch asks can_pay_fees before it clones anything, and the
		// exit branch reaches do_real_open, which asks for itself (:130).
		return await CreateCloneAsync(mediator, database, configuration, notifyService, permissionService, lockService,
			executor, target, name, into, owner, modified, cloneAt) switch
		{
			DBRef cloneDbRef => await ClonedAsync(parser, mediator, notifyService, attributeService,
				manipulateSharpObjectService, didItService, eventService, logger, executor, target, owner, preserve,
				cloneDbRef),
			// A guest refusal, an exhausted quota and a dbref the provider would not give up are three
			// different answers; the caller is handed the one it was actually given.
			Error<string> refused => refused
		};
	}

	/// <summary>Everything <c>do_clone</c> carries across once the clone itself exists.</summary>
	private static async ValueTask<Result<DBRef>> ClonedAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		INotifyService notifyService,
		IAttributeService attributeService,
		IManipulateSharpObjectService manipulateSharpObjectService,
		IDidItService didItService,
		IEventService eventService,
		ILogger? logger,
		AnySharpObject executor,
		AnySharpObject target,
		SharpPlayer owner,
		bool preserve,
		DBRef cloneDbRef)
	{
		if (await mediator.Send(new GetObjectNodeQuery(cloneDbRef)) is not AnySharpObject clonedObj)
		{
			throw new InvalidOperationException("The clone just created must exist.");
		}

		await CopyAttributesAsync(mediator, attributeService, logger, executor, target, clonedObj, owner);

		foreach (var (lockName, data) in target.Object().Locks)
		{
			if (data.Flags.HasFlag(Library.Services.LockService.LockFlags.NoClone)) continue;
			var copied = await mediator.Send(new CopyLockCommand(target.Object(), clonedObj.Object(), lockName, executor));
			if (copied is Error<string> failure)
			{
				await notifyService.Notify(executor, $"Unable to clone {lockName} lock: {failure.Value}", executor);
			}
		}

		await CopyFlagsAsync(manipulateSharpObjectService, executor, target, clonedObj, preserve);
		await CopyPrivilegesAsync(mediator, manipulateSharpObjectService, notifyService, executor, target, clonedObj,
			preserve);

		// create.c:636-637 — the clone inherits the original's zone and parent, and not the cloner's.
		if (await target.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject zone)
		{
			await mediator.Send(new SetObjectZoneCommand(clonedObj, zone));
		}

		if (await target.Object().Parent.WithCancellation(CancellationToken.None) is AnySharpObject parent)
		{
			await mediator.Send(new SetObjectParentCommand(clonedObj, parent));
		}

		await eventService.TriggerEventAsync(parser, "OBJECT`CREATE", executor.Object().DBRef,
			cloneDbRef.ToString(),
			target.Object().DBRef.ToString()); // cloned-from

		if (parser.ServiceProvider.GetService<IPluginHookDispatcher>() is { } createHooks)
		{
			await createHooks.ObjectCreatedAsync(cloneDbRef, executor.Object().DBRef);
		}

		await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ClonedNewObjectFormat),
			executor, cloneDbRef.Number);

		// real_did_it(player, clone, NULL, NULL, NULL, NULL, "ACLONE", …) — create.c:727 and :742. The
		// attribute has just been copied onto the clone, and it runs there with the cloner as enactor.
		// See the remarks above for why an exit gets none.
		if (!target.IsExit)
		{
			await didItService.DidIt(parser, new DidItRequest(executor, clonedObj, AWhat: "ACLONE"));
		}

		return cloneDbRef;
	}

	/// <summary>
	/// The object itself, per type. A thing keeps the original's home (<c>create.c:657</c>); a room
	/// arrives with no exits and no drop-to, which <c>create.c:656</c> clears and a fresh room does not
	/// have; an exit is re-opened onto the original's destination, which is what <c>do_real_open</c>
	/// does for <c>do_clone</c> at <c>create.c:765-780</c> and what SharpMUSH left out, so a cloned
	/// exit led nowhere. An unlinked original still clones to an unlinked exit.
	/// </summary>
	private static async ValueTask<Result<DBRef>> CreateCloneAsync(
		IMediator mediator,
		IObjectStore database,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		AnySharpObject executor,
		AnySharpObject target,
		string name,
		AnySharpContainer into,
		SharpPlayer owner,
		long modified,
		DBRef? requestedDbref)
	{
		switch (target)
		{
			case SharpThing thing:
				return await ThingChargedAsync(mediator, configuration, notifyService, executor, name, into, owner,
					await thing.Home.WithCancellation(CancellationToken.None), requestedDbref, modified);

			case SharpRoom:
				return await RoomChargedAsync(mediator, configuration, notifyService, executor, name, owner,
					requestedDbref, modified);

			case SharpExit exit:
				// create.c:771-773 — the exit branch is a do_real_open, so it is held to everything one
				// is: the source must be a room (:108-110), can_open_from must admit the cloner (:127),
				// and only then is a slot charged (:130). Charging the clone directly skipped the first
				// two, so a mortal could clone an exit into a room they may not open in.
				return await OpenExitAsync(mediator, database, configuration, notifyService, permissionService,
					lockService, executor, MarkupText.Plain(name), into, requestedDbref, modified) switch
				{
					DBRef cloned => await ReopenedAsync(mediator, exit, cloned),
					Error<string> refused => refused
				};

			default:
				// CloneAsync refuses a player before reaching here, and the union has no fifth case.
				throw new InvalidOperationException($"Nothing can clone a {target.GetType().Name}.");
		}
	}

	/// <summary>The cloned exit's destination, which is the original's (create.c:765-780).</summary>
	private static async ValueTask<Result<DBRef>> ReopenedAsync(IMediator mediator, SharpExit original, DBRef cloned)
	{
		if (await original.Home.WithCancellation(CancellationToken.None) is AnySharpContainer destination
			&& await mediator.Send(new GetObjectNodeQuery(cloned)) is AnySharpObject and SharpExit clonedExit)
		{
			await mediator.Send(new LinkExitCommand(clonedExit, destination));
		}

		return cloned;
	}

	/// <summary>
	/// Penn's <c>atr_cpy</c> (<c>attrib.c:1692-1710</c>) walks the source's flat, sorted attribute
	/// list — branch vs. leaf is purely a naming convention over one namespace — and for each attribute
	/// checks AF_Nocopy, then calls <c>atr_new_add(..., makeroots: false)</c>. With makeroots false,
	/// <c>atr_new_add</c> (<c>attrib.c:756-820</c>) silently aborts without adding when the immediate
	/// parent isn't already on the destination (<c>:804-806</c>). Because the list is sorted with parent
	/// before child, a no_clone BRANCH is itself skipped by atr_cpy, and its leaves then find no parent
	/// on the clone either and are dropped too — incidentally, via the missing-root abort, not via any
	/// permission walk of their own. <c>GetAttributesByRegexAsync</c> (via <c>GetAttributesQuery</c> in
	/// Regex mode) is used here rather than a depth-1 enumeration (or the unsorted
	/// <c>GetAttributesAsync</c>) because it walks the whole tree and sorts LongName ascending — parent
	/// before child — which this skip-propagation depends on.
	/// </summary>
	private static async ValueTask CopyAttributesAsync(
		IMediator mediator,
		IAttributeService attributeService,
		ILogger? logger,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpObject clonedObj,
		SharpPlayer owner)
	{
		var skippedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		await foreach (var sourceAttribute in mediator.CreateStream(
			new GetAttributesQuery(target.Object().DBRef, ".*", false, IAttributeService.AttributePatternMode.Regex)))
		{
			var attr = sourceAttribute.Attribute;
			var longName = attr.LongName!;
			var attrPath = longName.Split('`');
			var lastSeparator = longName.LastIndexOf('`');
			var parentLongName = lastSeparator < 0 ? null : longName[..lastSeparator];
			var parentSkipped = parentLongName is not null && skippedAttributes.Contains(parentLongName);

			// The "_"-prefix skip is a pre-existing SharpMUSH-only filter, orthogonal to Penn's
			// no_clone. It folds into the same skip set so that a "_"-prefixed branch's children don't
			// get silently auto-vivified a stripped-down parent — the missing-root hazard the no_clone
			// propagation above exists to avoid.
			if (attr.IsNoCopy() || attr.Name.StartsWith('_') || parentSkipped)
			{
				skippedAttributes.Add(longName);
				continue;
			}

			// AL_CREATOR(ptr) is passed through unchanged in atr_cpy (attrib.c:1706) — a cloned
			// attribute keeps its original creator, not the cloner.
			var creator = await attr.Owner.WithCancellation(CancellationToken.None) ?? owner;
			var setResult = await attributeService.SetAttributeAsync(executor, clonedObj, longName, attr.Value, creator);

			// A failed set means the branch was NOT actually copied. Treating it as skipped keeps the
			// invariant this whole loop depends on: a LongName only avoids the skip set if it genuinely
			// landed on the clone. Without this, a child under a branch that failed to set would still
			// see its parent as "not skipped" and auto-vivify a stripped-down stand-in — the exact
			// hazard this propagation exists to prevent. Unreachable today (the clone's owner always
			// controls the freshly-created destination), but one permission change away from live.
			if (setResult is Error<string>)
			{
				skippedAttributes.Add(longName);
				continue;
			}

			// AL_FLAGS(ptr) is assigned directly alongside AL_CREATOR on the very same atr_new_add call
			// (attrib.c:1706-1707) — Penn copies the flags too, with no permission gate at all:
			// atr_new_add is a deliberately "dangerous", bypass-everything helper reserved for database
			// load and atr_cpy (its own doc comment, attrib.c:750-754). SetAttributeAsync only just
			// created the destination attribute with whatever SharpAttributeEntry.DefaultFlags applies
			// (AttributeService.cs, applied inside SetAttributeCommand's handler) — a SharpMUSH-only
			// mechanism Penn has no equivalent of — so the destination flag set is forced to match the
			// source's exactly, mirroring Penn's unconditional overwrite rather than a union. Goes
			// straight through SetAttributeFlagCommand/UnsetAttributeFlagCommand (no permission checks
			// in either handler) rather than AttributeService.SetAttributeFlagsAsync, for the same
			// bypass reason atr_new_add itself bypasses can_write_attr.
			var destAttribute = await mediator.CreateStream(new GetAttributeQuery(clonedObj.Object().DBRef, attrPath))
				.LastOrDefaultAsync();

			if (destAttribute is null)
			{
				// SetAttributeAsync above reported success, so the destination attribute should exist —
				// this re-fetch failing is not the "copy failed" case handled above (that one still owns
				// skippedAttributes so children don't auto-vivify a stripped parent). Here the value
				// genuinely landed; only the flag sync had nothing to attach to. Surface it instead of
				// silently leaving the clone's flags at SetAttributeAsync's defaults.
				logger?.LogWarning(
					"Clone flag sync skipped for {LongName} on {CloneDbRef}: destination attribute was not found immediately after a successful set",
					longName, clonedObj.Object().DBRef);
				continue;
			}

			var sourceFlagNames = attr.Flags.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var destFlagNames = destAttribute.Flags.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

			foreach (var flag in destAttribute.Flags.Where(f => !sourceFlagNames.Contains(f.Name)))
			{
				await mediator.Send(new UnsetAttributeFlagCommand(clonedObj.Object().DBRef, destAttribute, flag));
			}

			foreach (var flag in attr.Flags.Where(f => !destFlagNames.Contains(f.Name)))
			{
				await mediator.Send(new SetAttributeFlagCommand(clonedObj.Object().DBRef, destAttribute, flag));
			}
		}
	}

	/// <summary>
	/// <c>Flags(clone) = clone_flag_bitmask("FLAG", Flags(thing))</c> (create.c:638), with WIZARD and
	/// ROYALTY cleared again unless preserving (<c>:640-644</c>).
	/// </summary>
	/// <remarks>
	/// Synchronised to the source, not unioned with it. The clone is created through the same path as
	/// any other object and therefore arrives carrying the configured creation defaults, so copying only
	/// what the source has would leave a NO_COMMAND that the source had deliberately cleared — and the
	/// $-commands just copied onto the clone would not run. The attribute-flag sync works the same way,
	/// for the same reason.
	/// </remarks>
	private static async ValueTask CopyFlagsAsync(
		IManipulateSharpObjectService manipulateSharpObjectService,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpObject clonedObj,
		bool preserve)
	{
		var copyable = await target.Object().Flags.Value
			.Where(flag => preserve || (!flag.Name.Contains("WIZARD") && !flag.Name.Contains("ROYALTY")))
			.Select(flag => flag.Name)
			.ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

		// Materialized: the clone's flags are unset while this list is walked.
		var clonedObjectFlags = await clonedObj.Object().Flags.Value.ToArrayAsync();
		foreach (var flag in clonedObjectFlags.Where(flag => !copyable.Contains(flag.Name)))
		{
			await manipulateSharpObjectService.SetOrUnsetFlag(executor, clonedObj, $"!{flag.Name}", false);
		}

		foreach (var flagName in copyable)
		{
			await manipulateSharpObjectService.SetOrUnsetFlag(executor, clonedObj, flagName, false);
		}
	}

	/// <summary>
	/// The half of <c>clone_object</c> that <c>/PRESERVE</c> exists for (create.c:643-652): without it
	/// the clone's powers and warnings are zapped, which is what a freshly created object already has;
	/// with it they come across and the cloner is warned that they did. Only a wizard reaches this — the
	/// gate is in <see cref="CloneAsync"/> — so the copy goes through the ordinary setter rather than
	/// around it.
	/// </summary>
	private static async ValueTask CopyPrivilegesAsync(
		IMediator mediator,
		IManipulateSharpObjectService manipulateSharpObjectService,
		INotifyService notifyService,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpObject clonedObj,
		bool preserve)
	{
		if (!preserve)
		{
			return;
		}

		await foreach (var power in target.Object().Powers.Value)
		{
			await manipulateSharpObjectService.SetPower(executor, clonedObj, power.Name, false);
		}

		if (target.Object().Warnings != WarningType.None)
		{
			await mediator.Send(new SetObjectWarningsCommand(clonedObj, target.Object().Warnings));
		}

		// create.c:652-656 — the notice fires on what the clone ended up with, not on what was asked for.
		if (await clonedObj.HasFlag("WIZARD") || await clonedObj.HasFlag("ROYALTY")
			|| clonedObj.Object().Warnings != WarningType.None
			|| await clonedObj.Object().Powers.Value.AnyAsync())
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.ClonePreserveCarriedPrivileges), executor);
		}
	}

	/// <summary>An argument that was supplied and is not blank, or nothing.</summary>
	public static string? Given(MString? argument)
		=> argument?.ToPlainText() is { } text && !string.IsNullOrWhiteSpace(text) ? text : null;

	/// <summary>The argument at <paramref name="index"/>, if one was supplied and is not blank.</summary>
	public static MString? Argument(IReadOnlyDictionary<string, CallState> args, string index)
		=> args.TryGetValue(index, out var argument) && Given(argument.Message) is not null
			? argument.Message
			: null;

	/// <summary>
	/// The permission-and-parse half of PennMUSH's <c>make_first_free_wrapper</c>
	/// (<c>src/destroy.c:930-943</c>), in its own order: the power first, then whether the id could
	/// name a slot at all. Both refuse outright — Penn returns NOTHING from the command and never falls
	/// back to the next free dbref, and neither does this. Whether the id is genuinely free is the
	/// provider's to answer, inside the same transaction as the write that takes it.
	/// </summary>
	public static async ValueTask<Result<DBRef>> RequestedDbrefAsync(
		INotifyService notifyService,
		AnySharpObject executor,
		string requested)
	{
		if (!await executor.IsWizard() && !await executor.Object().HasPower("Pick_DBRefs"))
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new Error<string>(ErrorMessages.Returns.PermissionDenied);
		}

		if (ParseDbref(requested) is not { } wanted)
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.CreateDbrefUnavailable), executor);
			return new Error<string>(ErrorMessages.Returns.InvalidDbref);
		}

		return wanted;
	}

	/// <summary>
	/// Every requested dbref a command names, settled before any object is built. <c>do_dig</c>
	/// (<c>create.c:480-490</c>) pushes <c>argv[5]</c>, <c>argv[4]</c> and <c>argv[3]</c> onto the free
	/// list before <c>new_object()</c>, and a push that cannot be honoured returns NOTHING with nothing
	/// dug; <c>do_open</c> (<c>:219-226</c>) does the same for its two. A slot not asked for comes back
	/// null.
	/// </summary>
	/// <summary>
	/// Serialises builds that name their own dbrefs, so the availability check below is worth
	/// something. Only these builds can take a hole at all — <c>AllocateDbref</c> hands out the next
	/// id from the counter and <c>AllocateDbrefAt</c> takes an id below it, so a build that names
	/// nothing can never contend for one. Process-wide rather than per owner, because two different
	/// owners can name the same slot; the engine is single-process by design
	/// (<c>docs/design/engine-data-trunk.md</c>), and naming a dbref is a wizard's rare act.
	/// <para>Always taken outside <see cref="QuotaGates"/>, never the other way round.</para>
	/// </summary>
	private static readonly SemaphoreSlim RequestedDbrefGate = new(1, 1);

	/// <summary>
	/// <see cref="RequestedDbrefsAsync"/> held across the whole of <paramref name="build"/>, which is
	/// what makes the availability check binding rather than advisory for a multi-object build: no
	/// other build that names a dbref can take one of these slots between the check and the last
	/// write, so <c>@dig name=to,from,#a,#b,#c</c> either gets all three or builds nothing.
	/// </summary>
	public static async ValueTask<Result<DBRef>> WithRequestedDbrefsAsync(
		IMediator mediator,
		INotifyService notifyService,
		AnySharpObject executor,
		MString?[] requested,
		Func<DBRef?[], ValueTask<Result<DBRef>>> build)
	{
		await RequestedDbrefGate.WaitAsync();
		try
		{
			return await RequestedDbrefsAsync(mediator, notifyService, executor, requested) switch
			{
				DBRef?[] at => await build(at),
				Error<string> refused => refused
			};
		}
		finally
		{
			RequestedDbrefGate.Release();
		}
	}

	public static async ValueTask<Result<DBRef?[]>> RequestedDbrefsAsync(
		IMediator mediator,
		INotifyService notifyService,
		AnySharpObject executor,
		params MString?[] requested)
	{
		var wanted = new DBRef?[requested.Length];

		for (var i = 0; i < requested.Length; i++)
		{
			if (Given(requested[i]) is not { } text)
			{
				continue;
			}

			switch (await RequestedDbrefAsync(notifyService, executor, text))
			{
				case DBRef at:
					wanted[i] = at;
					break;
				case Error<string> refused:
					return refused;
			}
		}

		// make_first_free_wrapper asks IsGarbage and pushes the slot onto the free list before
		// new_object() runs (destroy.c:939-947), so every id a multi-object build names is settled
		// before the first object exists. Without this the room would be dug and the exit then refused.
		// Advisory: the authority is still the check inside the write that takes the id.
		for (var i = 0; i < wanted.Length; i++)
		{
			if (wanted[i] is not { } at)
			{
				continue;
			}

			// Penn's free list silently tolerates the same slot pushed twice and hands the second
			// object whatever came next instead. Building somewhere other than where you asked is the
			// whole thing a requested dbref exists to prevent, so a repeat is refused here.
			var repeated = Array.FindIndex(wanted, other => other is { } o && o.Number == at.Number) != i;

			if (repeated || !await mediator.Send(new DbrefAvailableQuery(at)))
			{
				await notifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.CreateDbrefUnavailable), executor);
				return new Error<string>(ErrorMessages.Returns.InvalidDbref);
			}
		}

		return wanted;
	}

	/// <summary>
	/// PennMUSH <c>parse_dbref</c> (<c>src/parse.c:120-138</c>) — strictly <c>#nnn</c>, because
	/// anything looser would swallow a possessive. The <c>GoodObject</c> half of its check is the
	/// provider's to answer, and <c>CreateThingAtCommand</c> answers it.
	/// </summary>
	private static DBRef? ParseDbref(string requested)
	{
		var trimmed = requested.Trim();
		return trimmed.Length > 1 && trimmed[0] == '#' && int.TryParse(trimmed[1..], out var number) && number >= 0
			? new DBRef(number)
			: null;
	}

	/// <summary>
	/// PennMUSH <c>do_create</c> (<c>src/create.c:589-597</c>) derives the new object's home from the
	/// creator and never from a configured constant:
	/// <code>
	/// if ((loc = Location(player)) != NOTHING &amp;&amp; (controls(player, loc) || Abode(loc)))
	///   Home(thing) = loc;
	/// else
	///   Home(thing) = Home(player);
	/// </code>
	/// <c>Database.DefaultHome</c> survives only as the last resort, for a creator whose own home is
	/// unset — a room without a drop-to, or an exit that has never been linked. Penn cannot reach that
	/// branch, because every one of its objects always carries a <c>home</c>.
	/// </summary>
	private static async ValueTask<AnyOptionalSharpContainer> HomeForNewObjectAsync(
		IMediator mediator,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IPermissionService permissionService,
		AnySharpObject executor)
	{
		var where = await executor.Where();
		if (await permissionService.Controls(executor, where.WithExitOption()) || await where.Object().HasFlag("ABODE"))
		{
			return new AnyOptionalSharpContainer(where);
		}

		if (await CreatorHomeAsync(executor) is AnySharpContainer own)
		{
			return new AnyOptionalSharpContainer(own);
		}

		var configured = new DBRef((int)configuration.CurrentValue.Database.DefaultHome);
		return await mediator.Send(new GetObjectNodeQuery(configured)) is AnySharpObject { IsContainer: true } fallback
			? new AnyOptionalSharpContainer(fallback.AsContainer)
			: new AnyOptionalSharpContainer(new None());
	}

	/// <summary>
	/// PennMUSH's <c>home</c> field is one slot read differently per type, and <c>Home(player)</c> at
	/// create.c:595 reads whichever applies to the creator: a player's or thing's home, an exit's
	/// destination (<c>src/db.h</c> aliases <c>Destination</c> to it), or a room's drop-to.
	/// </summary>
	private static async ValueTask<AnyOptionalSharpContainer> CreatorHomeAsync(AnySharpObject executor) => executor switch
	{
		SharpPlayer player => new AnyOptionalSharpContainer(
			await player.Home.WithCancellation(CancellationToken.None)),
		SharpThing thing => new AnyOptionalSharpContainer(
			await thing.Home.WithCancellation(CancellationToken.None)),
		SharpExit exit => await exit.Home.WithCancellation(CancellationToken.None),
		SharpRoom room => await room.Location.WithCancellation(CancellationToken.None)
	};
}
