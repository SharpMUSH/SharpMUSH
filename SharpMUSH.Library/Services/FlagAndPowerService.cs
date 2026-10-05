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

/// <summary>Sets and clears an object's flags and powers, under PennMUSH's <c>can_set_flag</c> and <c>set_power</c> rules.</summary>
public class FlagAndPowerService(
	IMediator mediator,
	IPermissionService permissionService,
	INotifyService notifyService,
	IPublisher publisher)
	: IFlagAndPowerService
{
	public async ValueTask<CallState> SetOrUnsetFlag(AnySharpObject executor, AnySharpObject obj, string flagOrFlagAlias,
		bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.YouDoNotControlThatObject);
			}

			return ErrorMessages.Returns.PermissionDenied;
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

			return ErrorMessages.Returns.NoSuchFlag;
		}

		if (!realFlag.TypeRestrictions.Contains(obj.TypeString()))
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.PermissionDenied);
			}

			return ErrorMessages.Returns.InvalidFlag;
		}

		// Visibility/effect metadata does not grant or require a privilege.
		var requiredPermissions = (unset ? realFlag.UnsetPermissions : realFlag.SetPermissions) ?? [];
		if (realFlag.Disabled || !await HasAnyFlagPermission(executor, obj, requiredPermissions))
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.PermissionDenied);
			}

			return ErrorMessages.Returns.PermissionDenied;
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

			return ErrorMessages.Returns.PermissionDenied;
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

					await publisher.Publish(new ObjectFlagChangedNotification(
						obj,
						realFlag.Name,
						"FLAG",
						false,
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

				await publisher.Publish(new ObjectFlagChangedNotification(
					obj,
					realFlag.Name,
					"FLAG",
					true,
					executor.Object().DBRef));

				break;
		}

		return true;
	}

	/// <summary>
	/// Resolves a power by name or alias.
	/// </summary>
	public ValueTask<SharpPower?> FindPower(string powerOrPowerAlias) =>
		mediator.CreateStream(new GetPowersQuery())
			.FirstOrDefaultAsync(x => x.AnswersTo(powerOrPowerAlias));

	/// <summary>
	/// PennMUSH src/wiz.c do_power: the shared body of <c>@power &lt;object&gt;=...</c> and the side-effect
	/// form of <c>powers()</c>.
	/// </summary>
	public async ValueTask<CallState> SetOrUnsetPowers(AnySharpObject executor, AnySharpObject obj,
		string powerSpecification, bool notify)
	{
		if (!await executor.IsWizard())
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.OnlyWizardsMayGrantPowers);
			}
			return ErrorMessages.Returns.PermissionDenied;
		}

		if (await obj.HasFlag("UNREGISTERED"))
		{
			if (notify)
			{
				await notifyService.Notify(executor,
					Definitions.ErrorMessages.Notifications.CantGrantPowersUnregistered);
			}
			return ErrorMessages.Returns.PermissionDenied;
		}

		if (obj.IsGod() && !executor.IsGod())
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.GodIsAlreadyAllPowerful);
			}
			return ErrorMessages.Returns.PermissionDenied;
		}

		var tokens = powerSpecification.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		if (tokens.Length == 0)
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.MustSpecifyPowerToSet);
			}
			return ErrorMessages.Returns.PermissionDenied;
		}

		CallState last = true;
		foreach (var token in tokens)
		{
			// A bare "!" is not a revoke: do_power only strips the token when something follows it.
			var revoke = token[0] == '!' && token.Length > 1;
			var powerName = revoke ? token[1..] : token;

			last = revoke
				? await UnsetPower(executor, obj, powerName, notify)
				: await SetPower(executor, obj, powerName, notify);
		}

		return last;
	}

	public async ValueTask<CallState> SetPower(AnySharpObject executor, AnySharpObject obj, string powerOrPowerAlias,
		bool notify)
	{
		// set_power resolves the power before any permission check, and names the *power* when it cannot.
		var found = await FindPower(powerOrPowerAlias);

		if (found is null)
		{
			if (notify)
			{
				await notifyService.Notify(executor,
					string.Format(Definitions.ErrorMessages.Notifications.DontRecognizePower, powerOrPowerAlias));
			}
			return ErrorMessages.Returns.NoSuchPower;
		}

		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.YouDoNotControlThatObject);
			}
			return ErrorMessages.Returns.PermissionDenied;
		}

		// God protection: non-God cannot modify God's powers (PennMUSH src/flags.c)
		if (obj.IsGod() && !executor.IsGod())
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.WhoDoYouThinkYouAre);
			}
			return ErrorMessages.Returns.PermissionDenied;
		}

		// Can't make admin (Wizard/Royalty) into guests (PennMUSH src/flags.c)
		if (found.Name.Equals("Guest", StringComparison.OrdinalIgnoreCase)
			&& (await obj.IsWizard() || await obj.IsRoyalty()))
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.CantMakeAdminGuests);
			}
			return ErrorMessages.Returns.PermissionDenied;
		}

		// set_power reports all four outcomes only when !AreQuiet(player, thing) (src/flags.c:1978).
		if (await obj.HasPower(found.Name))
		{
			if (notify && !await obj.Object().AreQuietAsync(executor))
			{
				await notifyService.Notify(executor,
					string.Format(Definitions.ErrorMessages.Notifications.PowerAlreadyGranted, obj.Object().Name, found.Name));
			}
			return true;
		}

		await mediator.Send(new SetObjectPowerCommand(obj, found));

		if (notify && !await obj.Object().AreQuietAsync(executor))
		{
			await notifyService.Notify(executor,
				string.Format(Definitions.ErrorMessages.Notifications.PowerGranted, obj.Object().Name, found.Name));
		}

		// Powers trigger the same OBJECT`FLAG event as flags.
		await publisher.Publish(new ObjectFlagChangedNotification(
			obj,
			found.Name,
			"POWER",
			true,
			executor.Object().DBRef));

		return true;
	}

	public async ValueTask<CallState> UnsetPower(AnySharpObject executor, AnySharpObject obj, string powerOrPowerAlias,
		bool notify)
	{
		var found = await FindPower(powerOrPowerAlias);

		if (found is null)
		{
			if (notify)
			{
				await notifyService.Notify(executor,
					string.Format(Definitions.ErrorMessages.Notifications.DontRecognizePower, powerOrPowerAlias));
			}
			return ErrorMessages.Returns.NoSuchPower;
		}

		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.YouDoNotControlThatObject);
			}
			return ErrorMessages.Returns.PermissionDenied;
		}

		// God protection: non-God cannot modify God's powers (PennMUSH src/flags.c)
		if (obj.IsGod() && !executor.IsGod())
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.WhoDoYouThinkYouAre);
			}
			return ErrorMessages.Returns.PermissionDenied;
		}

		if (!await obj.HasPower(found.Name))
		{
			if (notify && !await obj.Object().AreQuietAsync(executor))
			{
				await notifyService.Notify(executor,
					string.Format(Definitions.ErrorMessages.Notifications.PowerAlreadyRemoved, obj.Object().Name, found.Name));
			}
			return true;
		}

		await mediator.Send(new UnsetObjectPowerCommand(obj, found));

		if (notify && !await obj.Object().AreQuietAsync(executor))
		{
			await notifyService.Notify(executor,
				string.Format(Definitions.ErrorMessages.Notifications.PowerRemoved, obj.Object().Name, found.Name));
		}

		// Powers trigger the same OBJECT`FLAG event as flags.
		await publisher.Publish(new ObjectFlagChangedNotification(
			obj,
			found.Name,
			"POWER",
			false,
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
			return ErrorMessages.Returns.PermissionDenied;
		}

		// Materialized: the loop below removes powers from the collection being read.
		var objectPowers = await obj.Object().Powers.Value.ToArrayAsync();
		if (objectPowers.Length == 0)
		{
			return true;
		}

		foreach (var power in objectPowers)
		{
			await mediator.Send(new UnsetObjectPowerCommand(obj, power));

			await publisher.Publish(new ObjectFlagChangedNotification(
				obj,
				power.Name,
				"POWER",
				false,
				executor.Object().DBRef));
		}

		if (notify)
		{
			await notifyService.NotifyLocalized(executor, nameof(Definitions.ErrorMessages.Notifications.ClearedPowersFromFormat), executor, objectPowers.Length, obj.Object().Name);
		}

		return true;
	}

	/// <summary>Checks operation prohibitions before the remaining principal alternatives; metadata alone imposes no privilege requirement.</summary>
	private static async ValueTask<bool> HasAnyFlagPermission(AnySharpObject executor, AnySharpObject obj, string[] permissions)
	{
		if (permissions.Any(permission => permission.ToLowerInvariant() is "internal" or "disabled"))
			return false;

		var hasPrincipal = false;
		foreach (var permission in permissions)
		{
			if (permission.ToLowerInvariant() is "dark" or "mdark" or "odark" or "log" or "event")
				continue;
			hasPrincipal = true;
			if (await HasFlagPermission(executor, obj, permission))
				return true;
		}
		return !hasPrincipal;
	}

	/// <summary>
	/// Resolves a named flag permission level to the appropriate privilege check.
	/// Built-in privilege levels follow PennMUSH; custom flag and power names are SharpMUSH alternatives.
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
