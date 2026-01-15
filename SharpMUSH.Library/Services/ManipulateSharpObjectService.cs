using Mediator;
using OneOf.Types;
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

public class ManipulateSharpObjectService(
	IMediator mediator,
	IPermissionService permissionService,
	IPasswordService passwordService,
	IValidateService validateService,
	INotifyService notifyService,
	IAttributeService attributeService,
	IPublisher publisher)
	: IManipulateSharpObjectService
{
	public async ValueTask<CallState> SetName(AnySharpObject executor, AnySharpObject obj, MString name, bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, "You do not control that object.");
			}

			return Errors.ErrorPerm;
		}

		if (!await validateService.Valid(IValidateService.ValidationType.Name, name, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, $"You cannot name that object {name}.");
			}

			return Errors.ErrorPerm;
		}

		switch (obj)
		{
			case { IsThing: true } or { IsRoom: true }:
				await mediator.Send(new SetNameCommand(obj, name));
				return obj.Object().DBRef;

			case { IsPlayer: true }:
				var tryFindPlayerByName = await (mediator.CreateStream(new GetPlayerQuery(name.ToPlainText())))
					.ToArrayAsync();
				if (tryFindPlayerByName.Any(x =>
					    x.Object.Name.Equals(name.ToPlainText(), StringComparison.InvariantCultureIgnoreCase)))
				{
					if (notify)
					{
						await notifyService.Notify(executor, "That player name is already in use.");
					}

					return "#-1 PLAYER NAME ALREADY IN USE.";
				}

				var playerSplit = MModule.split(";", name);

				await mediator.Send(new SetNameCommand(obj, playerSplit.First()));

				if (playerSplit.Length <= 1)
				{
					return obj.Object().DBRef;
				}

				var aliases = playerSplit.Skip(1).Select(x => x.ToPlainText()).ToArray();

				if (tryFindPlayerByName
				    .SelectMany(x => x.Aliases ?? [])
				    .Intersect(aliases, StringComparer.InvariantCultureIgnoreCase)
				    .Any())
				{
					if (notify)
					{
						await notifyService.Notify(executor, "That player alias is already in use.");
					}

					return "#-1 PLAYER ALIAS ALREADY IN USE.";
				}

				await attributeService.SetAttributeAsync(executor, obj, "ALIAS",
					MModule.multipleWithDelimiter(MModule.single(";"), aliases.Select(MModule.single)));

				return obj.Object().DBRef;

			default:
				var split = MModule.split(";", name);
				await mediator.Send(new SetNameCommand(obj, split.First()));
				if (split.Length > 1)
				{
					await attributeService.SetAttributeAsync(executor, obj, "ALIAS",
						MModule.multipleWithDelimiter(MModule.single(";"), split.Skip(1)));
				}

				return obj.Object().DBRef;
		}
	}

	public async ValueTask<CallState> SetPassword(AnySharpObject executor, SharpPlayer player, string newPassword,
		bool notify)
	{
		if (!await permissionService.Controls(executor, player))
		{
			if (notify)
			{
				await notifyService.Notify(executor, "You do not control that object.");
			}

			return Errors.ErrorPerm;
		}

		if (!await validateService.Valid(IValidateService.ValidationType.Password, MModule.single(newPassword), new None()))
		{
			if (notify)
			{
				await notifyService.Notify(executor, "That password is not a valid password.");
			}

			return Errors.ErrorInvalidPassword;
		}

		var hashedPw = passwordService.HashPassword(player.Object.DBRef.ToString(), newPassword);
		await passwordService.SetPassword(player, hashedPw);

		return true;
	}

	public async ValueTask<CallState> SetOrUnsetFlag(AnySharpObject executor, AnySharpObject obj, string flagOrFlagAlias,
		bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, "You do not control that object.");
			}

			return Errors.ErrorPerm;
		}

		var unset = flagOrFlagAlias.StartsWith('!');
		var plainFlag = flagOrFlagAlias;
		plainFlag = unset
			? plainFlag[1..]
			: plainFlag;

		var realFlag = await mediator.Send(new GetObjectFlagQuery(plainFlag.ToUpperInvariant()));
		if (realFlag is null)
		{
			if (notify)
			{
				await notifyService.Notify(executor, $"No such flag exists: {plainFlag}.");
			}

			return Errors.ErrorNoSuchFlag;
		}

		if (!realFlag.TypeRestrictions.Contains(obj.TypeString()))
		{
			if (notify)
			{
				await notifyService.Notify(executor, $"Flag: {realFlag.Name} cannot be set on object type: {obj.TypeString()}.");
			}

			return Errors.InvalidFlag;
		}
		
		// Check flag set/unset permissions
		var requiredPermissions = unset ? realFlag.UnsetPermissions : realFlag.SetPermissions;
		if (requiredPermissions is not null && requiredPermissions.Length > 0)
		{
			var hasPermission = false;
			foreach (var permission in requiredPermissions)
			{
				// Check if executor has the required flag or power
				if (await executor.HasFlag(permission) || await executor.HasPower(permission))
				{
					hasPermission = true;
					break;
				}
			}
			
			if (!hasPermission)
			{
				if (notify)
				{
					var action = unset ? "unset" : "set";
					await notifyService.Notify(executor, $"Permission denied: You lack the required permissions to {action} flag {realFlag.Name}.");
				}
				
				return Errors.ErrorPerm;
			}
		}

		switch (unset)
		{
			case true when !await obj.HasFlag(plainFlag):
			{
				if (notify)
				{
					await notifyService.Notify(executor, $"Flag: {realFlag.Name} (Already) Unset.");
				}

				break;
			}
			case true:
			{
				if (notify)
				{
					await notifyService.Notify(executor, $"Flag: {realFlag.Name} Unset.");
				}

				await mediator.Send(new UnsetObjectFlagCommand(obj, realFlag));

				// Publish notification for OBJECT`FLAG event
				await publisher.Publish(new ObjectFlagChangedNotification(
					obj,
					realFlag.Name,
					"FLAG",
					false, // IsSet = false (clearing)
					executor.Object().DBRef));

				break;
			}
			case false when await obj.HasFlag(plainFlag):
			{
				if (notify)
				{
					await notifyService.Notify(executor, $"Flag: {realFlag.Name} (Already) Set.");
				}

				break;
			}
			case false:
				if (notify)
				{
					await notifyService.Notify(executor, $"Flag: {realFlag.Name} Set.");
				}

				await mediator.Send(new SetObjectFlagCommand(obj, realFlag));

				// Publish notification for OBJECT`FLAG event
				await publisher.Publish(new ObjectFlagChangedNotification(
					obj,
					realFlag.Name,
					"FLAG",
					true, // IsSet = true (setting)
					executor.Object().DBRef));

				break;
		}

		return true;
	}

	public async ValueTask<CallState> SetPower(AnySharpObject executor, AnySharpObject obj, string powerOrPowerAlias,
		bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, "You do not control that object.");
			}
			return Errors.ErrorPerm;
		}

		if (await obj.HasPower(powerOrPowerAlias))
		{
			if (notify)
			{
				await notifyService.Notify(executor, $"Power: {powerOrPowerAlias} (Already) Set.");
			}
			return true;
		}
		
		var allPowers = mediator.CreateStream(new GetPowersQuery());
		
		var found = await allPowers
			.FirstOrDefaultAsync(x => 
				x.Name.Equals(powerOrPowerAlias, StringComparison.InvariantCultureIgnoreCase)  
				|| x.Alias.Equals(powerOrPowerAlias, StringComparison.InvariantCultureIgnoreCase));

		if (found is null)
		{
			if (notify)
			{
				await notifyService.Notify(executor, $"No such power exists: {powerOrPowerAlias}.");
			}
			return Errors.ErrorNoSuchPower;
		}

		if (notify)
		{
			await notifyService.Notify(executor, $"Power: {powerOrPowerAlias} Set.");
		}

		await mediator.Send(new SetObjectPowerCommand(obj, found));

		// Publish notification for OBJECT`FLAG event (powers trigger same event)
		await publisher.Publish(new ObjectFlagChangedNotification(
			obj,
			found.Name,
			"POWER",
			true, // IsSet = true (setting)
			executor.Object().DBRef));
		
		return true;
	}

	public async ValueTask<CallState> UnsetPower(AnySharpObject executor, AnySharpObject obj, string powerOrPowerAlias,
		bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, "You do not control that object.");
			}
			return Errors.ErrorPerm;
		}

		if (!await obj.HasPower(powerOrPowerAlias))
		{
			if (notify)
			{
				await notifyService.Notify(executor, $"Power: {powerOrPowerAlias} (Already) Unset.");
			}
			return true;
		}
		
		var allPowers = mediator.CreateStream(new GetPowersQuery());
		
		var found = await allPowers
			.FirstOrDefaultAsync(x => 
				x.Name.Equals(powerOrPowerAlias, StringComparison.InvariantCultureIgnoreCase)  
				|| x.Alias.Equals(powerOrPowerAlias, StringComparison.InvariantCultureIgnoreCase));

		if (found is null)
		{
			if (notify)
			{
				await notifyService.Notify(executor, $"No such power exists: {powerOrPowerAlias}.");
			}
			return Errors.ErrorNoSuchPower;
		}

		if (notify)
		{
			await notifyService.Notify(executor, $"Power: {powerOrPowerAlias} Unset.");
		}

		await mediator.Send(new UnsetObjectPowerCommand(obj, found));

		// Publish notification for OBJECT`FLAG event (powers trigger same event)
		await publisher.Publish(new ObjectFlagChangedNotification(
			obj,
			found.Name,
			"POWER",
			false, // IsSet = false (clearing)
			executor.Object().DBRef));
		
		return true;
	}

	public async ValueTask<CallState> ClearAllPowers(AnySharpObject executor, AnySharpObject obj, bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, "You do not control that object.");
			}
			return Errors.ErrorPerm;
		}

		// Early return if object has no powers
		if (!await obj.Object().Powers.Value.AnyAsync())
		{
			return true;
		}

		// Materialize the powers collection to avoid modification during iteration
		var objectPowers = await obj.Object().Powers.Value.ToArrayAsync();
		var powersCleared = 0;

		foreach (var power in objectPowers)
		{
			// Unset each power
			await mediator.Send(new UnsetObjectPowerCommand(obj, power));
			
			// Publish notification for each power cleared
			await publisher.Publish(new ObjectFlagChangedNotification(
				obj,
				power.Name,
				"POWER",
				false, // IsSet = false (clearing)
				executor.Object().DBRef));
			
			powersCleared++;
		}

		if (notify && powersCleared > 0)
		{
			await notifyService.Notify(executor, $"Cleared {powersCleared} power(s) from {obj.Object().Name}.");
		}

		return true;
	}

	public async ValueTask<CallState> SetOwner(AnySharpObject executor, AnySharpObject obj, SharpPlayer newOwner, bool notify)
	{
		if (!await permissionService.Controls(executor, obj) 
		    || !await permissionService.Controls(executor, newOwner))
		{
			if (notify)
			{
				await notifyService.Notify(executor, "You do not control that object.");
			}
			return Errors.ErrorPerm;
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
		// Allow if: executor controls newParent OR obj has LINK_OK OR executor passes Parent lock
		// Deny if: NOT(controls newParent) AND NOT(LINK_OK) AND NOT(passes Parent lock)
		var controls = await permissionService.Controls(executor, newParent);
		var hasLinkOk = await obj.HasFlag("LINK_OK");
		var passesLock = permissionService.PassesLock(executor, newParent, LockType.Parent);
		
		if (!controls && !hasLinkOk && !passesLock)
		{
			if (notify)
			{
				await notifyService.Notify(executor, "Permission denied.");
			}

			return Errors.ErrorPerm;
		}

		var safeToAdd = await HelperFunctions.SafeToAddParent(obj, newParent);
		
		if (!safeToAdd)
		{
			if (notify)
			{
				await notifyService.Notify(executor, "Cannot add parent to loop.");
			}

			return Errors.ParentLoop;
		}

		await mediator.Send(new SetObjectParentCommand(obj, newParent));

		if (notify)
		{
			await notifyService.Notify(executor, $"Parent set.");
		}

		return true;
	}

	public async ValueTask<CallState> UnsetParent(AnySharpObject executor, AnySharpObject obj, bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			await notifyService.Notify(executor, "Permission denied.");
			return Errors.ErrorPerm;
		}
		
		await mediator.Send(new UnsetObjectParentCommand(obj));
		
		return true;
	}

	public async ValueTask<CallState> SetZone(AnySharpObject executor, AnySharpObject obj, AnySharpObject newZone,
		bool notify)
	{
		// Check if executor controls the object
		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, "Permission denied.");
			}

			return Errors.ErrorPerm;
		}

		var safeToAdd = await HelperFunctions.SafeToAddZone(obj, newZone);
		
		if (!safeToAdd)
		{
			if (notify)
			{
				await notifyService.Notify(executor, "Cannot add zone: would create a cycle.");
			}

			return Errors.ZoneLoop;
		}

		await mediator.Send(new SetObjectZoneCommand(obj, newZone));

		if (notify)
		{
			await notifyService.Notify(executor, $"Zone set.");
		}

		return true;
	}

	public async ValueTask<CallState> UnsetZone(AnySharpObject executor, AnySharpObject obj, bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, "Permission denied.");
			}
			return Errors.ErrorPerm;
		}
		
		await mediator.Send(new UnsetObjectZoneCommand(obj));
		
		if (notify)
		{
			await notifyService.Notify(executor, "Zone cleared.");
		}
		
		return true;
	}
}