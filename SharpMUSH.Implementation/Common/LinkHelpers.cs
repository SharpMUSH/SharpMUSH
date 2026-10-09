using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// The linking work <c>@link</c> and <c>link()</c> share.
/// </summary>
/// <remarks>
/// PennMUSH's <c>fun_link</c> is one call to <c>do_link</c> (<c>src/fundb.c:2219-2237</c>), the same
/// routine <c>@link</c> reaches, with <c>args[2]</c> standing in for <c>/PRESERVE</c>. Written out
/// twice, the function allowed only a room as an exit destination where <c>do_link</c> allows any
/// container, had no link-lock path and no ownership transfer, read no <c>preserve</c>, and asked for
/// no control at all before setting a room's drop-to.
/// </remarks>
public static class LinkHelpers
{
	/// <inheritdoc cref="LinkHelpers"/>
	/// <param name="targetName">The unmatched object being linked, <c>do_link</c>'s <c>name</c>.</param>
	/// <param name="destinationName">The unmatched destination, <c>do_link</c>'s <c>room_name</c>.</param>
	/// <param name="preserve">
	/// <c>@link/preserve</c>, and <c>link()</c>'s third argument (<c>src/fundb.c:2232-2233</c>).
	/// </param>
	/// <remarks>
	/// <c>do_link</c>'s second guard — "You somehow wound up in a exit. No biscuit."
	/// (<c>src/create.c:325-329</c>) — has no counterpart here, and cannot. PennMUSH holds a location
	/// in one flat dbref space, so an object can be sitting in an exit; SharpMUSH's location relation
	/// is an <see cref="AnySharpContainer"/>, which is a room, a player or a thing and never an exit.
	/// There is no state for the guard to catch.
	/// </remarks>
	public static async ValueTask<Result<Success>> LinkAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		INotifyService notifyService,
		ILocateService locateService,
		IPermissionService permissionService,
		ILockService lockService,
		IAttributeService attributeService,
		IFlagAndPowerService flagAndPowerService,
		IConnectionService connectionService,
		AnySharpObject executor,
		string targetName,
		string destinationName,
		bool preserve)
	{
		// create.c:321-324: no destination at all is @unlink, ahead of matching the object, and do_link
		// reports 0 whether the unlink went through or not.
		if (destinationName.Length == 0)
		{
			return await UnlinkAsync(parser, mediator, notifyService, locateService, permissionService,
				attributeService, connectionService, executor, targetName) switch
			{
				Success => new Error<string>(ErrorMessages.Returns.MissingArguments),
				Error<string> refused => refused
			};
		}

		return await LocatedAsync(parser, locateService, executor, targetName) switch
		{
			AnySharpObject target => await LinkedAsync(parser, mediator, notifyService, locateService,
				permissionService, lockService, attributeService, flagAndPowerService, connectionService,
				executor, target, destinationName, preserve),
			Error<string> unmatched => unmatched
		};
	}

	/// <summary>
	/// <c>do_link</c>'s type switch (<c>src/create.c:332-447</c>) over the object it matched.
	/// </summary>
	private static async ValueTask<Result<Success>> LinkedAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		INotifyService notifyService,
		ILocateService locateService,
		IPermissionService permissionService,
		ILockService lockService,
		IAttributeService attributeService,
		IFlagAndPowerService flagAndPowerService,
		IConnectionService connectionService,
		AnySharpObject executor,
		AnySharpObject target,
		string destinationName,
		bool preserve)
		=> target switch
		{
			SharpExit exit => await LinkedExitAsync(parser, mediator, notifyService, locateService, permissionService,
				lockService, attributeService, flagAndPowerService, connectionService, executor, target, exit,
				destinationName, preserve),
			SharpThing thing => await HomedAsync(parser, mediator, notifyService, locateService,
				permissionService, executor, target, thing, destinationName),
			SharpPlayer player => await HomedAsync(parser, mediator, notifyService, locateService,
				permissionService, executor, target, player, destinationName),
			SharpRoom room => await DroppedToAsync(parser, mediator, notifyService, locateService, permissionService,
				executor, target, room, destinationName),
			_ => await RefusedAsync(notifyService, executor, ErrorMessages.Returns.InvalidObjectType,
				ErrorMessages.Notifications.InvalidObjectTypeForLinking)
		};

	/// <summary>
	/// PennMUSH <c>do_unlink</c> (<c>src/create.c:250-289</c>), which <c>@UNLINK</c> is and which
	/// <c>do_link</c> falls back to when it is given no destination.
	/// </summary>
	/// <remarks>
	/// <c>do_unlink</c> matches <c>MAT_EXIT | MAT_HERE | MAT_ABSOLUTE</c> with a <c>TYPE_EXIT</c>
	/// preference, and adds <c>MAT_CONTROL</c> for anyone who is not a wizard (<c>:254-258</c>). The
	/// call is <c>match_result</c>, not <c>noisy_match_result</c>, because <c>do_unlink</c> words both
	/// failures itself. Matching with <see cref="LocateFlags.All"/> and letting the locator report
	/// meant <c>@unlink</c> resolved names PennMUSH does not reach here — any neighbour, anything
	/// carried, any player by <c>*name</c> — and answered "I can't see that here." for the rest.
	/// </remarks>
	public static async ValueTask<Result<Success>> UnlinkAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		INotifyService notifyService,
		ILocateService locateService,
		IPermissionService permissionService,
		IAttributeService attributeService,
		IConnectionService connectionService,
		AnySharpObject executor,
		string targetName)
	{
		// create.c:256-258. A candidate dropped for MAT_CONTROL leaves the search empty, so a mortal
		// naming someone else's exit gets "Unlink what?" rather than "Permission denied." — the
		// permission refusal below is only reachable once the match itself succeeded.
		var flags = UnlinkMatchFlags;
		if (!await executor.IsWizard())
		{
			flags |= LocateFlags.OnlyMatchLookerControlledObjects;
		}

		return await locateService.Locate(parser, executor, executor, targetName, flags) switch
		{
			AnySharpObject target => await UnlinkedAsync(mediator, notifyService, permissionService, attributeService,
				connectionService, executor, target),
			// create.c:263-265.
			Error<string> { Value: ErrorMessages.Returns.AmbiguousMatch }
				=> await RefusedAsync(notifyService, executor, ErrorMessages.Returns.AmbiguousMatch,
					ErrorMessages.Notifications.AmbiguousMatch),
			// create.c:260-262: match_result's only other failure is NOTHING.
			_ => await RefusedAsync(notifyService, executor, ErrorMessages.Returns.NoMatch,
				ErrorMessages.Notifications.UnlinkWhat)
		};
	}

	/// <summary>
	/// <c>do_unlink</c>'s <c>match_flags</c> before the <c>MAT_CONTROL</c> a mortal adds, with
	/// <c>TYPE_EXIT</c> as the preferred type (<c>src/create.c:254-259</c>).
	/// </summary>
	/// <remarks>
	/// The preference is not <c>MAT_TYPE</c>, so a room still matches — which is how <c>@unlink here</c>
	/// removes a drop-to.
	/// </remarks>
	private const LocateFlags UnlinkMatchFlags =
		LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.MatchHereForLookerLocation |
		LocateFlags.AbsoluteMatch | LocateFlags.ExitsPreference;

	/// <inheritdoc cref="UnlinkAsync"/>
	private static async ValueTask<Result<Success>> UnlinkedAsync(
		IMediator mediator,
		INotifyService notifyService,
		IPermissionService permissionService,
		IAttributeService attributeService,
		IConnectionService connectionService,
		AnySharpObject executor,
		AnySharpObject target)
	{
		// create.c:267.
		if (!await permissionService.Controls(executor, target))
		{
			return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.PermissionDenied,
				ErrorMessages.Notifications.PermissionDenied);
		}

		switch (target)
		{
			// create.c:271-277. _LINKTYPE goes with the relation: HOME and VARIABLE are destinations too,
			// so leaving one behind would unlink an exit that still leads somewhere.
			case SharpExit exit:
				var ledTo = await DestinationNameAsync(permissionService, attributeService, connectionService, executor,
					target, exit);
				await attributeService.SetAttributeAsync(executor, target, TeleportHelpers.AttrLinkType,
					MarkupText.Empty);
				await mediator.Send(new UnlinkExitCommand(exit));
				await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.UnlinkedExit),
					executor, target.Object().DBRef.Number, ledTo);
				return new Success();

			// create.c:278-282.
			case SharpRoom room:
				await mediator.Send(new UnlinkRoomCommand(room));
				await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DropToRemoved),
					executor);
				return new Success();

			// create.c:283-285.
			default:
				return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.InvalidObjectType,
					ErrorMessages.Notifications.InvalidObjectTypeGeneric);
		}
	}

	/// <summary>
	/// <c>do_link</c>'s <c>TYPE_EXIT</c> branch (<c>src/create.c:333-383</c>): the two variable-link
	/// keywords, <c>can_link_to</c> on the destination, then the permission pair that lets an exit be
	/// linked either by someone who controls it or — only while it leads nowhere — by anyone who
	/// passes its <c>@lock/link</c>, who then seizes it.
	/// </summary>
	private static async ValueTask<Result<Success>> LinkedExitAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		INotifyService notifyService,
		ILocateService locateService,
		IPermissionService permissionService,
		ILockService lockService,
		IAttributeService attributeService,
		IFlagAndPowerService flagAndPowerService,
		IConnectionService connectionService,
		AnySharpObject executor,
		AnySharpObject target,
		SharpExit exit,
		string destinationName,
		bool preserve)
	{
		// create.c:334-335: check_var_link comes before parse_linkable_room, so HOME and VARIABLE are
		// keywords no object name can shadow.
		var keyword = destinationName switch
		{
			_ when destinationName.Equals(TeleportHelpers.LinkTypeHome, StringComparison.InvariantCultureIgnoreCase)
				=> TeleportHelpers.LinkTypeHome,
			_ when destinationName.Equals(TeleportHelpers.LinkTypeVariable, StringComparison.InvariantCultureIgnoreCase)
				=> TeleportHelpers.LinkTypeVariable,
			_ => null
		};

		if (keyword is not null)
		{
			if (await ControlsOrSeizesAsync(mediator, notifyService, permissionService, lockService, attributeService,
					flagAndPowerService, executor, target, exit, preserve) is Error<string> refusedKeyword)
			{
				return refusedKeyword;
			}

			await attributeService.SetAttributeAsync(executor, target, TeleportHelpers.AttrLinkType,
				MarkupText.Plain(keyword));
			// create.c:385: unparse_object names HOME and AMBIGUOUS as *HOME* and *VARIABLE*.
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LinkedExitToObject),
				executor, target.Object().DBRef.Number,
				keyword == TeleportHelpers.LinkTypeHome ? UnparsedHome : UnparsedVariable);
			return new Success();
		}

		return await LocatedAsync(parser, locateService, executor, destinationName) switch
		{
			AnySharpObject destination => await LinkedExitToAsync(mediator, notifyService, permissionService,
				lockService, attributeService, flagAndPowerService, connectionService, executor, target, exit,
				destination, preserve),
			Error<string> unmatched => unmatched
		};
	}

	/// <inheritdoc cref="LinkedExitAsync"/>
	private static async ValueTask<Result<Success>> LinkedExitToAsync(
		IMediator mediator,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		IAttributeService attributeService,
		IFlagAndPowerService flagAndPowerService,
		IConnectionService connectionService,
		AnySharpObject executor,
		AnySharpObject target,
		SharpExit exit,
		AnySharpObject destination,
		bool preserve)
	{
		// An exit may lead to any container — room, player or thing (PennMUSH can_link_to). Only
		// another exit is not a place you can end up.
		if (destination.AsOptionalContainer is not AnySharpContainer exitDestination)
		{
			return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.InvalidDestination,
				ErrorMessages.Notifications.InvalidDestinationExit);
		}

		// create.c:338-341.
		if (!await permissionService.CanLinkToAsync(executor, destination))
		{
			return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.PermissionDenied,
				ErrorMessages.Notifications.CantLinkToThat);
		}

		if (await ControlsOrSeizesAsync(mediator, notifyService, permissionService, lockService, attributeService,
				flagAndPowerService, executor, target, exit, preserve) is Error<string> refused)
		{
			return refused;
		}

		await attributeService.SetAttributeAsync(executor, target, TeleportHelpers.AttrLinkType, MarkupText.Empty);
		await mediator.Send(new LinkExitCommand(exit, exitDestination));

		// create.c:385-386.
		await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LinkedExitToObject), executor,
			target.Object().DBRef.Number,
			await MessageFormatting.UnparseObjectAsync(permissionService, executor, destination, connectionService));
		return new Success();
	}

	/// <summary>
	/// <c>do_link</c>'s exit permission pair and the ownership transfer that follows it
	/// (<c>src/create.c:342-380</c>).
	/// </summary>
	/// <remarks>
	/// The gate is <c>controls(player, thing) || (Location(thing) == NOTHING &amp;&amp;
	/// eval_lock(Link_Lock))</c>. An exit's <c>Location</c> is its destination, so the lock half is
	/// reachable only while the exit leads nowhere. SharpMUSH asked for control up front and then
	/// asked again inside, which left the whole seizure path dead.
	/// <para>The seizure is <c>chown_object(player, thing, player, 0)</c> followed by
	/// <c>Owner(thing) = Owner(player)</c> (<c>:371-377</c>): the new owner, and <c>HALT</c> set so the
	/// exit's code cannot keep running for the owner who just lost it. <c>preserve</c> keeps both
	/// (<c>:369</c>, <c>:375</c>) and is wizard-only (<c>:352-355</c>).</para>
	/// </remarks>
	private static async ValueTask<Result<Success>> ControlsOrSeizesAsync(
		IMediator mediator,
		INotifyService notifyService,
		IPermissionService permissionService,
		ILockService lockService,
		IAttributeService attributeService,
		IFlagAndPowerService flagAndPowerService,
		AnySharpObject executor,
		AnySharpObject target,
		SharpExit exit,
		bool preserve)
	{
		var controls = await permissionService.Controls(executor, target);

		// create.c:344-345: only an exit that leads nowhere is open to whoever passes its link lock.
		if (!controls)
		{
			if (!await LeadsNowhereAsync(attributeService, executor, target, exit))
			{
				return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.PermissionDenied,
					ErrorMessages.Notifications.PermissionDenied);
			}

			if (!await lockService.Evaluate(LockType.Link, target, executor))
			{
				return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.PermissionDenied,
					ErrorMessages.Notifications.DontPassLinkLock);
			}
		}

		// create.c:352-355, asked after the permission pair and before anything is written.
		if (preserve && !await executor.IsWizard())
		{
			return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.PermissionDenied,
				ErrorMessages.Notifications.PermissionDenied);
		}

		// create.c:367-378: an exit linked by its controller keeps its owner, and /preserve keeps it
		// even when someone else's link lock let the linker in.
		if (controls || preserve)
		{
			return new Success();
		}

		// create.c:371 into chown_object, whose non-God branch writes Owner(newowner) rather than
		// newowner itself (src/set.c:303-307): a thing running a forced @link hands the exit to the
		// thing's owner, not to the thing.
		var seizedBy = await executor.Object().Owner.WithCancellation(CancellationToken.None);

		try
		{
			await mediator.Send(new SetObjectOwnerCommand(target, seizedBy));
		}
		catch (Exception)
		{
			// The transfer is a store write with no failure the caller can act on beyond reporting
			// it, and do_link's seizure is not a step it can skip: an exit left with its old owner
			// but a new destination is worse than a refusal.
			return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.PermissionDenied,
				ErrorMessages.Notifications.FailedToTransferOwnership);
		}

		// chown_object's non-preserve half (src/set.c:332-340) sets HALT, so the exit's code stops
		// running for the owner who just lost it.
		await flagAndPowerService.SetOrUnsetFlag(executor, target, "HALT", true);
		return new Success();
	}

	/// <summary>
	/// <c>Location(thing) == NOTHING</c> for an exit (<c>src/create.c:344</c>) — it leads nowhere.
	/// </summary>
	/// <remarks>
	/// SharpMUSH splits an exit's destination in two: an ordinary one is the <c>Home</c> relation, and
	/// <c>HOME</c>/<c>VARIABLE</c> live in <c>_LINKTYPE</c> with no relation written
	/// (<see cref="TeleportHelpers.ResolveExitDestination"/> reads both). Penn has one field —
	/// <c>check_var_link</c> hands <c>do_link</c> a pseudo-dbref, which is not <c>NOTHING</c> — so
	/// asking only about <c>Home</c> would offer a variable exit to anyone who passes its link lock.
	/// </remarks>
	private static async ValueTask<bool> LeadsNowhereAsync(IAttributeService attributeService,
		AnySharpObject executor, AnySharpObject target, SharpExit exit)
	{
		if (await exit.Home.WithCancellation(CancellationToken.None) is not None)
		{
			return false;
		}

		return await attributeService.GetAttributeAsync(executor, target, TeleportHelpers.AttrLinkType,
				IAttributeService.AttributeMode.Read, false) is not SharpAttribute[] { Length: > 0 } linkType
			|| string.IsNullOrEmpty(linkType[0].Value.ToPlainText().Trim());
	}

	/// <summary>
	/// <c>do_link</c>'s <c>TYPE_PLAYER</c>/<c>TYPE_THING</c> branch (<c>src/create.c:386-414</c>):
	/// a home is any non-exit that is not the object itself, gated on control-or-<c>ABODE</c> of the
	/// destination and on control of the object.
	/// </summary>
	private static async ValueTask<Result<Success>> HomedAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		INotifyService notifyService,
		ILocateService locateService,
		IPermissionService permissionService,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpContent homed,
		string destinationName)
	{
		return await LocatedAsync(parser, locateService, executor, destinationName) switch
		{
			AnySharpObject destination => await HomedToAsync(mediator, notifyService, permissionService, executor,
				target, homed, destination),
			Error<string> unmatched => unmatched
		};
	}

	/// <inheritdoc cref="HomedAsync"/>
	private static async ValueTask<Result<Success>> HomedToAsync(
		IMediator mediator,
		INotifyService notifyService,
		IPermissionService permissionService,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpContent homed,
		AnySharpObject destination)
	{
		// create.c:395: a home is any object that is not an exit — a room, a player or a thing.
		// safe_tel's "homed to the mover" case (move.c:311) is only reachable because a player can
		// be a home.
		if (destination.AsOptionalContainer is not AnySharpContainer home)
		{
			return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.InvalidDestination,
				ErrorMessages.Notifications.HomeIsAnExit);
		}

		// create.c:399.
		if (destination.Object().DBRef.Equals(target.Object().DBRef))
		{
			return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.InvalidDestination,
				ErrorMessages.Notifications.CannotLinkToItself);
		}

		// create.c:404. ABODE is ROOM-only in the flag seed, as in PennMUSH, so a player or thing
		// destination is gated on control alone. Penn's following room == HOME guard (create.c:412)
		// is unreachable: this branch matches with MAT_EVERYTHING, which has no home entry, and only
		// parse_linkable_room ever yields HOME.
		if (!await permissionService.Controls(executor, destination) && !await destination.HasFlag("ABODE"))
		{
			return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.PermissionDenied,
				ErrorMessages.Notifications.PermissionDenied);
		}

		// create.c:408.
		if (!await permissionService.Controls(executor, target))
		{
			return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.PermissionDenied,
				ErrorMessages.Notifications.PermissionDenied);
		}

		await mediator.Send(new SetObjectHomeCommand(homed, home));
		// create.c:419: `if (!Quiet(player) && !(Quiet(thing) && (Owner(thing) == player)))`, which is
		// AreQuiet(player, thing). "Dropto set." below has no such test (create.c:439).
		if (!await target.Object().AreQuietAsync(executor))
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HomeSet), executor);
		}
		return new Success();
	}

	/// <summary>
	/// <c>do_link</c>'s <c>TYPE_ROOM</c> branch (<c>src/create.c:416-436</c>): a drop-to must be a
	/// room, and setting one needs control of the room being linked. The function asked for neither.
	/// </summary>
	private static async ValueTask<Result<Success>> DroppedToAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		INotifyService notifyService,
		ILocateService locateService,
		IPermissionService permissionService,
		AnySharpObject executor,
		AnySharpObject target,
		SharpRoom room,
		string destinationName)
	{
		return await LocatedAsync(parser, locateService, executor, destinationName) switch
		{
			AnySharpObject destination => await DroppedToRoomAsync(mediator, notifyService, permissionService, executor,
				target, room, destination),
			Error<string> unmatched => unmatched
		};
	}

	/// <inheritdoc cref="DroppedToAsync"/>
	private static async ValueTask<Result<Success>> DroppedToRoomAsync(
		IMediator mediator,
		INotifyService notifyService,
		IPermissionService permissionService,
		AnySharpObject executor,
		AnySharpObject target,
		SharpRoom room,
		AnySharpObject destination)
	{
		// create.c:420.
		if (destination is not SharpRoom destinationRoom)
		{
			return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.InvalidDestination,
				ErrorMessages.Notifications.DropToMustBeRoom);
		}

		// create.c:424.
		if (!await permissionService.Controls(executor, target))
		{
			return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.PermissionDenied,
				ErrorMessages.Notifications.PermissionDenied);
		}

		await mediator.Send(new LinkRoomCommand(room, destinationRoom));
		await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DropToSet), executor);
		return new Success();
	}

	/// <summary>
	/// <c>do_link</c>'s destination match (<c>src/create.c:336</c>, <c>:387</c>, <c>:417</c>), which
	/// reports its own failure and leaves <c>do_link</c> returning 0. The locator's error is the one
	/// that comes back, so an ambiguous name still says it was ambiguous.
	/// </summary>
	private static async ValueTask<Result<AnySharpObject>> LocatedAsync(IMUSHCodeParser parser,
		ILocateService locateService, AnySharpObject executor, string destinationName)
		=> await locateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, destinationName,
			LocateFlags.All) switch
		{
			AnySharpObject found => found,
			Error<CallState> reported => new Error<string>(reported.Value.Message.ToPlainText()
				?? ErrorMessages.Returns.NoSuchObject)
		};

	/// <summary><c>unparse_object</c>'s word for <c>HOME</c> (<c>src/unparse.c:108-109</c>).</summary>
	private const string UnparsedHome = "*HOME*";

	/// <summary><c>unparse_object</c>'s word for <c>AMBIGUOUS</c>, a variable link (<c>src/unparse.c:106-107</c>).</summary>
	private const string UnparsedVariable = "*VARIABLE*";

	/// <summary><c>unparse_object</c>'s word for <c>NOTHING</c>, an unlinked exit (<c>src/unparse.c:104-105</c>).</summary>
	private const string UnparsedNothing = "*NOTHING*";

	/// <summary>
	/// Where an exit leads, as <c>unparse_object</c> names it: a <c>_LINKTYPE</c> keyword, the
	/// destination with its dbref when the viewer may see it, or <c>*NOTHING*</c>.
	/// </summary>
	private static async ValueTask<string> DestinationNameAsync(IPermissionService permissionService,
		IAttributeService attributeService, IConnectionService connectionService, AnySharpObject executor,
		AnySharpObject target, SharpExit exit)
	{
		var linkType = await attributeService.GetAttributeAsync(executor, target, TeleportHelpers.AttrLinkType,
			IAttributeService.AttributeMode.Read, false) is SharpAttribute[] { Length: > 0 } chain
			? chain[0].Value.ToPlainText().Trim()
			: string.Empty;

		if (linkType.Equals(TeleportHelpers.LinkTypeHome, StringComparison.OrdinalIgnoreCase)) return UnparsedHome;
		if (linkType.Equals(TeleportHelpers.LinkTypeVariable, StringComparison.OrdinalIgnoreCase)) return UnparsedVariable;

		return await exit.Home.WithCancellation(CancellationToken.None) switch
		{
			AnySharpContainer destination => await MessageFormatting.UnparseObjectAsync(permissionService, executor,
				destination.WithExitOption(), connectionService),
			_ => UnparsedNothing
		};
	}

	/// <summary>
	/// A <c>do_link</c> refusal: it says why to the linker and returns 0, which the command reports as
	/// its error return and the function as a falsehood.
	/// </summary>
	private static async ValueTask<Result<Success>> RefusedAsync(INotifyService notifyService,
		AnySharpObject executor, string errorReturn, string notifyMessage)
	{
		await notifyService.NotifyAndReturn(executor.Object().DBRef, errorReturn, notifyMessage, shouldNotify: true);
		return new Error<string>(errorReturn);
	}
}
