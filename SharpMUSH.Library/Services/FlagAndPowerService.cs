using Mediator;
using SharpMUSH.Library.Authorization;
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

/// <summary>
/// Sets and clears an object's flags and powers, under PennMUSH's <c>can_set_flag</c> and <c>set_power</c>
/// rules. WIZARD, ROYALTY and the built-in powers are roles and overrides (<see cref="RoleFlags"/>,
/// <see cref="GamePowers"/>): setting one goes through <see cref="IRoleManagementService"/>, under the
/// same rules as <c>@role</c>, and keeps PennMUSH's messages.
/// </summary>
public class FlagAndPowerService(
	IMediator mediator,
	IPermissionService permissionService,
	INotifyService notifyService,
	IPublisher publisher,
	IRoleManagementService roles)
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

		if (RoleFlags.Find(realFlag.Name) is { } roleFlag)
		{
			return await SetOrUnsetRoleFlag(executor, obj, realFlag, roleFlag, unset, notify);
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
	/// <c>@set</c> on WIZARD or ROYALTY: assigns or removes the object's role, under the role rules in
	/// place of the flag's set permissions. PennMUSH's messages are kept; a refusal is its
	/// "Permission denied."
	/// </summary>
	private async ValueTask<CallState> SetOrUnsetRoleFlag(AnySharpObject executor, AnySharpObject obj,
		SharpObjectFlag realFlag, RoleFlags.Flag roleFlag, bool unset, bool notify)
	{
		var name = obj.Object().Name;
		if (unset ? !await obj.HasFlag(realFlag.Name) : await obj.HasFlag(realFlag.Name))
		{
			if (notify)
			{
				await notifyService.Notify(executor, string.Format(
					unset ? Definitions.ErrorMessages.Notifications.FlagAlreadyReset : Definitions.ErrorMessages.Notifications.FlagAlreadySet,
					name, realFlag.Name));
			}

			return true;
		}

		var outcome = unset
			? await roles.UnassignFromObjectAsync(executor, obj, roleFlag.Role)
			: await roles.AssignToObjectAsync(executor, obj, roleFlag.Role);
		if (outcome is RoleRefusal)
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.PermissionDenied);
			}

			return ErrorMessages.Returns.PermissionDenied;
		}

		if (notify)
		{
			await notifyService.Notify(executor, string.Format(
				unset ? Definitions.ErrorMessages.Notifications.FlagReset : Definitions.ErrorMessages.Notifications.FlagSet,
				name, realFlag.Name));
			if (unset)
			{
				await NotifyIfStillHeld(executor, obj, roleFlag.Scope, roleFlag.Role);
			}
		}

		await publisher.Publish(new ObjectFlagChangedNotification(obj, realFlag.Name, "FLAG", !unset, executor.Object().DBRef));
		return true;
	}

	/// <summary>
	/// <c>@power</c> on a built-in power: the Guest and Builder powers assign or remove the role, every
	/// other one sets or clears an Allow override on its scope. A refusal is PennMUSH's
	/// "Permission denied."
	/// </summary>
	private async ValueTask<bool> ChangeGamePower(AnySharpObject executor, AnySharpObject obj, GamePowers.Power power, bool grant)
	{
		var outcome = power.Role is { } role
			? grant
				? await roles.AssignToObjectAsync(executor, obj, role)
				: await roles.UnassignFromObjectAsync(executor, obj, role)
			: await roles.SetObjectOverridesAsync(executor, obj, [power.Scope], grant ? PermissionState.Allow : PermissionState.Inherit);
		return outcome is not RoleRefusal;
	}

	/// <summary>
	/// After a role or override came off a character, says so when its account still grants the same
	/// thing: the command acted, but what softcode sees has not changed.
	/// </summary>
	private async ValueTask NotifyIfStillHeld(AnySharpObject executor, AnySharpObject obj, string scope, string what)
	{
		var grants = await obj.Object().Grants.WithCancellation(ExecutionBudget.CurrentToken);
		if (grants.Shows(scope)
				&& (grants.Roles.Any(r => r.Source == RoleSource.Account && PermissionResolver.StateOf(r.Role.Permissions, scope) == PermissionState.Allow)
						|| PermissionResolver.StateOf(grants.Context.Overrides, scope) == PermissionState.Allow))
		{
			await notifyService.Notify(executor,
				string.Format(Definitions.ErrorMessages.Notifications.StillHeldThroughAccount, obj.Object().Name, what));
		}
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

		if (GamePowers.Find(found.Name) is { } gamePower)
		{
			if (!await ChangeGamePower(executor, obj, gamePower, grant: true))
			{
				return await PowerRefused(executor, notify, Definitions.ErrorMessages.Notifications.PermissionDenied);
			}
		}
		else if (!await executor.IsWizard())
		{
			return await PowerRefused(executor, notify, Definitions.ErrorMessages.Notifications.OnlyWizardsMayGrantPowers);
		}
		else
		{
			await mediator.Send(new SetObjectPowerCommand(obj, found));
		}

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

		var gamePower = GamePowers.Find(found.Name);
		if (gamePower is not null)
		{
			if (!await ChangeGamePower(executor, obj, gamePower, grant: false))
			{
				return await PowerRefused(executor, notify, Definitions.ErrorMessages.Notifications.PermissionDenied);
			}
		}
		else if (!await executor.IsWizard())
		{
			return await PowerRefused(executor, notify, Definitions.ErrorMessages.Notifications.OnlyWizardsMayGrantPowers);
		}
		else
		{
			await mediator.Send(new UnsetObjectPowerCommand(obj, found));
		}

		if (notify && !await obj.Object().AreQuietAsync(executor))
		{
			await notifyService.Notify(executor,
				string.Format(Definitions.ErrorMessages.Notifications.PowerRemoved, obj.Object().Name, found.Name));
			if (gamePower is not null)
			{
				await NotifyIfStillHeld(executor, obj, gamePower.Scope, gamePower.Name);
			}
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

	private async ValueTask<CallState> PowerRefused(AnySharpObject executor, bool notify, string message)
	{
		if (notify)
		{
			await notifyService.Notify(executor, message);
		}

		return ErrorMessages.Returns.PermissionDenied;
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
		var principals = permissions
			.Where(permission => permission.ToLowerInvariant() is not ("dark" or "mdark" or "odark" or "log" or "event"));
		foreach (var permission in principals)
		{
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

		return false; // no additional restriction
	}
}
