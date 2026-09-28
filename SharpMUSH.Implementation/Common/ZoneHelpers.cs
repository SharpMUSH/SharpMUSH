using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
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
		IObjectStore database,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		IDidItService didItService,
		IManipulateSharpObjectService manipulateSharpObjectService,
		AnySharpObject executor,
		AnySharpObject target,
		AnyOptionalSharpObject zone,
		bool preserve,
		bool noisy)
	{
		// set.c:392-396. This is also do_chzoneall's `Zone(i) != zone` filter (wiz.c:1047): the refusal
		// returns 0, so the object is not counted.
		if (await AlreadyZonedAsync(target, zone))
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
			return Written(await manipulateSharpObjectService.UnsetZone(executor, target, noisy));
		}

		if (await ZoneRefusedAsync(parser, notifyService, permissionService, lockService, didItService, executor,
				destination, noisy) is Error<string> refusedZone)
		{
			return refusedZone;
		}

		if (await CycleRefusedAsync(mediator, database, notifyService, executor, target, destination, noisy)
			is Error<string> refusedCycle)
		{
			return refusedCycle;
		}

		// set.c:470-471: a non-wizard's /preserve is discarded rather than refused, so the flags and
		// powers come off anyway.
		if (preserve && !await executor.IsWizard())
		{
			preserve = false;
		}

		// set.c:472-482, but ahead of the zone change rather than after it.
		//
		// PennMUSH strips with clear_flag_internal() and destroy_flag_bitmask(), which ask nobody's
		// permission, so its one controls() check above is the whole authorization. These go through
		// ManipulateSharpObjectService, which checks Controls itself — and Controls reads the object's
		// *current* zone (PermissionService.Controls, Zone Master Object branch). Once the zone has
		// moved, an executor who held the object only through the zone it is leaving no longer controls
		// it, the strip is refused, and @CHZONE reports "Zone changed." over an object that kept every
		// power. Running the strip first is what keeps the authorization checked at the top of this
		// method the one that governs it. Nothing below can fail, so the observable order is PennMUSH's.
		if (!preserve && !target.IsPlayer)
		{
			await StripPrivilegeAsync(manipulateSharpObjectService, executor, target);
		}

		// check_zone_lock (src/lock.c:962): a zone that has never been zone-locked gets `=me` installed
		// on it, written as GOD. It is a system write on purpose — the executor who most needs it is the
		// one who reached this zone through its lock rather than through control, and so cannot write to
		// it.
		if (!destination.Object().Locks.ContainsKey(nameof(LockType.ChZone)))
		{
			await lockService.SetSystemAsync(destination, nameof(LockType.ChZone),
				$"=#{destination.Object().DBRef.Number}", ExecutionBudget.CurrentToken);
		}

		return Written(await manipulateSharpObjectService.SetZone(executor, target, destination, noisy));
	}

	/// <summary><c>Zone(thing) == zone</c> (<c>src/set.c:392</c>), for both a real zone and none.</summary>
	private static async ValueTask<bool> AlreadyZonedAsync(AnySharpObject target, AnyOptionalSharpObject zone)
	{
		var current = await target.Object().Zone.WithCancellation(CancellationToken.None);

		if (zone is not AnySharpObject wanted)
		{
			return current is None;
		}

		return current is AnySharpObject held && held.Object().DBRef.Equals(wanted.Object().DBRef);
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
	/// <see cref="IManipulateSharpObjectService.SetZone"/> both treat a self-loop as unsafe, and
	/// <c>ZoneParentCycleTests.SelfZone_ShouldFail</c> fixes that as the rule. Exempting a wizard here
	/// would not let the write through — <c>SetZone</c> refuses it again — it would only strip the
	/// object's flags and powers and install a zone lock on the way to the refusal. A deliberate
	/// difference for the compatibility profile (#1134), not a gap to close in this method.
	/// </remarks>
	private static async ValueTask<Result<Success>> CycleRefusedAsync(
		IMediator mediator,
		IObjectStore database,
		INotifyService notifyService,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpObject destination,
		bool noisy)
		=> await HelperFunctions.SafeToAddRelationship(mediator, database, target, destination) switch
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
	/// <c>clear_flag_internal</c> on <c>WIZARD</c>, <c>ROYALTY</c> and <c>TRUST</c>, then the whole
	/// power bitmask (<c>src/set.c:477-481</c>).
	/// </summary>
	private static async ValueTask StripPrivilegeAsync(IManipulateSharpObjectService manipulateSharpObjectService,
		AnySharpObject executor, AnySharpObject target)
	{
		string[] privileged = ["WIZARD", "ROYALTY", "TRUST"];

		foreach (var flag in privileged)
		{
			if (await target.HasFlag(flag))
			{
				await manipulateSharpObjectService.SetOrUnsetFlag(executor, target, $"!{flag}", false);
			}
		}

		await manipulateSharpObjectService.ClearAllPowers(executor, target, false);
	}

	/// <summary>
	/// The outcome of the store write, which <see cref="IManipulateSharpObjectService"/> reports as
	/// <c>"1"</c> or as the <c>#-1 …</c> return of a check this method already made.
	/// </summary>
	private static Result<Success> Written(CallState written)
		=> written.Message?.ToPlainText() is "1"
			? new Success()
			: new Error<string>(written.Message?.ToPlainText() ?? ErrorMessages.Returns.PermissionDenied);

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
