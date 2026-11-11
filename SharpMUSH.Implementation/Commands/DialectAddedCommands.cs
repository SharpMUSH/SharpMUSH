using System.Collections.Concurrent;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// TinyMUX's <c>@addcommand</c>, <c>@delcommand</c> and <c>@listcommands</c>, built from what SharpMUSH has: an
/// added command is <c>@command/add</c>'s unhooked command, or the built-in of that name, with an
/// <c>@hook/override</c> on the object's attribute. A built-in it covers stays reachable as <c>__&lt;name&gt;</c>.
/// </summary>
public partial class Commands
{
	/// <summary>
	/// The commands <c>@addcommand</c> made, by their full name, each with the <c>__&lt;name&gt;</c> copy of the
	/// built-in it covers, or <see langword="null"/> when it created the command. Like the rest of the command table,
	/// they last until the server restarts. The table is the truth: an entry whose command was taken away by other
	/// means is dropped when next looked at.
	/// </summary>
	private readonly ConcurrentDictionary<string, string?> _dialectAddedCommands = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>The command <paramref name="name"/> names as <c>@addcommand</c> made it, if it is still there.</summary>
	private bool LiveAddedCommand(string name, out string? original)
	{
		if (_dialectAddedCommands.TryGetValue(name, out original)
			&& (original is null ? FindCommand(name) is { } created && IsAddedCommand(created.LibraryInformation) : FindCommand(original) is not null))
			return true;
		_dialectAddedCommands.TryRemove(name, out _);
		return false;
	}

