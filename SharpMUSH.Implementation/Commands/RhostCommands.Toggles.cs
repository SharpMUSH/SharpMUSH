using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>RhostMUSH's toggle commands, over the flags its toggles are here (<see cref="RhostNameTables.Toggles"/>).</summary>
public partial class Commands
{
	/// <summary>
	/// RhostMUSH's <c>@toggle</c>: <c>@toggle &lt;object&gt;=[!]&lt;toggle&gt; ...</c> sets and clears toggles as
	/// <c>@set</c> does flags; <c>/check [&lt;object&gt;]</c>, or no list, lists an object's toggles, and
	/// <c>/clear &lt;object&gt;</c> removes those the caller can see. Each form needs control of the object.
	/// </summary>
	[SharpCommand(Name = "@TOGGLE", Switches = ["CHECK", "CLEAR"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["object", "toggles"])]
	public async ValueTask<Option<CallState>> ToggleCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var target = args.TryGetValue("0", out var first) ? first.Message!.ToPlainText().Trim() : string.Empty;
		var toggles = args.TryGetValue("1", out var second) ? second.Message!.ToPlainText().Trim() : string.Empty;
		var switches = parser.CurrentState.Switches.ToArray();

		switch (switches)
		{
			case ["CHECK"]:
			case [] when target.Length > 0 && toggles.Length == 0:
				return await OnControlledAsync(parser, executor, target.Length == 0 ? "me" : target, async found =>
				{
					var list = string.Join(' ', RhostToggleNames.ToggleNamesOf(await RhostToggleNames.VisibleFlagNamesAsync(executor, found, ConnectionService)));
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TogglesListFormat), executor, list);
					return new CallState(list);
				});
			case ["CLEAR"]:
				return await OnControlledAsync(parser, executor, target, async found =>
				{
					var held = RhostToggleNames.ToggleNamesOf(await RhostToggleNames.VisibleFlagNamesAsync(executor, found, ConnectionService))
						.Select(name => $"!{RhostNameTables.Toggle(name)!.Flag}")
						.ToList();
					if (held.Count > 0)
					{
						await SetHelpers.DoSet(parser, LocateService, AttributeService, FlagAndPowerService, NotifyService,
							PermissionService, executor, MString.Plain($"#{found.Object().DBRef.Number}"),
							MString.Plain(string.Join(' ', held)), quiet: true);
					}

					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TogglesCleared), executor);
					return CallState.Empty;
				});
			case []:
				break;
			default:
				return new CallState(ErrorMessages.Returns.TooManySwitches);
		}

		var (flags, unknown) = RhostToggleNames.Translate(toggles, await RhostToggleNames.DefinedFlagsAsync(Mediator));
		if (target.Length == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ToggleNeedsList), executor);
			return CallState.Empty;
		}

		foreach (var name in unknown)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoSuchToggleFormat), executor, name);
		}

		return flags.Count == 0
			? CallState.Empty
			: await SetHelpers.DoSet(parser, LocateService, AttributeService, FlagAndPowerService, NotifyService, PermissionService,
				executor, MString.Plain(target), MString.Plain(string.Join(' ', flags)));
	}

	/// <summary>Runs <paramref name="action"/> on the object <paramref name="name"/> finds, when the executor controls it.</summary>
	private async ValueTask<CallState> OnControlledAsync(IMUSHCodeParser parser, AnySharpObject executor, string name,
		Func<AnySharpObject, ValueTask<CallState>> action)
		=> await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, name,
			LocateFlags.All, async found =>
			{
				if (await PermissionService.Controls(executor, found))
				{
					return await action(found);
				}

				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDeniedNotControlledMatch),
					executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			});

	/// <summary>
	/// RhostMUSH's <c>@toggledef</c>: with no switch or <c>/list</c>, each toggle with who may set and clear it;
	/// <c>/set</c>, <c>/unset</c> and <c>/see</c> change that on the toggle's flag, taking RhostMUSH's permission words.
	/// </summary>
	[SharpCommand(Name = "@TOGGLEDEF", Switches = ["LIST", "SET", "UNSET", "SEE", "TYPE", "LETTER", "INDEX"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 2, ParameterNames = ["toggle", "permissions"])]
	public async ValueTask<Option<CallState>> ToggleDef(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var name = args.TryGetValue("0", out var first) ? first.Message!.ToPlainText().Trim() : string.Empty;
		var words = args.TryGetValue("1", out var second) ? second.Message!.ToPlainText() : string.Empty;
		var switches = parser.CurrentState.Switches.ToArray();

		if (!await executor.Can(PortalPermission.ConfigAdmin))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDeniedNeedsPermission),
				executor, PortalPermission.ConfigAdmin);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		switch (switches)
		{
			case [] or ["LIST"] or ["INDEX"]:
				return await ListToggleDefinitionsAsync(executor, name);
			case ["SET"] or ["UNSET"] or ["SEE"]:
				break;
			case ["TYPE"] or ["LETTER"]:
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ToggleDefSwitchUnsupported), executor);
				return CallState.Empty;
			default:
				return new CallState(ErrorMessages.Returns.TooManySwitches);
		}

		if (name.Length == 0 || words.Trim().Length == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ToggleDefNeedsPermissions), executor);
			return CallState.Empty;
		}

		if (RhostNameTables.Toggle(name) is not { Flag: { } flagName }
			|| await Mediator.Send(new GetObjectFlagQuery(flagName)) is not { } flag)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoSuchToggleFormat), executor, name);
			return CallState.Empty;
		}

		// A system flag's permissions are the seed's, as @flag holds; a restart would put them back.
		if (flag.System)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotModifySystemFlagFormat),
				executor, flag.Name);
			return CallState.Empty;
		}

		var changed = switches switch
		{
			["SET"] => RhostPermissions.Apply(flag.SetPermissions, words) is { } set ? (set, flag.UnsetPermissions) : default,
			["UNSET"] => RhostPermissions.Apply(flag.UnsetPermissions, words) is { } unset ? (flag.SetPermissions, unset) : default,
			_ => RhostPermissions.ApplySee(flag.SetPermissions, words) is { } seen ? (seen, flag.UnsetPermissions) : default
		};
		if (changed is not ({ } setPermissions, { } unsetPermissions))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ToggleDefNothingToDo), executor);
			return CallState.Empty;
		}

		await Mediator.Send(new UpdateObjectFlagCommand(flag.Name, flag.Aliases ?? [], flag.Symbol, setPermissions,
			unsetPermissions, flag.TypeRestrictions));
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ToggleDefUpdatedFormat), executor,
			name.ToUpperInvariant());
		return CallState.Empty;
	}

	private async ValueTask<CallState> ListToggleDefinitionsAsync(AnySharpObject executor, string pattern)
	{
		var flags = await Mediator.CreateStream(new GetAllObjectFlagsQuery())
			.ToDictionaryAsync(flag => flag.Name, StringComparer.OrdinalIgnoreCase);
		foreach (var toggle in RhostNameTables.Toggles.OrderBy(toggle => toggle.Name, StringComparer.Ordinal))
		{
			if (toggle.Flag is not { } name || flags.GetValueOrDefault(name) is not { } flag
				|| (pattern.Length > 0 && !MushText.IsWildcardMatch(MString.Plain(toggle.Name), pattern)))
			{
				continue;
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ToggleDefFormat), executor,
				toggle.Name, toggle.Letter, RhostPermissions.Describe(flag.SetPermissions),
				RhostPermissions.Describe(flag.UnsetPermissions));
		}

		return CallState.Empty;
	}
}
