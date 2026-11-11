using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// The zone change <c>@chzone</c> and <c>@chzoneall</c> share.
/// </summary>
/// <remarks>
/// PennMUSH's <c>do_chzoneall</c> is a loop over one player's objects calling <c>do_chzone</c> once
/// per object with <c>noisy</c> off (<c>src/wiz.c:1046-1054</c>) — "This keeps consistency on things
/// like flag resetting, etc...". SharpMUSH wrote the loop out a second time, and that copy asked
/// nothing of the destination's <c>@lock/chzone</c>, never installed <c>check_zone_lock</c>'s
/// default lock, re-zoned objects that were already in the target zone, and reported a per-owner
/// summary PennMUSH does not have.
/// </remarks>
public static class ZoneHelpers
{
	/// <inheritdoc cref="ZoneHelpers"/>
	/// <param name="target">
	/// The object being re-zoned, already matched — <c>do_chzone</c>'s <c>noisy_match_result</c>
	/// (<c>src/set.c:381</c>), which both callers perform.
	/// </param>
	/// <param name="zone">The new zone, or <see cref="None"/> for <c>@chzone … =none</c>.</param>
	/// <param name="preserve">
	/// <c>/PRESERVE</c>: keep the privileged flags and the powers the change otherwise strips. Wizard
	/// only — <c>do_chzone</c> zeroes it for everyone else (<c>src/set.c:470-471</c>).
	/// </param>
	/// <param name="noisy">
	/// <c>do_chzone</c>'s <c>noisy</c> parameter. <c>@chzoneall</c> passes it off and reports one
	/// count instead (<c>src/wiz.c:1050</c>).
	/// </param>
	public static async ValueTask<Result<Success>> ChangeZoneAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		IRelationshipCycleChecker cycleChecker,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		IDidItService didItService,
		IObjectRelationshipService objectRelationshipService,
		IFlagAndPowerService flagAndPowerService,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IConnectionService connections,
		AnySharpObject executor,
		AnySharpObject target,
		AnyOptionalSharpObject zone,
		bool preserve,
		bool noisy)
	{
		// set.c:392-396. This is also do_chzoneall's `Zone(i) != zone` filter (wiz.c:1047): the refusal
		// returns 0, so the object is not counted.
		if (await AlreadyZonedAsync(mediator, target, zone))
		{
			return await RefusedAsync(notifyService, executor, noisy, ErrorMessages.Returns.NothingToDo,
				nameof(ErrorMessages.Notifications.ObjectAlreadyInThatZone));
		}

		// set.c:398-402.
		if (!await permissionService.Controls(executor, target))
		{
			return await RefusedAsync(notifyService, executor, noisy, ErrorMessages.Returns.PermissionDenied,
				nameof(ErrorMessages.Notifications.NoPowerToShiftReality));
		}

		if (zone is not AnySharpObject destination)
		{
			// Clearing a zone skips the destination gate, the cycle walk and the strip, all of which
			// set.c guards on `zone != NOTHING` (:412, :421, :472).
			return Written(await objectRelationshipService.UnsetZone(executor, target, noisy));
		}

		return await ZoneToAsync(parser, mediator, cycleChecker, notifyService, permissionService, lockService, didItService,
			flagAndPowerService, configuration, connections, executor, target, destination, preserve, noisy,
			async () => Written(await objectRelationshipService.SetZone(executor, target, destination, noisy)));
	}

	/// <summary>
	/// <c>@chzone/add</c>: puts <paramref name="target"/> in <paramref name="zone"/> as well as the zones it is in, last.
	/// An object in no zone is zoned as <c>@chzone</c> would. The checks, the default zone lock and the strip of
	/// privileges are <c>@chzone</c>'s.
	/// </summary>
	public static async ValueTask<Result<Success>> AddZoneAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		IRelationshipCycleChecker cycleChecker,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		IDidItService didItService,
		IObjectRelationshipService objectRelationshipService,
		IFlagAndPowerService flagAndPowerService,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IConnectionService connections,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpObject zone,
		bool preserve,
		bool noisy)
	{
		var zones = await mediator.ZonesOfAsync(target);
		if (zones.Count == 0)
		{
			return await ChangeZoneAsync(parser, mediator, cycleChecker, notifyService, permissionService, lockService,
				didItService, objectRelationshipService, flagAndPowerService, configuration, connections, executor, target,
				new AnyOptionalSharpObject(zone), preserve, noisy);
		}

		if (zones.Any(held => held.Object().DBRef.Equals(zone.Object().DBRef)))
		{
			return await RefusedAsync(notifyService, executor, noisy, ErrorMessages.Returns.NothingToDo,
				nameof(ErrorMessages.Notifications.ObjectAlreadyInThatZone));
		}

		if (!await permissionService.Controls(executor, target))
		{
			return await RefusedAsync(notifyService, executor, noisy, ErrorMessages.Returns.PermissionDenied,
				nameof(ErrorMessages.Notifications.NoPowerToShiftReality));
		}

		return await ZoneToAsync(parser, mediator, cycleChecker, notifyService, permissionService, lockService, didItService,
			flagAndPowerService, configuration, connections, executor, target, zone, preserve, noisy,
			async () =>
			{
				await mediator.Send(new SetObjectZonesCommand(target, [.. zones, zone]));
				return await ZoneChangedAsync(notifyService, executor, noisy);
			});
	}

	/// <summary>
	/// <c>@chzone/remove</c>: takes <paramref name="target"/> out of <paramref name="zone"/>, leaving it in its other zones.
	/// When that was its first zone, the next becomes its first.
	/// </summary>
	public static async ValueTask<Result<Success>> RemoveZoneAsync(
		IMediator mediator,
		INotifyService notifyService,
		IPermissionService permissionService,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpObject zone,
		bool noisy)
	{
		var zones = await mediator.ZonesOfAsync(target);
		if (!zones.Any(held => held.Object().DBRef.Equals(zone.Object().DBRef)))
		{
			return await RefusedAsync(notifyService, executor, noisy, ErrorMessages.Returns.NothingToDo,
				nameof(ErrorMessages.Notifications.ObjectNotInThatZone));
		}

		if (!await permissionService.Controls(executor, target))
		{
			return await RefusedAsync(notifyService, executor, noisy, ErrorMessages.Returns.PermissionDenied,
				nameof(ErrorMessages.Notifications.NoPowerToShiftReality));
		}

		await mediator.Send(new SetObjectZonesCommand(target,
			[.. zones.Where(held => !held.Object().DBRef.Equals(zone.Object().DBRef))]));
		return await ZoneChangedAsync(notifyService, executor, noisy);
	}

	/// <summary>
	/// What zoning <paramref name="target"/> to <paramref name="destination"/> asks and does before
	/// <paramref name="write"/> records it: <c>do_chzone</c> from its destination gate on (<c>src/set.c:408-482</c>).
	/// </summary>
	private static async ValueTask<Result<Success>> ZoneToAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		IRelationshipCycleChecker cycleChecker,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		IDidItService didItService,
		IFlagAndPowerService flagAndPowerService,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IConnectionService connections,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpObject destination,
		bool preserve,
		bool noisy,
		Func<ValueTask<Result<Success>>> write)
	{
		if (await ZoneRefusedAsync(parser, notifyService, permissionService, lockService, didItService, executor,
				destination, noisy) is Error<string> refusedZone)
		{
			return refusedZone;
		}

		if (await CycleRefusedAsync(cycleChecker, notifyService, executor, target, destination, noisy)
			is Error<string> refusedCycle)
		{
			return refusedCycle;
		}

		// set.c:470-471: a non-wizard's /preserve is discarded rather than refused, so the flags and
		// powers come off anyway. Hoisted above check_zone_lock because the warnings at :477-482 read it.
		if (preserve && !await executor.IsWizard())
		{
			preserve = false;
		}

		// set.c:449-450.
		await CheckZoneLockAsync(mediator, notifyService, permissionService, lockService, configuration, connections,
			executor, destination, noisy);

		// set.c:452-456. Hasprivs(Owner(thing)), so a mortal's object owned by nobody privileged is quiet.
		var owner = new AnySharpObject(await target.Object().Owner.WithCancellation(CancellationToken.None));
		if (noisy && !target.IsPlayer && await owner.IsPriv())
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ChzoningAdminOwnedObject),
				executor);
		}

		// set.c:472-482, ahead of the zone change. PennMUSH strips with clear_flag_internal() and
		// destroy_flag_bitmask(), which ask nobody's permission, so the controls() check above is the whole
		// authorization; PrivilegeHelpers does the same. Nothing below can fail, so the observable order is
		// PennMUSH's.
		if (!preserve && !target.IsPlayer)
		{
			await PrivilegeHelpers.StripPrivilegeAsync(mediator, target);
		}
		else if (noisy)
		{
			// set.c:477-482: the object keeps what the strip would have taken, so say what it kept.
			await WarnAboutKeptPrivilegeAsync(notifyService, executor, target);
		}

		return await write();
	}

	/// <summary>The "Zone changed." <c>do_chzone</c> reports for every change it makes (<c>src/set.c:487</c>).</summary>
	private static async ValueTask<Result<Success>> ZoneChangedAsync(INotifyService notifyService, AnySharpObject executor,
		bool noisy)
	{
		if (noisy)
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ZoneChanged), executor);
		}

		return new Success();
	}


	/// <summary>
	/// PennMUSH <c>check_zone_lock</c> (<c>src/lock.c:962-990</c>): a zone that has never been
	/// zone-locked gets <c>=me</c> installed on it, and one that has gets looked over for a lock that
	/// gates nothing.
	/// </summary>
	/// <remarks>
	/// The lock is the <b>Zone</b> lock, not the Chzone lock <c>do_chzone</c>'s destination gate reads.
	/// They are two locks with two jobs: Chzone says who may zone an object <em>to</em> this one
	/// (<c>src/set.c:409</c>), while Zone is what hands control of a zoned object to whoever passes it
	/// (<c>src/predicat.c:409</c>). Installing the default on Chzone left the control lock open and shut
	/// the destination gate against everyone but the zone itself.
	/// </remarks>
	private static async ValueTask CheckZoneLockAsync(
		IMediator mediator,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IConnectionService connections,
		AnySharpObject executor,
		AnySharpObject destination,
		bool noisy)
	{
		// check_zone_lock reads the lock with getlock (src/lock.c:964), so an inherited zone lock counts.
		if (await lockService.LookupAsync(destination, nameof(LockType.Zone), ExecutionBudget.CurrentToken) is not ResolvedLock)
		{
			// lock.c:965-967, written as GOD on purpose — the executor who most needs the lock installed is
			// the one who reached this zone through a lock rather than through control, and so cannot write
			// to it.
			await lockService.SetSystemAsync(destination, nameof(LockType.Zone),
				$"=#{destination.Object().DBRef.Number}", ExecutionBudget.CurrentToken);

			if (noisy)
			{
				await NotifyAboutZoneAsync(notifyService, permissionService, connections, executor, destination,
					nameof(ErrorMessages.Notifications.ZoneAutomaticallyLockedFormat));
			}

			return;
		}

		// lock.c:973-974.
		if (!noisy)
		{
			return;
		}

		// lock.c:975: the zone's Zone lock evaluated against the executor's *location*, which is the
		// cheapest thing that is not the executor and that a lock written as `=player` will not admit.
		var location = (await executor.Where()).WithExitOption();
		if (!await lockService.Evaluate(LockType.Zone, destination, location))
		{
			return;
		}

		// lock.c:976-978: "Does #0 and #2 pass it? If so, probably trivial elock".
		var playerStart = await RoomAsync(mediator, configuration.CurrentValue.Database.PlayerStart);
		var masterRoom = await RoomAsync(mediator, configuration.CurrentValue.Database.MasterRoom);
		var trivial = playerStart is AnySharpObject start && await lockService.Evaluate(LockType.Zone, destination, start)
			&& masterRoom is AnySharpObject master && await lockService.Evaluate(LockType.Zone, destination, master);

		await NotifyAboutZoneAsync(notifyService, permissionService, connections, executor, destination,
			trivial
				? nameof(ErrorMessages.Notifications.ZoneShouldHaveMoreSecureLockFormat)
				: nameof(ErrorMessages.Notifications.ZoneMayHaveLooseLockFormat));
	}

	/// <summary>One of <c>check_zone_lock</c>'s three notices, all of which name the zone by <c>unparse_object</c>.</summary>
	private static async ValueTask NotifyAboutZoneAsync(INotifyService notifyService,
		IPermissionService permissionService, IConnectionService connections, AnySharpObject executor,
		AnySharpObject destination, string key)
		=> await notifyService.NotifyLocalized(executor, key, executor,
			await MessageFormatting.UnparseObjectAsync(permissionService, executor, destination, connections));

	/// <summary><c>PLAYER_START</c> and <c>MASTER_ROOM</c>, which a world need not actually hold.</summary>
	private static async ValueTask<AnyOptionalSharpObject> RoomAsync(IMediator mediator, uint dbref)
		=> await mediator.Send(new GetObjectNodeQuery(new DBRef(Convert.ToInt32(dbref))));

	/// <summary>
	/// <c>do_chzone</c>'s two warnings for a target whose privileges survive the change
	/// (<c>src/set.c:477-482</c>) — because <c>/preserve</c> kept them, or because the target is a
	/// player and the strip never applied to one.
	/// </summary>
	private static async ValueTask WarnAboutKeptPrivilegeAsync(INotifyService notifyService, AnySharpObject executor,
		AnySharpObject target)
	{
		if (await target.IsPriv())
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ChzoningPrivilegedPlayer),
				executor);
		}

		// Inherit(thing) is the TRUST flag (src/flags.c).
		if (await target.HasFlag("TRUST"))
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ChzoningTrustPlayer),
				executor);
		}
	}

	/// <summary>
	/// <c>Zone(thing) == zone</c> (<c>src/set.c:392</c>), for both a real zone and none. An object in several zones is
	/// not already zoned to the first of them alone: <c>@chzone</c> to it leaves it in that one.
	/// </summary>
	private static async ValueTask<bool> AlreadyZonedAsync(IMediator mediator, AnySharpObject target,
		AnyOptionalSharpObject zone)
	{
		var zones = await mediator.ZonesOfAsync(target);
		return zone is AnySharpObject wanted
			? zones.Count == 1 && zones[0].Object().DBRef.Equals(wanted.Object().DBRef)
			: zones.Count == 0;
	}

	/// <summary>
	/// <c>do_chzone</c>'s destination gate (<c>src/set.c:408-420</c>): a player may zone to something
	/// they control, or to something whose <c>@lock/chzone</c> they pass.
	/// </summary>
	/// <remarks>
	/// PennMUSH computes <c>has_lock = getlock(zone, Chzone_Lock) != TRUE_BOOLEXP</c> and requires
	/// <c>has_lock &amp;&amp; eval_lock_with(...)</c>, with the comment "Note that an object with no
	/// chzone-lock isn't valid". An unset lock evaluates <c>#TRUE</c>, so asking only for the verdict
	/// would hand every never-zone-locked object to every mortal as a zone: the lock has to be
	/// *present* before its verdict counts.
	/// </remarks>
	private static async ValueTask<Result<Success>> ZoneRefusedAsync(
		IMUSHCodeParser parser,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		IDidItService didItService,
		AnySharpObject executor,
		AnySharpObject destination,
		bool noisy)
	{
		if (await permissionService.Controls(executor, destination))
		{
			return new Success();
		}

		var zoneLock = await lockService.LookupAsync(destination, nameof(LockType.ChZone), ExecutionBudget.CurrentToken);

		if (zoneLock is ResolvedLock resolved &&
			await lockService.Evaluate(resolved.Data.LockString, destination, executor))
		{
			return new Success();
		}

		// set.c:414-419: fail_lock() when the lock exists and refused, a bare notify when there was no
		// lock to fail.
		if (noisy && zoneLock is ResolvedLock)
		{
			await didItService.FailLockLocalized(parser, executor, destination, LockType.ChZone,
				new LocalizedNotification(nameof(ErrorMessages.Notifications.PermissionDeniedCannotZoneTo)));
			return new Error<string>(ErrorMessages.Returns.PermissionDenied);
		}

		return await RefusedAsync(notifyService, executor, noisy, ErrorMessages.Returns.PermissionDenied,
			nameof(ErrorMessages.Notifications.PermissionDeniedCannotZoneTo));
	}

	/// <summary>
	/// <c>do_chzone</c>'s self-zone guard and its cycle walk (<c>src/set.c:421-444</c>), which are two
	/// refusals with two different messages.
	/// </summary>
	/// <remarks>
	/// PennMUSH refuses the self-zone to mortals only (<c>:422</c>) — its walk stops on
	/// <c>tmp == Zone(tmp)</c>, so a privileged player may build that fixed point. SharpMUSH refuses it
	/// to everyone: <see cref="IObjectStore.IsReachableViaParentOrZoneAsync"/> and
	/// <see cref="IObjectRelationshipService.SetZone"/> both treat a self-loop as unsafe, and
	/// <c>ZoneParentCycleTests.SelfZone_ShouldFail</c> fixes that as the rule. Exempting a wizard here
	/// would not let the write through — <c>SetZone</c> refuses it again — it would only strip the
	/// object's flags and powers and install a zone lock on the way to the refusal. A deliberate
	/// difference for the compatibility profile (#1134), not a gap to close in this method.
	/// </remarks>
	private static async ValueTask<Result<Success>> CycleRefusedAsync(
		IRelationshipCycleChecker cycleChecker,
		INotifyService notifyService,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpObject destination,
		bool noisy)
		=> await cycleChecker.SafeToAddZoneAsync(target, destination) switch
		{
			RelationshipSafety.SelfReference
				=> await RefusedAsync(notifyService, executor, noisy, ErrorMessages.Returns.ZoneLoop,
					nameof(ErrorMessages.Notifications.CantZoneObjectsToThemselves)),
			RelationshipSafety.Cycle
				=> await RefusedAsync(notifyService, executor, noisy, ErrorMessages.Returns.ZoneLoop,
					nameof(ErrorMessages.Notifications.CantMakeCircularZones)),
			_ => new Success()
		};

	/// <summary>
	/// The outcome of the store write, which <see cref="IObjectRelationshipService"/> reports as
	/// <c>"1"</c> or as the <c>#-1 …</c> return of a check this method already made.
	/// </summary>
	private static Result<Success> Written(CallState written)
		=> written.Message.ToPlainText() is "1"
			? new Success()
			: new Error<string>(written.Message.ToPlainText());

	/// <summary>
	/// A <c>do_chzone</c> refusal: it says why to the player when <paramref name="noisy"/>, and returns
	/// 0 either way, so <c>@chzoneall</c>'s count passes over it.
	/// </summary>
	private static async ValueTask<Result<Success>> RefusedAsync(INotifyService notifyService,
		AnySharpObject executor, bool noisy, string errorReturn, string notificationKey)
	{
		if (noisy)
		{
			await notifyService.NotifyLocalized(executor, notificationKey, executor);
		}

		return new Error<string>(errorReturn);
	}
}
