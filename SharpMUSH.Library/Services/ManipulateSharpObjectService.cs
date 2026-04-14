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
	ISharpDatabase database,
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
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.YouDoNotControlThatObject);
			}

			return Errors.ErrorPerm;
		}

		if (!await validateService.Valid(IValidateService.ValidationType.Name, name, obj))
		{
			if (notify)
			{
				await notifyService.NotifyLocalized(executor, nameof(Definitions.ErrorMessages.Notifications.CannotNameObjectFormat), executor, name.ToPlainText());
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
					await notifyService.NotifyLocalized(executor, nameof(Definitions.ErrorMessages.Notifications.PlayerNameInUse), executor);
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
					await notifyService.NotifyLocalized(executor, nameof(Definitions.ErrorMessages.Notifications.PlayerAliasInUse), executor);
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
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.YouDoNotControlThatObject);
			}

			return Errors.ErrorPerm;
		}

		if (!await validateService.Valid(IValidateService.ValidationType.Password, MModule.single(newPassword), new None()))
		{
			if (notify)
			{
				await notifyService.NotifyLocalized(executor, nameof(Definitions.ErrorMessages.Notifications.InvalidPasswordText), executor);
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
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.YouDoNotControlThatObject);
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
				await notifyService.Notify(executor,
					string.Format(Definitions.ErrorMessages.Notifications.DontRecognizeFlag, obj.Object().Name));
			}

			return Errors.ErrorNoSuchFlag;
		}

		if (!realFlag.TypeRestrictions.Contains(obj.TypeString()))
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.PermissionDenied);
			}

			return Errors.InvalidFlag;
		}

		// Generic flag permission check, matching PennMUSH's can_set_flag_generic().
		// Permission levels: trusted, royalty, wizard, god (see help "flag permissions").
		var requiredPermissions = unset ? realFlag.UnsetPermissions : realFlag.SetPermissions;
		if (requiredPermissions is not null && requiredPermissions.Length > 0)
		{
			var hasPermission = await requiredPermissions.ToAsyncEnumerable()
				.AnyAsync(async (permission, _) => await HasFlagPermission(executor, obj, permission));

			if (!hasPermission)
			{
				if (notify)
				{
					await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.PermissionDenied);
				}

				return Errors.ErrorPerm;
			}
		}

		// Flag-specific permission checks, matching PennMUSH's can_set_flag().
		// These additional restrictions apply on top of the generic permission check.
		var flagSpecificDenied = await CheckFlagSpecificPermissions(executor, obj, realFlag, unset);
		if (flagSpecificDenied)
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.PermissionDenied);
			}

			return Errors.ErrorPerm;
		}

		switch (unset)
		{
			case true when !await obj.HasFlag(realFlag.Name):
				{
					if (notify)
					{
						await notifyService.Notify(executor,
							string.Format(Definitions.ErrorMessages.Notifications.FlagAlreadyReset, obj.Object().Name, realFlag.Name));
					}

					break;
				}
			case true:
				{
					if (notify)
					{
						await notifyService.Notify(executor,
							string.Format(Definitions.ErrorMessages.Notifications.FlagReset, obj.Object().Name, realFlag.Name));
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
			case false when await obj.HasFlag(realFlag.Name):
				{
					if (notify)
					{
						await notifyService.Notify(executor,
							string.Format(Definitions.ErrorMessages.Notifications.FlagAlreadySet, obj.Object().Name, realFlag.Name));
					}

					break;
				}
			case false:
				if (notify)
				{
					await notifyService.Notify(executor,
						string.Format(Definitions.ErrorMessages.Notifications.FlagSet, obj.Object().Name, realFlag.Name));
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
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.YouDoNotControlThatObject);
			}
			return Errors.ErrorPerm;
		}

		// God protection: non-God cannot modify God's powers (PennMUSH src/flags.c)
		if (obj.IsGod() && !executor.IsGod())
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.WhoDoYouThinkYouAre);
			}
			return Errors.ErrorPerm;
		}

		// Can't make admin (Wizard/Royalty) into guests (PennMUSH src/flags.c)
		if (powerOrPowerAlias.Equals("Guest", StringComparison.OrdinalIgnoreCase)
			&& (await obj.IsWizard() || await obj.IsRoyalty()))
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.CantMakeAdminGuests);
			}
			return Errors.ErrorPerm;
		}

		if (await obj.HasPower(powerOrPowerAlias))
		{
			if (notify)
			{
				await notifyService.Notify(executor,
					string.Format(Definitions.ErrorMessages.Notifications.PowerAlreadySet, obj.Object().Name, powerOrPowerAlias));
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
				await notifyService.Notify(executor,
					string.Format(Definitions.ErrorMessages.Notifications.DontRecognizePower, obj.Object().Name));
			}
			return Errors.ErrorNoSuchPower;
		}

		if (notify)
		{
			await notifyService.Notify(executor,
				string.Format(Definitions.ErrorMessages.Notifications.PowerSet, obj.Object().Name, powerOrPowerAlias));
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
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.YouDoNotControlThatObject);
			}
			return Errors.ErrorPerm;
		}

		// God protection: non-God cannot modify God's powers (PennMUSH src/flags.c)
		if (obj.IsGod() && !executor.IsGod())
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.WhoDoYouThinkYouAre);
			}
			return Errors.ErrorPerm;
		}

		if (!await obj.HasPower(powerOrPowerAlias))
		{
			if (notify)
			{
				await notifyService.Notify(executor,
					string.Format(Definitions.ErrorMessages.Notifications.FlagAlreadyReset, obj.Object().Name, powerOrPowerAlias));
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
				await notifyService.Notify(executor,
					string.Format(Definitions.ErrorMessages.Notifications.DontRecognizePower, obj.Object().Name));
			}
			return Errors.ErrorNoSuchPower;
		}

		if (notify)
		{
			await notifyService.Notify(executor,
				string.Format(Definitions.ErrorMessages.Notifications.FlagReset, obj.Object().Name, powerOrPowerAlias));
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
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.YouDoNotControlThatObject);
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
			await notifyService.Notify(executor, string.Format(Definitions.ErrorMessages.Notifications.ClearedPowersFromFormat, powersCleared, obj.Object().Name));
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
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.YouDoNotControlThatObject);
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
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.PermissionDenied);
			}

			return Errors.ErrorPerm;
		}

		var safeToAdd = await HelperFunctions.SafeToAddParent(mediator, database, obj, newParent);

		if (!safeToAdd)
		{
			if (notify)
			{
				await notifyService.NotifyLocalized(executor, nameof(Definitions.ErrorMessages.Notifications.ParentLoopCannotAdd), executor);
			}

			return Errors.ParentLoop;
		}

		await mediator.Send(new SetObjectParentCommand(obj, newParent));

		if (notify)
		{
			await notifyService.NotifyLocalized(executor, nameof(Definitions.ErrorMessages.Notifications.ParentSet), executor);
		}

		return true;
	}

	public async ValueTask<CallState> UnsetParent(AnySharpObject executor, AnySharpObject obj, bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.PermissionDenied);
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
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.PermissionDenied);
			}

			return Errors.ErrorPerm;
		}

		var safeToAdd = await HelperFunctions.SafeToAddZone(mediator, database, obj, newZone);

		if (!safeToAdd)
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.ZoneCycleCannotAdd);
			}

			return Errors.ZoneLoop;
		}

		await mediator.Send(new SetObjectZoneCommand(obj, newZone));

		if (notify)
		{
			await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.ZoneSet);
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
			return Errors.ErrorPerm;
		}

		await mediator.Send(new UnsetObjectZoneCommand(obj));

		if (notify)
		{
			await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.ZoneCleared);
		}

		return true;
	}

	/// <summary>
	/// Resolves a named flag permission level to the appropriate privilege check.
	/// Matches PennMUSH's can_set_flag_generic() logic from flags.c.
	/// See help "flag permissions" for the documented permission levels.
	/// </summary>
	private static async ValueTask<bool> HasFlagPermission(AnySharpObject executor, AnySharpObject obj, string permission) =>
		permission.ToLowerInvariant() switch
		{
			// F_INHERIT: Wizard(player) || (Inheritable(player) && Owns(player, thing))
			"trusted" => await executor.IsWizard()
				|| (await executor.Inheritable() && await executor.Owns(obj)),
			// F_ROYAL: Hasprivs(player) = IsPriv
			"royalty" => await executor.IsPriv(),
			// F_WIZARD: Wizard(player)
			"wizard" => await executor.IsWizard(),
			// F_GOD: God(player)
			"god" => executor.IsGod(),
			_ => await executor.HasFlag(permission) || await executor.HasPower(permission)
		};

	/// <summary>
	/// Checks flag-specific permission restrictions beyond the generic permission check.
	/// Matches PennMUSH's can_set_flag() logic from flags.c.
	/// Returns true if the operation should be DENIED.
	/// </summary>
	private static async ValueTask<bool> CheckFlagSpecificPermissions(
		AnySharpObject executor, AnySharpObject obj, SharpObjectFlag flag, bool negate)
	{
		var flagName = flag.Name.ToUpperInvariant();

		// God protection: non-God cannot modify God's flags at all (PennMUSH src/flags.c)
		if (obj.IsGod() && !executor.IsGod())
			return true; // deny

		// CHOWN_OK and DESTROY_OK: must own the target or be Wizard
		if (flagName is "CHOWN_OK" or "DESTROY_OK")
		{
			return !(await executor.Owns(obj) || await executor.IsWizard());
		}

		// Can't gag wizards/God, but can ungag them
		if (flagName == "GAGGED" && await obj.IsWizard())
			return !negate; // deny setting, allow unsetting

		// God can do (almost) anything after the generic check passes
		if (executor.IsGod())
			return false;

		// WIZARD flag: special restrictions
		if (flagName == "WIZARD")
		{
			if (!negate)
			{
				// Setting WIZARD: must be Wizard, own the target, and target must not be a player
				return !(await executor.IsWizard() && await executor.Owns(obj) && !obj.IsPlayer);
			}
			else
			{
				// Unsetting WIZARD: must be Wizard and target must not be a player
				return !(await executor.IsWizard() && !obj.IsPlayer);
			}
		}

		// ROYALTY flag: special restrictions
		if (flagName == "ROYALTY")
		{
			// Must not be guest target, and either Wizard or (Royalty + owns + not player)
			return await obj.IsGuest()
				|| !(await executor.IsWizard()
					|| (await executor.IsRoyalty() && await executor.Owns(obj) && !obj.IsPlayer));
		}

		return false; // no additional restriction
	}
}