using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>Changes an object's owner, parent and zone.</summary>
public class ObjectRelationshipService(
	IMediator mediator,
	IRelationshipCycleChecker cycleChecker,
	IPermissionService permissionService,
	INotifyService notifyService,
	IAttributeService attributeService)
	: IObjectRelationshipService
{
	public async ValueTask<CallState> SetOwner(AnySharpObject executor, AnySharpObject obj, SharpPlayer newOwner, bool notify)
	{
		if (!await permissionService.Controls(executor, obj)
				|| !await permissionService.Controls(executor, newOwner))
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.YouDoNotControlThatObject);
			}
			return ErrorMessages.Returns.PermissionDenied;
		}

		// Ownership transfer logic confirmed:
		// - Executor must control the object being transferred (prevents unauthorized changes)
		// - Executor must control the new owner (prevents forcing ownership on others)
		// This matches PennMUSH behavior where @chown requires control of both parties

		await mediator.Send(new SetObjectOwnerCommand(obj, newOwner));

		return true;
	}

	public async ValueTask<CallState> SetParent(AnySharpObject executor, AnySharpObject obj, AnySharpObject newParent,
		bool notify)
	{
		// PennMUSH do_parent (src/set.c:1462-1467): the executor must control the new parent, or the
		// new parent must be LINK_OK and the executor must pass its Parent lock.
		var permitted = await permissionService.Controls(executor, newParent)
			|| (await newParent.HasFlag("LINK_OK")
				&& await permissionService.PassesLock(executor, newParent, LockType.Parent));

		if (!permitted)
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.PermissionDenied);
			}

			return ErrorMessages.Returns.PermissionDenied;
		}

		var parentSafety = await cycleChecker.SafeToAddParentAsync(obj, newParent);

		if (parentSafety != RelationshipSafety.Safe)
		{
			if (notify)
			{
				// PennMUSH's do_parent (src/set.c:1432,1477) notifies different text for
				// self-reference ("@parent me=me") vs. a cycle through the existing chain
				// ("@parent A=B" where B already descends from A) - the Returns sentinel below
				// stays shared, since Penn has no equivalent machine-readable distinction.
				var notificationKey = parentSafety == RelationshipSafety.SelfReference
					? nameof(Definitions.ErrorMessages.Notifications.SelfAncestor)
					: nameof(Definitions.ErrorMessages.Notifications.CyclicAncestor);
				await notifyService.NotifyLocalized(executor, notificationKey, executor);
			}

			return ErrorMessages.Returns.ParentLoop;
		}

		if (await attributeService.ExceedsMaxParentDepthAsync(newParent))
		{
			if (notify)
			{
				await notifyService.NotifyLocalized(executor, nameof(Definitions.ErrorMessages.Notifications.TooManyAncestors), executor);
			}

			return ErrorMessages.Returns.TooManyAncestors;
		}

		await mediator.Send(new SetObjectParentCommand(obj, newParent));

		// do_parent: `if (!AreQuiet(player, thing)) notify(player, T("Parent changed."))` (src/set.c:1488).
		if (notify && !await obj.Object().AreQuietAsync(executor))
		{
			await notifyService.NotifyLocalized(executor, nameof(Definitions.ErrorMessages.Notifications.ParentSet), executor);
		}

		return true;
	}

	public async ValueTask<CallState> UnsetParent(AnySharpObject executor, AnySharpObject obj, bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.PermissionDenied);
			}

			return ErrorMessages.Returns.PermissionDenied;
		}

		await mediator.Send(new UnsetObjectParentCommand(obj));

		// PennMUSH's do_parent (src/set.c) says "Parent changed." for "none" too.
		// do_parent: `if (!AreQuiet(player, thing)) notify(player, T("Parent changed."))` (src/set.c:1488).
		if (notify && !await obj.Object().AreQuietAsync(executor))
		{
			await notifyService.NotifyLocalized(executor, nameof(Definitions.ErrorMessages.Notifications.ParentSet), executor);
		}

		return true;
	}

	public async ValueTask<CallState> SetZone(AnySharpObject executor, AnySharpObject obj, AnySharpObject newZone,
		bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.PermissionDenied);
			}

			return ErrorMessages.Returns.PermissionDenied;
		}

		if (await cycleChecker.SafeToAddZoneAsync(obj, newZone) is not RelationshipSafety.Safe)
		{
			if (notify)
			{
				await notifyService.NotifyLocalized(executor, nameof(Definitions.ErrorMessages.Notifications.CantMakeCircularZones), executor);
			}

			return ErrorMessages.Returns.ZoneLoop;
		}

		await mediator.Send(new SetObjectZoneCommand(obj, newZone));

		// PennMUSH's do_chzone says "Zone changed." for every outcome it reports, including a cleared
		// zone (src/set.c:487). "Zone set."/"Zone cleared." were SharpMUSH inventions.
		if (notify)
		{
			await notifyService.NotifyLocalized(executor, nameof(Definitions.ErrorMessages.Notifications.ZoneChanged), executor);
		}

		return true;
	}

	public async ValueTask<CallState> UnsetZone(AnySharpObject executor, AnySharpObject obj, bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.PermissionDenied);
			}
			return ErrorMessages.Returns.PermissionDenied;
		}

		await mediator.Send(new UnsetObjectZoneCommand(obj));

		// @chzone x=none reports the same "Zone changed." as a set does (src/set.c:487).
		if (notify)
		{
			await notifyService.NotifyLocalized(executor, nameof(Definitions.ErrorMessages.Notifications.ZoneChanged), executor);
		}

		return true;
	}
}
