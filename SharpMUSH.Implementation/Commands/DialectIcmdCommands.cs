using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// TinyMUX's and RhostMUSH's <c>@icmd</c>: commands disabled or ignored for one player, or for every player in a
/// location. The settings live in the target's ICMD attribute; the dispatcher reads them before each built-in.
/// </summary>
public partial class Commands
{
	/// <summary>The ICMD attribute's flags: only wizards see or change it, and the dispatcher trusts no other.</summary>
	private static readonly string[] IcmdAttributeFlags = ["wizard", "mortal_dark", "no_clone"];

	/// <summary>The switches that work on a location rather than a player.</summary>
	private static readonly string[] IcmdRoomSwitches = ["IROOM", "DROOM", "CROOM", "LROOM", "LALLROOM"];

	/// <summary>
	/// <c>@icmd[/&lt;switch&gt;] &lt;target&gt;[=&lt;command&gt;, !&lt;command&gt;, ...]</c>. On a player: /disable (the
	/// default), /ignore, /on, /off, /check, /clear. On a location: /droom, /iroom, /croom, /lroom, /lallroom.
	/// </summary>
	[SharpCommand(Name = "@DIALECT_ICMD",
		Switches = ["ON", "OFF", "DISABLE", "IGNORE", "CHECK", "CLEAR", "IROOM", "DROOM", "CROOM", "LROOM", "LALLROOM"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse, MinArgs = 0, MaxArgs = 2, ParameterNames = ["target", "commands"])]
	public async ValueTask<Option<CallState>> DialectIcmd(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await PermissionService.Check(executor, Requires.God()) is PermissionRefusal refused)
			return await NotifyService.PermissionDenied(executor, refused);

		var args = parser.CurrentState.ArgumentsOrdered;
		var name = args.GetValueOrDefault("0")?.Message.ToPlainText().Trim() ?? string.Empty;
		var switches = parser.CurrentState.Switches.ToArray();
		var room = switches.Any(IcmdRoomSwitches.Contains);
		if (name.Length == 0)
			return await IcmdNotice(executor, room ? "Name a location." : "Name a player.");

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, name, LocateFlags.All) switch
		{
			AnySharpObject target when target.IsPlayer == room
				=> await IcmdNotice(executor, room ? "That switch takes a location, not a player." : "That switch takes a player."),
			AnySharpObject target => await IcmdAsync(parser, executor, target, switches,
				args.GetValueOrDefault("1")?.Message.ToPlainText() ?? string.Empty),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> IcmdAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject target, string[] switches, string commands)
	{
		var list = await StoredIcmdAsync(target.Object().DBRef);
		var targetName = target.Object().Name;
		bool Has(string sw) => switches.Contains(sw);

		if (Has("CHECK") || Has("LROOM") || Has("LALLROOM"))
		{
			await ListIcmdAsync(executor, targetName, list);
			if (Has("LALLROOM") && await target.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject zone)
				await ListIcmdAsync(executor, $"Zone {zone.Object().Name}", await StoredIcmdAsync(zone.Object().DBRef));
			return CallState.Empty;
		}

		if (Has("CLEAR") || Has("CROOM"))
			return await WriteIcmdAsync(executor, target, IcmdList.Empty, $"@icmd cleared on {targetName}.");
		if (Has("ON") || Has("OFF"))
			return await WriteIcmdAsync(executor, target, list with { Checked = Has("ON") },
				$"@icmd checking is {(Has("ON") ? "on" : "off")} for {targetName}.");

		var level = Has("IGNORE") || Has("IROOM") ? IcmdSetting.Ignored : IcmdSetting.Disabled;
		var items = commands.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (items.Length == 0)
			return await IcmdNotice(executor, "Name the commands: <command>, !<command>, ...");

		var settings = new Dictionary<string, IcmdSetting>(list.Commands, StringComparer.OrdinalIgnoreCase);
		var dialect = Configuration.CurrentValue.Compatibility.Dialect;
		foreach (var item in items)
		{
			var removing = item.StartsWith('!');
			var typed = removing ? item[1..].Trim() : item;
			// An entry is removed by the name it was stored under, even when that no longer names a command.
			if (removing && settings.Remove(typed))
			{
				await NotifyService.Notify(executor, $"{typed.ToUpperInvariant()}: removed.");
				continue;
			}

			if (CommandDispatcher.BuiltInNameFor(parser, typed, dialect) is not { } command
				|| FindCommand(command) is not { } found || found.LibraryInformation.Attribute.Behavior.HasFlag(CB.SOCKET)
				|| found.LibraryInformation.Attribute.Behavior.HasFlag(CB.SingleToken))
			{
				await NotifyService.Notify(executor, $"{typed}: no such command.");
				continue;
			}

			if (removing)
			{
				await NotifyService.Notify(executor, settings.Remove(command) ? $"{command}: removed." : $"{command}: not set.");
			}
			else if (CommandsTheGameRuns.Contains(command))
			{
				await NotifyService.Notify(executor, $"{command}: run by the game itself, so it cannot be turned off.");
			}
			else if (settings.TryGetValue(command, out var current))
			{
				await NotifyService.Notify(executor,
					$"{command}: already {(current == IcmdSetting.Disabled ? "disabled" : "ignored")}; remove it with !{typed} first.");
			}
			else
			{
				settings[command] = level;
				await NotifyService.Notify(executor, $"{command}: {(level == IcmdSetting.Disabled ? "disabled" : "ignored")}.");
			}
		}

		return await WriteIcmdAsync(executor, target, list with { Commands = settings }, null);
	}

