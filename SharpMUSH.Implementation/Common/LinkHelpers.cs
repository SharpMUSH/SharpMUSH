using Mediator;
using SharpMUSH.Library;
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
	/// <param name="target">
	/// The object being linked, already matched — <c>do_link</c>'s <c>noisy_match_result</c>
	/// (<c>src/create.c:330</c>), which both callers perform.
	/// </param>
	/// <param name="destinationName">The unmatched destination, <c>do_link</c>'s <c>room_name</c>.</param>
	/// <param name="preserve">
	/// <c>@link/preserve</c>, and <c>link()</c>'s third argument (<c>src/fundb.c:2232-2233</c>).
	/// </param>
	public static async ValueTask<Result<Success>> LinkAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		INotifyService notifyService,
		ILocateService locateService,
		IPermissionService permissionService,
		ILockService lockService,
		IAttributeService attributeService,
		IManipulateSharpObjectService manipulateSharpObjectService,
		AnySharpObject executor,
		AnySharpObject target,
		string destinationName,
		bool preserve)
		=> target switch
		{
			SharpExit exit => await LinkedExitAsync(parser, mediator, notifyService, locateService, permissionService,
				lockService, attributeService, manipulateSharpObjectService, executor, target, exit, destinationName,
				preserve),
			SharpThing or SharpPlayer => await HomedAsync(parser, mediator, notifyService, locateService,
				permissionService, executor, target, destinationName),
			SharpRoom room => await DroppedToAsync(parser, mediator, notifyService, locateService, permissionService,
				executor, target, room, destinationName),
			_ => await RefusedAsync(notifyService, executor, ErrorMessages.Returns.InvalidObjectType,
				ErrorMessages.Notifications.InvalidObjectTypeForLinking)
		};

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
		IManipulateSharpObjectService manipulateSharpObjectService,
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
			if (await ControlsOrSeizesAsync(mediator, notifyService, permissionService, lockService,
					manipulateSharpObjectService, executor, target, exit, preserve) is Error<string> refusedKeyword)
			{
				return refusedKeyword;
			}

			await attributeService.SetAttributeAsync(executor, target, TeleportHelpers.AttrLinkType,
				MarkupText.Plain(keyword));
			await notifyService.NotifyLocalized(executor,
				keyword == TeleportHelpers.LinkTypeHome
					? nameof(ErrorMessages.Notifications.LinkedToHome)
					: nameof(ErrorMessages.Notifications.LinkedToVariable), executor);
			return new Success();
		}

		// do_link matches the destination with noisy_match_result/parse_linkable_room, which reports
		// its own failure and leaves do_link returning 0 (src/create.c:336, :387, :417).
		if (await locateService.LocateAndNotifyIfInvalid(parser, executor, executor, destinationName, LocateFlags.All)
			is not AnySharpObject destination)
		{
			return new Error<string>(ErrorMessages.Returns.NoSuchObject);
		}

		// An exit may lead to any container — room, player or thing (PennMUSH can_link_to). Only
		// another exit is not a place you can end up.
		if (!destination.IsContainer)
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

		if (await ControlsOrSeizesAsync(mediator, notifyService, permissionService, lockService,
				manipulateSharpObjectService, executor, target, exit, preserve) is Error<string> refused)
		{
			return refused;
		}

		await attributeService.SetAttributeAsync(executor, target, TeleportHelpers.AttrLinkType, MarkupText.Empty);
		await mediator.Send(new LinkExitCommand(exit, destination.AsContainer));

		await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LinkedExitToRoom), executor,
			target.Object().DBRef.Number, destination.Object().DBRef.Number);
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
		IManipulateSharpObjectService manipulateSharpObjectService,
		AnySharpObject executor,
		AnySharpObject target,
		SharpExit exit,
		bool preserve)
	{
		var controls = await permissionService.Controls(executor, target);

		// create.c:344-345: only an exit that leads nowhere is open to whoever passes its link lock.
		if (!controls)
		{
			var unlinked = await exit.Home.WithCancellation(CancellationToken.None) is None;
			if (!unlinked)
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

		// create.c:371. Only a player can own anything, so an exit seized by a non-player object is
		// not handed to something that cannot hold it.
		if (executor is SharpPlayer executorPlayer)
		{
			try
			{
				await mediator.Send(new SetObjectOwnerCommand(target, executorPlayer));
			}
			catch (Exception)
			{
				return await RefusedAsync(notifyService, executor, ErrorMessages.Returns.PermissionDenied,
					ErrorMessages.Notifications.FailedToTransferOwnership);
			}
		}

		// chown_object's non-preserve half (src/set.c:332-340) sets HALT, so the exit's code stops
		// running for the owner who just lost it.
		await manipulateSharpObjectService.SetOrUnsetFlag(executor, target, "HALT", true);
		return new Success();
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
		string destinationName)
	{
		// do_link matches the destination with noisy_match_result/parse_linkable_room, which reports
		// its own failure and leaves do_link returning 0 (src/create.c:336, :387, :417).
		if (await locateService.LocateAndNotifyIfInvalid(parser, executor, executor, destinationName, LocateFlags.All)
			is not AnySharpObject destination)
		{
			return new Error<string>(ErrorMessages.Returns.NoSuchObject);
		}

		// create.c:395: a home is any object that is not an exit — a room, a player or a thing.
		// safe_tel's "homed to the mover" case (move.c:311) is only reachable because a player can
		// be a home.
		if (!destination.IsContainer)
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

		await mediator.Send(new SetObjectHomeCommand(target.AsContent, destination.AsContainer));
		await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HomeSet), executor);
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
		// do_link matches the destination with noisy_match_result/parse_linkable_room, which reports
		// its own failure and leaves do_link returning 0 (src/create.c:336, :387, :417).
		if (await locateService.LocateAndNotifyIfInvalid(parser, executor, executor, destinationName, LocateFlags.All)
			is not AnySharpObject destination)
		{
			return new Error<string>(ErrorMessages.Returns.NoSuchObject);
		}

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
