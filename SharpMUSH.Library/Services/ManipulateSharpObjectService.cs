using DotNext.Collections.Generic;
using Mediator;
using OneOf.Types;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using AsyncEnumerable = System.Linq.AsyncEnumerable;

namespace SharpMUSH.Library.Services;

public class ManipulateSharpObjectService(
	IMediator mediator,
	IPermissionService permissionService,
	IPasswordService passwordService,
	IValidateService validateService,
	INotifyService notifyService,
	IAttributeService attributeService)
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
				var tryFindPlayerByName = await AsyncEnumerable.ToArrayAsync((await mediator.Send(new GetPlayerQuery(name.ToPlainText()))));
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

		if (!await validateService.Valid(IValidateService.ValidationType.Password, MModule.single(newPassword)))
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

		var realFlag = await mediator.Send(new GetObjectFlagQuery(plainFlag));
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
				await notifyService.Notify(executor, $"Flag: {realFlag} cannot be set on object type: {obj.TypeString()}.");
			}

			return Errors.InvalidFlag;
		}
		
		// TODO: Flag Restrictions based on ownership, permissions, etc.
		// if(realFlag.SetPermissions) 
		// if(realFlag.UnsetPermissions)

		switch (unset)
		{
			case true when !await obj.HasFlag(plainFlag):
			{
				if (notify)
				{
					await notifyService.Notify(executor, $"Flag: {realFlag} (Already) Unset.");
				}

				break;
			}
			case true:
			{
				if (notify)
				{
					await notifyService.Notify(executor, $"Flag: {realFlag} Unset.");
				}

				await mediator.Send(new UnsetObjectFlagCommand(obj, realFlag));

				break;
			}
			case false when await obj.HasFlag(plainFlag):
			{
				if (notify)
				{
					await notifyService.Notify(executor, $"Flag: {realFlag} (Already) Set.");
				}

				break;
			}
			case false:
				if (notify)
				{
					await notifyService.Notify(executor, $"Flag: {realFlag} Set.");
				}

				await mediator.Send(new SetObjectFlagCommand(obj, realFlag));

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
		
		var allPowers = await mediator.Send(new GetPowersQuery());
		
		var found = allPowers
			.FirstOrDefault(x => 
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
		
		var allPowers = await mediator.Send(new GetPowersQuery());
		
		var found = allPowers
			.FirstOrDefault(x => 
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
		
		return true;
	}

	public ValueTask<CallState> SetOwner(AnySharpObject executor, AnySharpObject obj, SharpPlayer newOwner, bool notify)
	{
		throw new NotImplementedException();
	}

	public async ValueTask<CallState> SetParent(AnySharpObject executor, AnySharpObject obj, AnySharpObject newParent,
		bool notify)
	{
		if (!await permissionService.Controls(executor, newParent)
		    || (!await obj.HasFlag("LINK_OK")
		        && !permissionService.PassesLock(executor, newParent, LockType.Parent)))
		{
			if (notify)
			{
				await notifyService.Notify(executor, Errors.ErrorPerm);
			}

			return Errors.ErrorPerm;
		}

		if (!await HelperFunctions.SafeToAddParent(obj, newParent))
		{
			if (notify)
			{
				await notifyService.Notify(executor, "Cannot add parent to loop.");
			}

			return Errors.ParentLoop;
		}

		await mediator.Send(new SetObjectParentCommand(obj, newParent));

		return true;
	}

	public async ValueTask<CallState> UnsetParent(AnySharpObject executor, AnySharpObject obj, bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			await notifyService.Notify(executor, Errors.ErrorPerm);
			return Errors.ErrorPerm;
		}
		
		await mediator.Send(new UnsetObjectParentCommand(obj));
		
		return true;
	}
}