	private async ValueTask ListIcmdAsync(AnySharpObject executor, string label, IcmdList list)
	{
		if (!list.Checked) await NotifyService.Notify(executor, $"{label}: @icmd checking is off.");
		if (list.Commands.Count == 0)
		{
			await NotifyService.Notify(executor, $"{label}: no commands disabled or ignored.");
			return;
		}

		foreach (var (command, setting) in list.Commands.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
			await NotifyService.Notify(executor, $"{label}: {command} {(setting == IcmdSetting.Disabled ? "disabled" : "ignored")}");
	}

	private ValueTask<IcmdList> StoredIcmdAsync(DBRef holder) => IcmdList.OnAsync(Mediator, holder);

	private async ValueTask<Option<CallState>> WriteIcmdAsync(AnySharpObject executor, AnySharpObject target, IcmdList list,
		string? done)
	{
		var value = list.Format();
		var written = value.Length == 0
			? await AttributeService.ClearAttributeAsync(executor, target, IcmdList.Attribute, IAttributeService.AttributePatternMode.Exact)
			: await AttributeService.SetAttributeAsync(executor, target, IcmdList.Attribute, MarkupText.Plain(value)) switch
			{
				Success => await FlagIcmdAsync(executor, target),
				var failed => failed
			};

		if (written is Error<string> error)
			return await IcmdNotice(executor, $"@icmd could not be saved: {error.Value}");

		return done is null ? CallState.Empty : await IcmdNotice(executor, done, CallState.Empty);
	}

	/// <summary>Flags the ICMD value just written; a value the flags could not protect is cleared, as no setting at all.</summary>
	private async ValueTask<Result<Success>> FlagIcmdAsync(AnySharpObject executor, AnySharpObject target)
	{
		var flagged = await AttributeService.SetAttributeFlagsAsync(executor, target, IcmdList.Attribute, IcmdAttributeFlags);
		if (flagged is Error<string>)
			await AttributeService.ClearAttributeAsync(executor, target, IcmdList.Attribute, IAttributeService.AttributePatternMode.Exact);
		return flagged;
	}

	private async ValueTask<Option<CallState>> IcmdNotice(AnySharpObject executor, string message, CallState? result = null)
	{
		await NotifyService.Notify(executor, message);
		return result ?? new CallState($"#-1 {message}");
	}
}