	/// <summary>
	/// <c>@addcommand &lt;command&gt;=&lt;object&gt;/&lt;attribute&gt;</c>: typing the command runs the attribute's
	/// $-command, matched against the whole line, as a command of the game's own.
	/// </summary>
	[SharpCommand(Name = "@DIALECT_ADDCOMMAND", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["command", "object/attribute"])]
	public async ValueTask<Option<CallState>> DialectAddCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await PermissionService.Check(executor, Requires.God()) is PermissionRefusal refused)
			return await NotifyService.PermissionDenied(executor, refused);

		var args = parser.CurrentState.ArgumentsOrdered;
		var name = args.GetValueOrDefault("0")?.Message.ToPlainText().Trim().ToUpperInvariant() ?? string.Empty;
		var path = args.GetValueOrDefault("1")?.Message.ToPlainText().Trim() ?? string.Empty;
		var slash = path.IndexOf('/');
		if (name.Length == 0 || slash <= 0 || slash == path.Length - 1)
			return await AddedCommandNotice(executor, "Give a command and an object/attribute: @addcommand <command>=<object>/<attribute>.");

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, path[..slash], LocateFlags.All) switch
		{
			AnySharpObject holder => await AddDialectCommandAsync(executor, name, holder, path[(slash + 1)..].Trim().ToUpperInvariant()),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> AddDialectCommandAsync(AnySharpObject executor, string name, AnySharpObject holder,
		string attribute)
	{
		if (name.StartsWith("__", StringComparison.Ordinal) || name.Contains(' ') || name.Contains('/'))
			return await AddedCommandNotice(executor, $"{name} cannot be a command's name.");
		if (await AttributeService.GetAttributeAsync(executor, holder, attribute, IAttributeService.AttributeMode.Read) is not SharpAttribute[])
			return await AddedCommandNotice(executor, $"{holder.Object().Name} has no {attribute} attribute.");

		// A hook lives on the command, not on the name that reached it, so an alias adds to the command it names.
		if (!LiveAddedCommand(name, out _) && FindCommand(name) is { } existing)
		{
			name = existing.LibraryInformation.Attribute.Name;
			if (!LiveAddedCommand(name, out _))
			{
				if (DisabledCommandFor(name) is not null)
					return await AddedCommandNotice(executor, $"{name} is disabled; @command/enable it first.");
				if (await HookService.GetHookAsync(name, "OVERRIDE") is CommandHook)
					return await AddedCommandNotice(executor, $"{name} already has an @hook/override; clear it first.");
				var original = $"__{name}";
				if (FindCommand(original) is not null)
					return await AddedCommandNotice(executor, $"{original} is already a command.");
				// The hooks stay on the command typed, so they run once and not again when the code calls the original.
				await CloneCommandEntry(existing, original, copyHooks: false);
				_dialectAddedCommands[name] = original;
			}
		}
		else if (!LiveAddedCommand(name, out _))
		{
			if (!await ValidCommandName(name))
				return await AddedCommandNotice(executor, $"{name} is not a valid command name.");
			AddUnhookedCommand(name, CommandBehavior.NoParse | CommandBehavior.RSNoParse);
			_dialectAddedCommands[name] = null;
		}

		await HookService.SetHookAsync(name, "OVERRIDE", holder.Object().DBRef, attribute);
		return await AddedCommandNotice(executor, $"{name} now runs #{holder.Object().DBRef.Number}/{attribute}.", new CallState(name));
	}

	/// <summary>
	/// <c>@delcommand &lt;command&gt;[=&lt;object&gt;/&lt;attribute&gt;]</c>: takes away a command <c>@addcommand</c>
	/// made, and gives a built-in it covered back its name. Given an object and attribute, only when the command
	/// runs that one.
	/// </summary>
	[SharpCommand(Name = "@DIALECT_DELCOMMAND", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["command", "object/attribute"])]
	public async ValueTask<Option<CallState>> DialectDelCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await PermissionService.Check(executor, Requires.God()) is PermissionRefusal refused)
			return await NotifyService.PermissionDenied(executor, refused);

		var args = parser.CurrentState.ArgumentsOrdered;
		var name = args.GetValueOrDefault("0")?.Message.ToPlainText().Trim().ToUpperInvariant() ?? string.Empty;
		var path = args.GetValueOrDefault("1")?.Message.ToPlainText().Trim() ?? string.Empty;
		if (!LiveAddedCommand(name, out _) && FindCommand(name) is { } named)
			name = named.LibraryInformation.Attribute.Name;
		if (!LiveAddedCommand(name, out var original))
			return await AddedCommandNotice(executor, $"{(name.Length == 0 ? "That" : name)} is not an added command.");

		if (path.Length > 0)
		{
			var slash = path.IndexOf('/');
			if (slash <= 0 || slash == path.Length - 1)
				return await AddedCommandNotice(executor, "Give the object/attribute the command runs.");
			if (await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, path[..slash], LocateFlags.All)
				is not AnySharpObject holder)
				return CallState.Empty;
			if (await HookService.GetHookAsync(name, "OVERRIDE") is not CommandHook hook
				|| hook.TargetObject != holder.Object().DBRef
				|| !hook.AttributeName.Equals(path[(slash + 1)..].Trim(), StringComparison.OrdinalIgnoreCase))
				return await AddedCommandNotice(executor, $"{name} does not run {path}.");
		}

		await HookService.ClearHookAsync(name, "OVERRIDE");
		// A created command goes with its aliases, as @command/delete takes them; a covered built-in only loses its copy.
		await ForgetCommandNamesAsync(original is null && FindCommand(name) is { } created ? NamesOf(created.LibraryInformation.Attribute) : [original ?? name]);
		_dialectAddedCommands.TryRemove(name, out _);
		return await AddedCommandNotice(executor, $"{name} removed.", new CallState(name));
	}

	/// <summary><c>@listcommands</c>: each command <c>@addcommand</c> made, and the object and attribute it runs.</summary>
	[SharpCommand(Name = "@DIALECT_LISTCOMMANDS", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public async ValueTask<Option<CallState>> DialectListCommands(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await PermissionService.Check(executor, Requires.God()) is PermissionRefusal refused)
			return await NotifyService.PermissionDenied(executor, refused);

		var any = false;
		foreach (var name in _dialectAddedCommands.Keys.Order(StringComparer.OrdinalIgnoreCase))
		{
			if (!LiveAddedCommand(name, out _)) continue;
			if (await HookService.GetHookAsync(name, "OVERRIDE") is not CommandHook hook) continue;
			any = true;
			await NotifyService.Notify(executor, $"{name}: #{hook.TargetObject.Number}/{hook.AttributeName}");
		}

		if (!any) await NotifyService.Notify(executor, "No commands have been added.");
		return CallState.Empty;
	}

	private async ValueTask<Option<CallState>> AddedCommandNotice(AnySharpObject executor, string message, CallState? result = null)
	{
		await NotifyService.Notify(executor, message);
		return result ?? new CallState($"#-1 {message}");
	}
}
