using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using System.Collections.Immutable;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using DotNext.Collections.Generic;
using System.Diagnostics;
using System.Buffers;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// PennMUSH's <c>DEF_FUNCTION_ARGS</c> (hdrs/function.h:133) — what <c>@function</c> gives a
	/// definition that names no maximum.
	/// </summary>
	private const int DefaultUserFunctionArguments = 10;

	/// <summary>
	/// PennMUSH's <c>MAX_STACK_ARGS</c> (hdrs/conf.h:29) — the positional arguments an invocation can
	/// carry as <c>%0</c>-<c>%9</c> and <c>v(N)</c>, and so the ceiling on either declared bound.
	/// </summary>
	private const int MaximumStackArguments = 30;

	[SharpCommand(Name = "@COMMAND",
		Switches =
		[
			"ADD", "ALIAS", "CLONE", "DELETE", "EqSplit", "LSARGS", "RSARGS", "NOEVAL", "ON", "OFF", "QUIET", "ENABLE",
			"DISABLE", "RESTRICT", "NOPARSE", "RSNoParse"
		], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "command", "code"])]
	public async ValueTask<Option<CallState>> Command(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandMustSpecifyName), executor);
			return new CallState(ErrorMessages.Returns.NoCommandSpecified);
		}

		var commandName = args["0"].Message?.ToPlainText()?.ToUpper();
		if (string.IsNullOrEmpty(commandName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandMustSpecifyName), executor);
			return new CallState(ErrorMessages.Returns.NoCommandSpecified);
		}

		var isQuiet = switches.Contains("QUIET");

		// cmd_command: /add, /alias and /clone are Wizard, /delete is God; each answers for itself.
		if (switches.Contains("ADD"))
		{
			return await AddCommandAsync(executor, commandName, switches);
		}

		if (switches.Contains("ALIAS"))
		{
			return await AliasCommandAsync(executor, commandName, args.GetValueOrDefault("1")?.Message?.ToPlainText().Trim().ToUpperInvariant() ?? "", isQuiet);
		}

		if (switches.Contains("CLONE"))
		{
			return await CloneCommandAsync(executor, commandName, args.GetValueOrDefault("1")?.Message?.ToPlainText().Trim().ToUpperInvariant() ?? "");
		}

		if (switches.Contains("DELETE"))
		{
			return await DeleteCommandAsync(executor, commandName);
		}

		// A disabled command is still found here, as command_find still finds it in Penn.
		(CommandDefinition LibraryInformation, bool IsSystem) commandInfo;
		var disabled = false;
		if (CommandLibrary.TryGetValue(commandName, out var live))
		{
			commandInfo = live;
		}
		else if (DisabledCommandFor(commandName) is { } parked)
		{
			commandInfo = parked[0].Value;
			disabled = true;
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandNoSuchCommand), executor);
			return new CallState(ErrorMessages.Returns.CommandNotFound);
		}

		// cmd_command: for a wizard, the state switches act, then the command is described.
		if (await executor.IsWizard())
		{
			if (switches.Contains("ON") || switches.Contains("ENABLE"))
			{
				EnableCommand(commandName);
			}
			else if ((switches.Contains("OFF") || switches.Contains("DISABLE"))
							 && await DisableCommandAsync(executor, commandInfo.LibraryInformation) is CallState disableRefused)
			{
				return disableRefused;
			}

			if (switches.Contains("RESTRICT")
					&& await RestrictCommandAsync(executor, commandInfo.LibraryInformation, args.GetValueOrDefault("1")?.Message?.ToPlainText() ?? "") is CallState restrictRefused)
			{
				return restrictRefused;
			}

			disabled = !CommandLibrary.ContainsKey(commandName) && DisabledCommandFor(commandName) is not null;
		}
		else if (switches.Any(sw => sw is "ON" or "OFF" or "ENABLE" or "DISABLE" or "RESTRICT"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (isQuiet)
		{
			return CallState.Empty;
		}

		var (definition, isSystem) = commandInfo;
		var attr = definition.Attribute;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoNameFormat), executor, attr.Name, disabled ? "Disabled" : "Enabled");
		// A command @command/add made is registered as a system entry because only those are matched
		// from the command trie, but it is not built in, and list_commands tells the two apart.
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoTypeFormat), executor,
			isSystem && !IsAddedCommand(definition) ? "Built-in" : "User-defined");
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoMinArgsFormat), executor, attr.MinArgs);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoMaxArgsFormat), executor, attr.MaxArgs);

		if (attr.Switches != null && attr.Switches.Length > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoSwitchesFormat), executor, string.Join(", ", attr.Switches));
		}

		var behaviors = new List<string>();
		if ((attr.Behavior & CB.Default) != 0) behaviors.Add("Default");
		if ((attr.Behavior & CB.EqSplit) != 0) behaviors.Add("EqSplit");
		if ((attr.Behavior & CB.LSArgs) != 0) behaviors.Add("LSArgs");
		if ((attr.Behavior & CB.RSArgs) != 0) behaviors.Add("RSArgs");
		if ((attr.Behavior & CB.RSNoParse) != 0) behaviors.Add("RSNoParse");
		if ((attr.Behavior & CB.NoGagged) != 0) behaviors.Add("NoGagged");
		if ((attr.Behavior & CB.NoParse) != 0) behaviors.Add("NoParse");

		if (behaviors.Count > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoBehaviorFormat), executor, string.Join(" | ", behaviors));
		}

		if (!string.IsNullOrEmpty(attr.CommandLock))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoLockFormat), executor, attr.CommandLock);
		}

		if (!string.IsNullOrEmpty(attr.RestrictMessage))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoFailureMsgFormat), executor, attr.RestrictMessage);
		}

		return CallState.Empty;
	}

	/// <summary>
	/// Commands <c>@command/disable</c> has taken out of the table, keyed by the command's name, with
	/// every name (the command and its aliases) it was reachable by. Penn marks a disabled command
	/// CMD_T_DISABLED and then treats it as no command at all (<c>src/command.c:1320</c>), so the line
	/// falls through to $-commands and HUH; taking it out of the table is how that happens here.
	/// </summary>
	private readonly Dictionary<string, List<KeyValuePair<string, (CommandDefinition LibraryInformation, bool IsSystem)>>> _disabledCommands
		= new(StringComparer.OrdinalIgnoreCase);

	private readonly Lock _commandTableLock = new();

	/// <summary>
	/// The commands the engine invokes by name rather than by matching what was typed. They cannot be
	/// taken out of the table: the engine would find nothing to run.
	/// </summary>
	private static readonly HashSet<string> CommandsTheGameRuns = new(["HUH_COMMAND", "@CHAT", "GOTO"], StringComparer.OrdinalIgnoreCase);

	/// <summary>What <c>@command/add</c> installs: Penn's <c>cmd_unimplemented</c>, for a hook to replace.</summary>
	private async ValueTask<Option<CallState>> CommandAddedWithoutHook(IMUSHCodeParser parser)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandNotImplemented), executor);
		return new None();
	}

	/// <summary>Whether <paramref name="definition"/> was made by <c>@command/add</c> (or cloned from one).</summary>
	private bool IsAddedCommand(CommandDefinition definition)
		=> definition.Command.Method == ((Func<IMUSHCodeParser, ValueTask<Option<CallState>>>)CommandAddedWithoutHook).Method;

	private List<KeyValuePair<string, (CommandDefinition LibraryInformation, bool IsSystem)>>? DisabledCommandFor(string name)
	{
		lock (_commandTableLock)
		{
			return _disabledCommands.Values.FirstOrDefault(entries => entries.Any(entry => entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase)));
		}
	}

	private async ValueTask<bool> ValidCommandName(string name)
		=> name.Length > 0 && await ValidateService.Valid(IValidateService.ValidationType.CommandName, MarkupText.Plain(name), new None());

	/// <summary>
	/// <c>do_command_add</c> (<c>src/command.c:1923</c>): a new command that does nothing until it is
	/// hooked, parsed as its switches say. Unless it is /noparse and /rsnoparse both, it also takes a
	/// /noeval switch, which leaves its arguments unevaluated for that one use.
	/// </summary>
	private async ValueTask<Option<CallState>> AddCommandAsync(AnySharpObject executor, string name, string[] switches)
	{
		if (switches.Contains("NOEVAL"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandNoevalNoLongerNoparse), executor);
		}

		if (!await executor.IsWizard())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (FindCommand(name) is { } taken)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandAlreadyExistsFormat), executor,
				taken.LibraryInformation.Attribute.Name);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		if (!await ValidCommandName(name))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandBadName), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var behavior = CommandBehavior.Default;
		if (switches.Contains("NOPARSE")) behavior |= CommandBehavior.NoParse;
		if (switches.Contains("RSARGS")) behavior |= CommandBehavior.RSArgs;
		if (switches.Contains("LSARGS")) behavior |= CommandBehavior.LSArgs;
		if (switches.Contains("EQSPLIT")) behavior |= CommandBehavior.EqSplit;
		if (switches.Contains("RSNOPARSE")) behavior |= CommandBehavior.RSNoParse;

		var attribute = new SharpCommandAttribute
		{
			Name = name,
			Behavior = behavior,
			Switches = behavior.HasFlag(CommandBehavior.NoParse) && behavior.HasFlag(CommandBehavior.RSNoParse) ? [] : ["NOEVAL"]
		};

		lock (_commandTableLock)
		{
			CommandLibrary[name] = (new CommandDefinition(attribute, CommandAddedWithoutHook), true);
			CommandTrie.Invalidate(CommandLibrary);
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandAddedFormat), executor, name);
		return new CallState(name);
	}

	/// <summary><c>alias_command</c>: a second name for an existing command, which must not already be taken.</summary>
	private async ValueTask<Option<CallState>> AliasCommandAsync(AnySharpObject executor, string name, string alias, bool quiet)
	{
		if (!await executor.IsWizard())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!await ValidCommandName(alias))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandAliasBadName), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		bool aliased;
		lock (_commandTableLock)
		{
			aliased = CommandLibrary.TryGetValue(name, out var command) && CommandLibrary.TryAdd(alias, command);
			CommandTrie.Invalidate(CommandLibrary);
		}

		if (!aliased)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandAliasFailed), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		if (!quiet)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandAliasSet), executor);
		}

		return new CallState(alias);
	}

	/// <summary>
	/// <c>do_command_clone</c> (<c>src/command.c:1960</c>): a separate command that starts as a copy of
	/// the original — its parsing, switches, lock and hooks — and can then be restricted or hooked
	/// on its own.
	/// </summary>
	private async ValueTask<Option<CallState>> CloneCommandAsync(AnySharpObject executor, string original, string clone)
	{
		if (!await executor.IsWizard())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!CommandLibrary.TryGetValue(original, out var source))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandNoSuchCommand), executor);
			return new CallState(ErrorMessages.Returns.CommandNotFound);
		}

		if (!await ValidCommandName(clone) || CommandLibrary.ContainsKey(clone) || DisabledCommandFor(clone) is not null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandBadName), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var from = source.LibraryInformation.Attribute;
		var attribute = new SharpCommandAttribute
		{
			Name = clone,
			MinArgs = from.MinArgs,
			MaxArgs = from.MaxArgs,
			CommandLock = from.CommandLock,
			RestrictMessage = from.RestrictMessage,
			Behavior = from.Behavior,
			Switches = from.Switches is null ? null : [.. from.Switches],
			SingleArgumentSwitches = [.. from.SingleArgumentSwitches],
			ParameterNames = [.. from.ParameterNames]
		};

		lock (_commandTableLock)
		{
			CommandLibrary[clone] = (source.LibraryInformation with { Attribute = attribute }, source.IsSystem);
			CommandTrie.Invalidate(CommandLibrary);
		}

		foreach (var (type, hook) in await HookService.GetAllHooksAsync(from.Name))
		{
			await HookService.SetHookAsync(clone, type, hook.TargetObject, hook.AttributeName,
				hook.Inline, hook.NoBreak, hook.Localize, hook.ClearRegs);
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandCloned), executor);
		return new CallState(clone);
	}

	/// <summary>
	/// <c>do_command_delete</c> (<c>src/command.c:2067</c>), God only: an alias is simply removed; a
	/// command is removed with all its aliases, and only if <c>@command/add</c> made it.
	/// </summary>
	private async ValueTask<Option<CallState>> DeleteCommandAsync(AnySharpObject executor, string name)
	{
		if (!executor.IsGod())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// command_find_exact still finds a disabled command, so /delete reaches one too.
		if (FindCommand(name) is not { } command)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandNoSuchCommand), executor);
			return new CallState(ErrorMessages.Returns.CommandNotFound);
		}

		var definition = command.LibraryInformation;
		if (!definition.Attribute.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
		{
			await ForgetCommandNamesAsync([name]);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandRemovedFormat), executor, name);
			return new CallState(name);
		}

		if (!IsAddedCommand(definition))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandCannotDeleteBuiltin), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var removed = await ForgetCommandNamesAsync(NamesOf(definition.Attribute));

		await NotifyService.NotifyLocalized(executor,
			removed > 1 ? nameof(ErrorMessages.Notifications.CommandRemovedWithAliasesFormat) : nameof(ErrorMessages.Notifications.CommandRemovedFormat),
			executor, name);
		return new CallState(name);
	}

	/// <summary>
	/// The command <paramref name="name"/> names, whether it is in the table or <c>@command/disable</c>
	/// has parked it — <c>command_find_exact</c> finds a disabled command too.
	/// </summary>
	private (CommandDefinition LibraryInformation, bool IsSystem)? FindCommand(string name)
	{
		if (CommandLibrary.TryGetValue(name, out var live))
		{
			return live;
		}

		lock (_commandTableLock)
		{
			return _disabledCommands.Values
				.SelectMany(entries => entries)
				.Where(entry => entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
				.Select(entry => ((CommandDefinition, bool)?)entry.Value)
				.FirstOrDefault();
		}
	}

	/// <summary>Every name <paramref name="attribute"/>'s command answers to, parked ones included.</summary>
	private string[] NamesOf(SharpCommandAttribute attribute)
	{
		lock (_commandTableLock)
		{
			return
			[
				.. CommandLibrary.Where(entry => ReferenceEquals(entry.Value.LibraryInformation.Attribute, attribute)).Select(entry => entry.Key),
				.. _disabledCommands.Values.SelectMany(entries => entries)
					.Where(entry => ReferenceEquals(entry.Value.LibraryInformation.Attribute, attribute)).Select(entry => entry.Key)
			];
		}
	}

	/// <summary>
	/// Forgets <paramref name="names"/> entirely: out of the table, out of the disabled parking, and
	/// out of the hook service. Penn frees the COMMAND_INFO and its hooks with it
	/// (<c>src/command.c:2100-2104</c>), so a name added again comes back unhooked — an alias as much
	/// as the command itself.
	/// </summary>
	private async ValueTask<int> ForgetCommandNamesAsync(string[] names)
	{
		lock (_commandTableLock)
		{
			foreach (var name in names)
			{
				CommandLibrary.Remove(name);
			}

			foreach (var (parked, entries) in _disabledCommands.ToArray())
			{
				entries.RemoveAll(entry => names.Contains(entry.Key, StringComparer.OrdinalIgnoreCase));
				if (entries.Count == 0)
				{
					_disabledCommands.Remove(parked);
				}
			}

			CommandTrie.Invalidate(CommandLibrary);
		}

		foreach (var name in names)
		{
			foreach (var (type, _) in await HookService.GetAllHooksAsync(name))
			{
				await HookService.ClearHookAsync(name, type);
			}
		}

		return names.Length;
	}

	/// <summary>
	/// Takes <paramref name="definition"/> out of the table under every name it has, keeping them for
	/// <see cref="EnableCommand"/>. <c>@command</c> itself stays: "@command is ALWAYS enabled."
	/// </summary>
	private async ValueTask<Option<CallState>> DisableCommandAsync(AnySharpObject executor, CommandDefinition definition)
	{
		if (definition.Attribute.Name.Equals("@COMMAND", StringComparison.OrdinalIgnoreCase))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandAlwaysEnabled), executor);
			return new None();
		}

		if (CommandsTheGameRuns.Contains(definition.Attribute.Name))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandCalledByTheGameFormat), executor, definition.Attribute.Name);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		lock (_commandTableLock)
		{
			var entries = CommandLibrary.Where(entry => ReferenceEquals(entry.Value.LibraryInformation.Attribute, definition.Attribute)).ToList();
			foreach (var entry in entries)
			{
				CommandLibrary.Remove(entry.Key);
			}

			if (entries.Count > 0)
			{
				_disabledCommands[definition.Attribute.Name] = entries;
			}

			CommandTrie.Invalidate(CommandLibrary);
		}

		return new None();
	}

	/// <summary>Puts a disabled command back under every name it had.</summary>
	private void EnableCommand(string name)
	{
		lock (_commandTableLock)
		{
			var parked = _disabledCommands.FirstOrDefault(disabled => disabled.Value.Any(entry => entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase)));
			if (parked.Value is null)
			{
				return;
			}

			foreach (var (key, value) in parked.Value)
			{
				CommandLibrary.TryAdd(key, value);
			}

			_disabledCommands.Remove(parked.Key);
			CommandTrie.Invalidate(CommandLibrary);
		}
	}

	/// <summary>
	/// <c>restrict_command</c> (<c>src/command.c:1719</c>): who may use the command, given as a lock or
	/// as Penn's restriction words — a flag or power name, <c>admin</c>, <c>player</c>/<c>thing</c>/
	/// <c>room</c>/<c>exit</c>/<c>any</c>, <c>god</c>, <c>noguest</c>, <c>nogagged</c>, <c>nofixed</c>,
	/// each negated with <c>!</c> — and <c>nobody</c>, which disables it.
	/// </summary>
	private async ValueTask<Option<CallState>> RestrictCommandAsync(AnySharpObject executor, CommandDefinition definition, string restriction)
	{
		var quote = restriction.IndexOf('"');
		var message = quote >= 0 ? restriction[(quote + 1)..].Trim() : null;
		var words = (quote >= 0 ? restriction[..quote] : restriction).Trim();
		if (words.Length == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandHowToRestrict), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var attribute = definition.Attribute;
		switch (await RestrictionFromWords(words, attribute.Behavior))
		{
			case CommandRestriction { Disables: true }:
				// "nobody" is CMD_T_DISABLED, so the refusals @command/disable answers with are this
				// command's answers too.
				if (await DisableCommandAsync(executor, definition) is CallState refused)
				{
					return refused;
				}

				break;
			case CommandRestriction translated:
				attribute.CommandLock = translated.Lock;
				attribute.Behavior = translated.Behavior;
				break;
			case NotFound when LockService.Validate(words, executor):
				attribute.CommandLock = words;
				break;
			default:
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandRestrictFailed), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		// restrict_command frees the old message whenever the restriction carries a quote at all, and
		// stores what follows only if anything does (command.c:1741-1750), so a bare `"` clears it.
		if (message is not null)
		{
			attribute.RestrictMessage = message;
		}

		return new None();
	}

	private readonly record struct CommandRestriction(string Lock, CommandBehavior Behavior, bool Disables);

	/// <summary>
	/// Penn's old-style restriction words as a lock, the way <c>restrict_command</c> builds one: the
	/// named flags and powers OR'ed, the allowed types OR'ed, and <c>!FLAG^FIXED</c> for
	/// <c>nofixed</c>; <c>god</c>, <c>noguest</c> and <c>nogagged</c> become the command behaviours
	/// that already enforce them. NotFound when any word is not one of these, so the text is a lock.
	/// </summary>
	private async ValueTask<Found<CommandRestriction>> RestrictionFromWords(string words, CommandBehavior behavior)
	{
		string[] allTypes = ["PLAYER", "THING", "ROOM", "EXIT"];
		var types = new HashSet<string>(allTypes);
		// "Commands can also give any flag, power or type, to restrict to objects ... of one of those
		// types" (help restrict2): the first type named is the whole allowed set, and later ones add to
		// it. A negated type subtracts from every type, which is what `noplayer` is for.
		var narrowed = false;
		var flags = new List<string>();
		var noFixed = false;
		var disables = false;

		foreach (var token in words.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			var clear = token.StartsWith('!');
			var word = (clear ? token[1..] : token).ToUpperInvariant();
			if (word == "NOPLAYER")
			{
				clear = !clear;
				word = "PLAYER";
			}

			switch (word)
			{
				case "NOBODY":
					disables = !clear;
					break;
				case "ANY":
					if (clear)
					{
						types.Clear();
					}
					else
					{
						types.UnionWith(allTypes);
						narrowed = false;
					}

					break;
				case "PLAYER" or "THING" or "ROOM" or "EXIT":
					if (clear)
					{
						types.Remove(word);
					}
					else
					{
						if (!narrowed)
						{
							types.Clear();
							narrowed = true;
						}

						types.Add(word);
					}

					break;
				case "GOD":
					behavior = clear ? behavior & ~CommandBehavior.God : behavior | CommandBehavior.God;
					break;
				case "NOGUEST":
					behavior = clear ? behavior & ~CommandBehavior.NoGuest : behavior | CommandBehavior.NoGuest;
					break;
				case "NOGAGGED":
					behavior = clear ? behavior & ~CommandBehavior.NoGagged : behavior | CommandBehavior.NoGagged;
					break;
				case "NOFIXED":
					noFixed = !clear;
					break;
				case "ADMIN":
					foreach (var admin in new[] { "FLAG^ROYALTY", "FLAG^WIZARD" })
					{
						if (clear) flags.Remove(admin); else if (!flags.Contains(admin)) flags.Add(admin);
					}

					break;
				default:
					var term = await Mediator.Send(new GetObjectFlagQuery(word)) is not null ? $"FLAG^{word}"
						: await Mediator.Send(new GetPowerQuery(word)) is not null ? $"POWER^{word}"
						: null;
					if (term is null)
					{
						return new NotFound();
					}

					if (clear) flags.Remove(term); else if (!flags.Contains(term)) flags.Add(term);
					break;
			}
		}

		List<string> clauses = [];
		if (flags.Count > 0) clauses.Add($"({string.Join('|', flags)})");
		if (types.Count < allTypes.Length) clauses.Add($"({string.Join('|', allTypes.Where(types.Contains).Select(type => $"TYPE^{type}"))})");
		if (noFixed) clauses.Add("!FLAG^FIXED");
		return new CommandRestriction(string.Join('&', clauses), behavior, disables);
	}

	[SharpCommand(Name = "@FUNCTION",
		Switches = ["ALIAS", "BUILTIN", "CLONE", "DELETE", "ENABLE", "DISABLE", "PRESERVE", "RESTORE", "RESTRICT", "LOCAL"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 5, ParameterNames = ["name", "object/attribute"])]
	public async ValueTask<Option<CallState>> Function(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (switches.Contains("LOCAL")) return await LocalFunctionCommand(parser, executor, switches);

		if (args.Count == 0)
		{
			if (FunctionLibrary == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionLibraryUnavailable), executor);
				return new CallState(ErrorMessages.Returns.LibraryUnavailable);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionGlobalUserDefinedHeader), executor);

			var canSeeDetails = await executor.IsWizard();

			// Global user-defined functions live in the in-memory registry (@function), not the
			// FunctionLibrary; the library holds only built-ins (and any compiled-in defs).
			var registry = parser.ServiceProvider.GetService<IUserDefinedFunctionService>();
			var userFunctions = registry?.All().ToArray() ?? [];
			var builtinFunctions = FunctionLibrary.Where(kvp => kvp.Value.IsSystem).ToArray();

			if (canSeeDetails)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionUserDefinedCountFormat), executor, userFunctions.Length);
				foreach (var fn in userFunctions.Take(10))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionEntryFormat), executor, fn.Name, fn.MinArgs, fn.MaxArgs, fn.Enabled ? "Enabled" : "Disabled");
				}
				if (userFunctions.Length > 10)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionAndMoreFormat), executor, userFunctions.Length - 10);
				}

				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionBuiltInCountFormat), executor, builtinFunctions.Length);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionUserDefinedSummaryFormat), executor, userFunctions.Length);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionBuiltInSummaryFormat), executor, builtinFunctions.Length);
			}

			return CallState.Empty;
		}

		var functionName = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(functionName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyName), executor);
			return new CallState(ErrorMessages.Returns.NoFunctionSpecified);
		}

		var userFunctionService = parser.ServiceProvider.GetService<IUserDefinedFunctionService>();
		if (userFunctionService == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionLibraryUnavailable), executor);
			return new CallState(ErrorMessages.Returns.LibraryUnavailable);
		}

		if (switches.Contains("ALIAS"))
		{
			var aliasName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(aliasName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyAliasName), executor);
				return new CallState(ErrorMessages.Returns.NoAliasSpecified);
			}

			// @function/alias <alias>=<existing-user-function>
			// functionName is the alias being created; aliasName is the existing target.
			if (!userFunctionService.Alias(functionName, aliasName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, aliasName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionAliasWouldCreateFormat), executor, functionName, aliasName);
			return CallState.Empty;
		}

		if (switches.Contains("CLONE"))
		{
			// @function/clone <new>=<existing>: create <new> mirroring <existing> (built-in or user)
			// so the clone can be independently restricted/disabled without touching the original.
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var existingName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(existingName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyCloneName), executor);
				return new CallState(ErrorMessages.Returns.NoCloneNameSpecified);
			}

			// Prefer a user-defined source; fall back to a built-in in the FunctionLibrary.
			if (userFunctionService.Get(existingName) is not null)
			{
				if (!userFunctionService.Clone(functionName, existingName))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, existingName);
					return new CallState(ErrorMessages.Returns.FunctionNotFound);
				}

				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionClonedFormat), executor, functionName, existingName);
				return CallState.Empty;
			}

			if (FunctionLibrary != null && FunctionLibrary.TryGetValue(existingName.ToUpper(), out var builtinSource) && builtinSource.IsSystem)
			{
				// Register the clone under <new> pointing at the SAME FunctionDefinition; it is a
				// system function (IsSystem=true) so it resolves like a built-in, but its name is
				// distinct, so @function/restrict and @function/builtin can act on it alone.
				FunctionLibrary[functionName.ToUpper()] = builtinSource;
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionClonedFormat), executor, functionName, existingName);
				return CallState.Empty;
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, existingName);
			return new CallState(ErrorMessages.Returns.FunctionNotFound);
		}

		if (switches.Contains("BUILTIN"))
		{
			// @function/builtin <function>: discard a user override/clone so the original built-in
			// (regenerated by the function-library source generator) resolves again. We remove any
			// registry entry, any restriction overlay, and any cloned/overridden library entry for
			// the name. The generated built-in is re-added lazily on next call (DiscoverBuiltInFunction).
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			userFunctionService.Delete(functionName);
			userFunctionService.SetBuiltinRestriction(functionName, null);
			FunctionLibrary?.Remove(functionName.ToUpper());
			RestoreBuiltinFunction(functionName);

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionBuiltinRestoredFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("PRESERVE"))
		{
			// @function/preserve <function>: mark a user function to survive a bulk
			// @function/restore reset and to be reported for re-registration.
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (!userFunctionService.SetPreserved(functionName, true))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionPreservedFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("RESTORE"))
		{
			// @function/restore <function>: discard the user override of a single name so its
			// built-in resolves again (same outcome as /builtin).
			// @function/restore * : bulk reset — remove every user function NOT marked /preserve,
			// keeping the preserved set for re-registration.
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (functionName.Equals("*", StringComparison.Ordinal))
			{
				var removed = userFunctionService.ResetUnpreserved();
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestoredResetFormat), executor, removed);
				return CallState.Empty;
			}

			userFunctionService.Delete(functionName);
			userFunctionService.SetBuiltinRestriction(functionName, null);
			FunctionLibrary?.Remove(functionName.ToUpper());
			RestoreBuiltinFunction(functionName);

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestoredOneFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("DELETE"))
		{
			// /delete removes a user-defined or cloned function, OR "deletes" a built-in from the
			// library so a user @function can override it (PennMUSH semantics). The "deleted"
			// built-in is still reachable via fn() and can be brought back with /builtin or /restore.
			var removedUser = userFunctionService.Delete(functionName);
			var removedBuiltin = FunctionLibrary?.Remove(functionName.ToUpper()) ?? false;

			if (!removedUser && !removedBuiltin)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDeleteWouldDeleteFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("DISABLE"))
		{
			if (!userFunctionService.SetEnabled(functionName, false))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDisableWouldDisableFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("ENABLE"))
		{
			if (!userFunctionService.SetEnabled(functionName, true))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionEnableWouldEnableFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("RESTRICT"))
		{
			// @function/restrict <function>=<restriction>: set the permission restriction on a
			// function. A user function stores it on its registry entry; a built-in (or "deleted"
			// built-in / clone) stores it in the registry's built-in restriction overlay, consulted
			// at call time. An empty restriction clears it.
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var restriction = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			var clearing = string.IsNullOrWhiteSpace(restriction);

			if (userFunctionService.Get(functionName) is not null)
			{
				userFunctionService.SetRestriction(functionName, restriction);
			}
			else if (FunctionLibrary != null && FunctionLibrary.ContainsKey(functionName.ToUpper()))
			{
				userFunctionService.SetBuiltinRestriction(functionName, restriction);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			if (clearing)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestrictionClearedFormat), executor, functionName);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestrictedFormat), executor, functionName, restriction!);
			}

			return CallState.Empty;
		}

		// Defining a new function: @function <name>=<obj>,<attrib>[,<min>,<max>]
		// CB.RSArgs splits the RHS on commas: args["1"]=obj, ["2"]=attrib, ["3"]=min, ["4"]=max.
		if (args.Count >= 2)
		{
			var objSpec = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			var attribSpec = args.GetValueOrDefault("2")?.Message?.ToPlainText();

			if (!string.IsNullOrEmpty(objSpec))
			{
				if (string.IsNullOrEmpty(attribSpec))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyName), executor);
					return new CallState(ErrorMessages.Returns.NoFunctionSpecified);
				}

				// Built-in functions take precedence and may not be overridden by a user function.
				if (FunctionLibrary != null && FunctionLibrary.TryGetValue(functionName.ToUpper(), out var existing) && existing.IsSystem)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
					return new CallState(string.Format(ErrorMessages.Returns.NoSuchFunction, functionName.ToUpperInvariant()));
				}

				// Bounds follow PennMUSH's do_function (function.c:1703-1721): an omitted maximum is
				// DEF_FUNCTION_ARGS, a negative one keeps its magnitude (PennMUSH's "do not split the
				// last argument" marker, which a user function has no way to honour), and either bound
				// is clamped to MAX_STACK_ARGS — the engine carries no more positional arguments than
				// that, so a larger number would be a promise it cannot keep.
				var minArgs = 0;
				var maxArgs = DefaultUserFunctionArguments;
				if (args.Count >= 4 && int.TryParse(args.GetValueOrDefault("3")?.Message?.ToPlainText(), out var parsedMin))
				{
					minArgs = Math.Clamp(parsedMin, 0, MaximumStackArguments);
				}
				if (args.Count >= 5 && int.TryParse(args.GetValueOrDefault("4")?.Message?.ToPlainText(), out var parsedMax))
				{
					maxArgs = Math.Min(Math.Abs(parsedMax), MaximumStackArguments);
				}

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
					parser, executor, executor, objSpec, LocateFlags.All, async targetObject =>
				{
					if (!await PermissionService.Controls(executor, targetObject))
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
						return new CallState(ErrorMessages.Returns.PermissionDenied);
					}

					if (await AttributeService.GetAttributeAsync(
							executor, targetObject, attribSpec, IAttributeService.AttributeMode.Read, false)
						is not SharpAttribute[] attributeChain)
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, attribSpec);
						return new CallState(ErrorMessages.Returns.NoSuchAttribute);
					}

					var attributeLongName = attributeChain.Last().LongName!.ToUpper();

					userFunctionService.Define(new UserDefinedFunction(
						Name: functionName,
						Object: targetObject.Object().DBRef,
						Attribute: attributeLongName,
						MinArgs: minArgs,
						MaxArgs: maxArgs,
						Enabled: true,
						AliasOf: null));

					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDefineWouldDefineFormat), executor, functionName, $"{targetObject.Object().DBRef}/{attributeLongName}");
					return CallState.Empty;
				});
			}
		}

		var registeredFunction = userFunctionService.Get(functionName);
		if (registeredFunction != null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoNameFormat), executor, registeredFunction.Name);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoTypeFormat), executor, "User-defined");
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoMinArgsFormat), executor, registeredFunction.MinArgs);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoMaxArgsFormat), executor, registeredFunction.MaxArgs);
			return CallState.Empty;
		}

		if (FunctionLibrary == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionLibraryUnavailable), executor);
			return new CallState(ErrorMessages.Returns.LibraryUnavailable);
		}

		var functionNameUpper = functionName.ToUpper();
		if (!FunctionLibrary.TryGetValue(functionNameUpper, out var functionInfo))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
			return new CallState(ErrorMessages.Returns.FunctionNotFound);
		}

		var (definition, isSystem) = functionInfo;
		var attr = definition.Attribute;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoNameFormat), executor, attr.Name);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoTypeFormat), executor, isSystem ? "Built-in" : "User-defined");
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoMinArgsFormat), executor, attr.MinArgs);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoMaxArgsFormat), executor, attr.MaxArgs);

		var flags = Enum.GetValues<FunctionFlags>()
			.Where(flag => flag != FunctionFlags.Regular && flag != FunctionFlags.Arg_Mask && attr.Flags.HasFlag(flag))
			.Select(flag => flag.ToString()).ToList();
		if (flags.Count == 0) flags.Add(nameof(FunctionFlags.Regular));

		if (flags.Count > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoFlagsFormat), executor, string.Join(" | ", flags));
		}

		if (attr.Restrict != null && attr.Restrict.Length > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoRestrictionsFormat), executor, string.Join(", ", attr.Restrict));
		}

		return CallState.Empty;
	}

	/// <summary>
	/// Re-registers a built-in function into the shared FunctionLibrary from the source-generated
	/// definition dictionary, undoing a prior <c>@function/delete</c> of a built-in. No-op if the
	/// name is not a generated built-in (e.g. it was a pure user function).
	/// </summary>
	private void RestoreBuiltinFunction(string functionName)
	{
		var key = functionName.ToLowerInvariant();
		if (Functions.Builtins.TryGetValue(key, out var definition))
		{
			FunctionLibrary[key] = (definition, true);
		}
	}

	/// <summary>
	/// The retroactive half of PennMUSH's <c>do_attribute_access</c> (<c>src/atr_tab.c:816-826</c>):
	/// every object's own copy of <paramref name="name"/> gets exactly <paramref name="flags"/> — its
	/// <c>branch</c> flag aside, which is Penn's AF_ROOT and is kept — and the executor as its creator.
	/// </summary>
	/// <remarks>
	/// One pass over the world, streamed an object at a time, each change a cache-invalidating command.
	/// The pass stops when the command's execution budget runs out; what it has changed stays changed,
	/// and the report says how far it got rather than claiming every copy was reached.
	/// </remarks>
	private async ValueTask RetroactiveAttributeAccessAsync(AnySharpObject executor, string name, SharpAttributeFlag[] flags)
	{
		var cancellationToken = ExecutionBudget.CurrentToken;
		var creator = await executor.Object().Owner.WithCancellation(cancellationToken);
		var path = name.Split('`');
		var (scanned, updated, failed) = (0, 0, 0);

		try
		{
			await foreach (var obj in Mediator.CreateStream(new GetAllObjectsQuery()).WithCancellation(cancellationToken))
			{
				scanned++;
				var copy = await Mediator.CreateStream(new GetAttributeQuery(obj.DBRef, path)).LastOrDefaultAsync(cancellationToken);
				if (copy is null || !copy.LongName.Equals(name, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				try
				{
					foreach (var flag in copy.Flags.Where(had => !IsBranchFlag(had) && !flags.Any(wanted => wanted.Name == had.Name)))
					{
						await Mediator.Send(new UnsetAttributeFlagCommand(obj.DBRef, copy, flag), cancellationToken);
					}

					foreach (var flag in flags.Where(wanted => !copy.Flags.Any(had => had.Name == wanted.Name)))
					{
						await Mediator.Send(new SetAttributeFlagCommand(obj.DBRef, copy, flag), cancellationToken);
					}

					await Mediator.Send(new SetAttributeOwnerCommand(obj.DBRef, path, creator), cancellationToken);
					updated++;
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					failed++;
					Logger.LogWarning(ex, "@attribute/retroactive could not update {Attribute} on {Object}", name, obj.DBRef);
				}
			}
		}
		catch (OperationCanceledException)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRetroactivePartialFormat),
				executor, scanned, updated, name, failed);
			return;
		}

		if (failed > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRetroactivePartialFormat),
				executor, scanned, updated, name, failed);
			return;
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRetroactiveUpdatedFormat),
			executor, updated, name);
	}

	/// <summary>Penn's AF_ROOT: whether the attribute has branches below it, which is structure, not permission.</summary>
	private static bool IsBranchFlag(SharpAttributeFlag flag)
		=> flag.Name.Equals("branch", StringComparison.OrdinalIgnoreCase);

	[SharpCommand(Name = "@ATTRIBUTE",
		Switches = ["ACCESS", "DELETE", "RENAME", "RETROACTIVE", "LIMIT", "ENUM", "DECOMPILE"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 2, ParameterNames = ["attribute", "options..."])]
	public async ValueTask<Option<CallState>> Attribute(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (switches.Contains("DECOMPILE"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var pattern = args.GetValueOrDefault("0")?.Message?.ToPlainText() is { Length: > 0 } given ? given : "*";
			var retroactive = switches.Contains("RETROACTIVE");

			// quick_wild over the whole name (src/atr_tab.c:1017): the general MUSH wildcard, caseless.
			var matcher = SoftcodeRegex.Wildcard(pattern);
			var matchingEntries = await Mediator.CreateStream(new GetAllAttributeEntriesQuery())
				.Where(entry => SoftcodeRegex.IsMatch(matcher, entry.Name))
				.ToArrayAsync();

			if (matchingEntries.Length == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNoMatchPatternFormat), executor, pattern);
				return CallState.Empty;
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileHeaderFormat), executor, matchingEntries.Length, pattern);

			foreach (var entry in matchingEntries.OrderBy(e => e.Name))
			{
				var flagList = string.Join(" ", entry.DefaultFlags);
				var retroFlag = retroactive ? "/retroactive" : "";
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileAccessFormat), executor, retroFlag, entry.Name, flagList);

				if (!string.IsNullOrEmpty(entry.Limit))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileLimitFormat), executor, entry.Name, entry.Limit);
				}

				if (entry.Enum is { Length: > 0 } choices)
				{
					// A delimiter other than space is written back, so the line re-creates the same enum.
					var target = entry.EnumDelimiter == ' ' ? entry.Name : $"{entry.EnumDelimiter} {entry.Name}";
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileEnumFormat), executor, target, string.Join(entry.EnumDelimiter, choices));
				}
			}

			return CallState.Empty;
		}

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyAttribute), executor);
			return new CallState(ErrorMessages.Returns.NoAttributeSpecified);
		}

		var attrName = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attrName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyAttribute), executor);
			return new CallState(ErrorMessages.Returns.NoAttributeSpecified);
		}

		if (switches.Contains("ACCESS"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (args.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyFlags), executor);
				return new CallState(ErrorMessages.Returns.NoFlagsSpecified);
			}

			var flagList = args["1"].Message?.ToPlainText() ?? "none";
			var retroactive = switches.Contains("RETROACTIVE");

			// strcasecmp(perms, "none"): no permissions at all, not a flag called NONE.
			var flagNames = flagList.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
				? []
				: flagList.Split(' ', StringSplitOptions.RemoveEmptyEntries)
					.Select(f => f.ToUpper())
					.ToArray();

			var allFlags = await Mediator.CreateStream(new GetAttributeFlagsQuery()).ToArrayAsync();
			foreach (var flagName in flagNames)
			{
				if (!allFlags.Any(f => f.Name.Equals(flagName, StringComparison.OrdinalIgnoreCase)))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandUnknownFlagFormat), executor, flagName);
					return new CallState(ErrorMessages.Returns.UnknownFlag);
				}
			}

			// Permissions only: the entry's limit or enum survives, as in Penn's do_attribute_access.
			var current = await Mediator.Send(new GetAttributeEntryQuery(attrName.ToUpper()));
			var entry = await Mediator.Send(new CreateAttributeEntryCommand(attrName.ToUpper(), flagNames,
				current?.Limit, current?.Enum, current?.EnumDelimiter ?? ' '));
			if (entry == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandFailedToCreate), executor);
				return new CallState(ErrorMessages.Returns.CreateFailed);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandPermissionsNowFormat), executor, attrName.ToUpperInvariant(), string.Join(" ", flagNames.Select(f => f.ToLowerInvariant())));

			if (retroactive)
			{
				await RetroactiveAttributeAccessAsync(executor, attrName.ToUpperInvariant(),
					[.. allFlags.Where(flag => flagNames.Contains(flag.Name, StringComparer.OrdinalIgnoreCase))]);
			}

			return CallState.Empty;
		}

		if (switches.Contains("DELETE"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var deleted = await Mediator.Send(new DeleteAttributeEntryCommand(attrName.ToUpper()));

			if (deleted)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRemovedFromTableFormat), executor, attrName);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandExistingCopiesRemain), executor);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundInTableFormat), executor, attrName);
				return new CallState(ErrorMessages.Returns.NotFound);
			}

			return CallState.Empty;
		}

		if (switches.Contains("RENAME"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (args.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyNewName), executor);
				return new CallState(ErrorMessages.Returns.NoNewNameSpecified);
			}

			var newName = args["1"].Message?.ToPlainText();
			if (string.IsNullOrEmpty(newName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyNewName), executor);
				return new CallState(ErrorMessages.Returns.NoNewNameSpecified);
			}

			var renamed = await Mediator.Send(new RenameAttributeEntryCommand(attrName.ToUpper(), newName.ToUpper()));

			if (renamed != null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRenamedFormat), executor, attrName, newName);
				// Note: Existing attribute instances keep their original names - this only affects new instances
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundInTableFormat), executor, attrName);
				return new CallState(ErrorMessages.Returns.NotFound);
			}

			return CallState.Empty;
		}

		if (switches.Contains("LIMIT") || switches.Contains("ENUM"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			// Penn's cmds.c tries /limit before /enum.
			return await SetAttributeRestrictionAsync(executor, attrName,
				args.GetValueOrDefault("1")?.Message?.ToPlainText() ?? string.Empty, isEnum: !switches.Contains("LIMIT"));
		}

		var attrEntry = await Mediator.Send(new GetAttributeEntryQuery(attrName.ToUpper()));

		if (attrEntry == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundNotErrorFormat), executor, attrName);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundNotError2), executor);
			return CallState.Empty;
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandInfoFormat), executor, attrEntry.Name);

		if (attrEntry.DefaultFlags.Any())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDefaultFlagsFormat), executor, string.Join(" ", attrEntry.DefaultFlags));
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDefaultFlagsNone), executor);
		}

		if (!string.IsNullOrEmpty(attrEntry.Limit))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandLimitPatternValueFormat), executor, attrEntry.Limit);
		}

		if (attrEntry.Enum != null && attrEntry.Enum.Any())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandEnumValuesFormat), executor, string.Join(attrEntry.EnumDelimiter, attrEntry.Enum));
		}

		return CallState.Empty;
	}

	/// <summary>
	/// PennMUSH's <c>do_attribute_limit</c> (<c>src/atr_tab.c:629-743</c>): <c>@attribute/limit</c> sets a
	/// caseless regexp every value must match, <c>@attribute/enum [&lt;delim&gt;] &lt;attr&gt;=&lt;list&gt;</c> the
	/// choices a value must name, and an empty restriction unsets either. The two replace each other, and
	/// the attribute must already be in the table. <see cref="SharpMUSH.Library.Services.AttributeValueRestriction"/> enforces them.
	/// </summary>
	private async ValueTask<Option<CallState>> SetAttributeRestrictionAsync(AnySharpObject executor, string target,
		string restriction, bool isEnum)
	{
		var name = target;
		var delimiter = ' ';
		string? limit = null;
		string[]? choices = null;

		if (restriction.Length > 0 && !isEnum)
		{
			try
			{
				SoftcodeRegex.Create(restriction, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
			}
			catch (ArgumentException)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandInvalidRegexp), executor);
				return new CallState(ErrorMessages.Returns.InvalidRegexp);
			}

			limit = restriction;
		}
		else if (restriction.Length > 0)
		{
			// "@attribute/enum | NAME=a|b": a delimiter is exactly one character before a space.
			if (target.IndexOf(' ') is var space and >= 0)
			{
				if (space != 1)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDelimiterOneCharacter), executor);
					return new CallState(ErrorMessages.Returns.InvalidArguments);
				}

				delimiter = target[0];
				name = target[2..];
			}

			choices = restriction.Split(delimiter, StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } split ? split : null;
		}

		name = name.Trim().TrimStart('@').ToUpperInvariant();
		if (await Mediator.Send(new GetAttributeEntryQuery(name)) is not SharpAttributeEntry entry)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotInTableUseAccess), executor);
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		var wasRestricted = !string.IsNullOrEmpty(entry.Limit) || entry.Enum is { Length: > 0 };
		await Mediator.Send(new CreateAttributeEntryCommand(entry.Name, entry.DefaultFlags, limit, choices, delimiter));

		if (limit is null && choices is null)
		{
			await NotifyService.NotifyLocalized(executor, wasRestricted
				? nameof(ErrorMessages.Notifications.AttributeCommandRestrictionUnsetFormat)
				: nameof(ErrorMessages.Notifications.AttributeCommandRestrictionAlreadyUnsetFormat), executor, entry.Name);
			return CallState.Empty;
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRestrictionSetFormat),
			executor, entry.Name, isEnum ? "enum" : "limit", limit ?? string.Join(delimiter, choices!));
		return CallState.Empty;
	}

	private enum DefinitionOperation { Default, List, Add, Delete, Letter, Type, Alias, Restrict, Decompile, Disable, Enable, Debug }

	private static DefinitionOperation SelectDefinitionOperation(IEnumerable<string> switches, bool power)
	{
		ReadOnlySpan<DefinitionOperation> precedence = power
			? [DefinitionOperation.List, DefinitionOperation.Add, DefinitionOperation.Delete, DefinitionOperation.Alias,
				DefinitionOperation.Letter, DefinitionOperation.Type, DefinitionOperation.Restrict, DefinitionOperation.Decompile,
				DefinitionOperation.Disable, DefinitionOperation.Enable]
			: [DefinitionOperation.List, DefinitionOperation.Add, DefinitionOperation.Delete, DefinitionOperation.Letter,
				DefinitionOperation.Type, DefinitionOperation.Alias, DefinitionOperation.Restrict, DefinitionOperation.Decompile,
				DefinitionOperation.Disable, DefinitionOperation.Enable, DefinitionOperation.Debug];
		foreach (var operation in precedence)
			if (switches.Contains(operation.ToString(), StringComparer.OrdinalIgnoreCase)) return operation;
		return DefinitionOperation.Default;
	}

	// PennMUSH src/flags.c:955 letter_to_flagptr: a letter is only taken by a definition whose object
	// types overlap, so two definitions with no type in common may share one
	// (game/txt/hlp/pennv177.hlp:20). The letter comparison is case-sensitive.
	private static ValueTask<string?> FindLetterConflict(
		IAsyncEnumerable<(string Name, string Symbol, string[] TypeRestrictions)> definitions,
		string ownName, string letter, string[] ownTypes)
		=> definitions
			.Where(definition =>
				!definition.Name.Equals(ownName, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(definition.Symbol, letter, StringComparison.Ordinal)
				&& definition.TypeRestrictions.Intersect(ownTypes, StringComparer.OrdinalIgnoreCase).Any())
			.Select(definition => (string?)definition.Name)
			.FirstOrDefaultAsync();

	[SharpCommand(Name = "@FLAG",
		Switches =
		[
			"ADD", "TYPE", "LETTER", "LIST", "RESTRICT", "DELETE", "ALIAS", "DISABLE", "ENABLE", "DEBUG", "DECOMPILE"
		], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 2, ParameterNames = ["object", "flag"])]
	public async ValueTask<Option<CallState>> Flag(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var operation = SelectDefinitionOperation(parser.CurrentState.Switches, power: false);

		if (operation != DefinitionOperation.Default)
		{
			return await EditDefinitionAsync(FlagRegistry, parser, executor, operation);
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagUsage), executor);
		return CallState.Empty;
	}

	[SharpCommand(Name = "@POWER",
		Switches = ["ADD", "TYPE", "LETTER", "LIST", "RESTRICT", "DELETE", "ALIAS", "DISABLE", "ENABLE", "DECOMPILE"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0, MaxArgs = 2, ParameterNames = ["object", "power"])]
	public async ValueTask<Option<CallState>> Power(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var operation = SelectDefinitionOperation(switches, power: true);

		if (operation != DefinitionOperation.Default)
		{
			return await EditDefinitionAsync(PowerRegistry, parser, executor, operation);
		}

		// A declared-but-unhandled switch must not fall through into the grant form below.
		if (switches.Any())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerUsage), executor);
			return CallState.Empty;
		}

		var powerArgs = parser.CurrentState.Arguments;
		var objectArg = powerArgs.Count > 0 ? powerArgs["0"].Message!.ToPlainText().Trim() : string.Empty;
		var powerArg = powerArgs.Count > 1 ? powerArgs["1"].Message!.ToPlainText() : string.Empty;

		if (string.IsNullOrWhiteSpace(powerArg))
		{
			// "@power <power>" describes the power itself. It does NOT list an object's powers.
			if (string.IsNullOrWhiteSpace(objectArg))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerUsage), executor);
				return CallState.Empty;
			}

			var namedPower = await ManipulateSharpObjectService.FindPower(objectArg);
			if (namedPower is null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoSuchPowerInfo), executor);
				return CallState.Empty;
			}

			var info = new System.Text.StringBuilder();
			info.AppendLine($"{"Name",9}: {namedPower.Name}");
			info.AppendLine($"{"Character",9}: {namedPower.Symbol}");
			info.AppendLine($"{"Aliases",9}: {namedPower.Alias}");
			info.AppendLine($"{"Type(s)",9}: {string.Join(" ", namedPower.TypeRestrictions)}");
			info.AppendLine($"{"Perms",9}: {string.Join(" ", namedPower.SetPermissions)}");
			info.Append($"{"ResetPrms",9}: {string.Join(" ", namedPower.UnsetPermissions)}");

			await NotifyService.Notify(executor, info.ToString(), executor);
			return new CallState(MarkupText.Plain(namedPower.Name));
		}

		// do_power refuses non-wizards before resolving <object>, so a non-wizard is told they may not
		// grant powers rather than that the object could not be found.
		if (!await executor.IsWizard())
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.OnlyWizardsMayGrantPowers);
			return CallState.Empty;
		}

		if (await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, objectArg, LocateFlags.All)
				is not AnySharpObject target)
		{
			return CallState.Empty;
		}

		await ManipulateSharpObjectService.SetOrUnsetPowers(executor, target, powerArg, true);
		return CallState.Empty;
	}

	/// <summary>One flag or power definition, as the definition-registry editor handles it.</summary>
	private sealed record RegistryEntry(string Name, string[]? Aliases, string Symbol, bool System, bool Disabled,
		string[] TypeRestrictions, string[] SetPermissions, string[] UnsetPermissions, string? Id);

	/// <summary>
	/// What <c>@flag</c> and <c>@power</c> differ in — PennMUSH runs both through one set of
	/// <c>src/flags.c</c> routines, parameterised by flagspace: where the definitions are stored, what
	/// <c>/add</c>'s second argument is (a flag's letter, a power's alias), whether a definition has one
	/// alias or a list, how one is listed and described, and the message keys each speaks with.
	/// </summary>
	private sealed record DefinitionRegistry(
		Func<IMediator, string, ValueTask<RegistryEntry?>> Find,
		Func<IMediator, IAsyncEnumerable<RegistryEntry>> All,
		Func<IMediator, string, string, ValueTask<bool>> Create,
		Func<IMediator, RegistryEntry, ValueTask<bool>> Update,
		Func<IMediator, string, ValueTask<bool>> Delete,
		Func<IMediator, string, bool, ValueTask<bool>> SetDisabled,
		bool SingleAlias,
		string[] ListHeader,
		Func<RegistryEntry, string> ListRow,
		Func<RegistryEntry, string[]> Describe,
		RegistryMessages Messages);

	/// <summary>The message keys (and, for the three unlocalised results, formats) of one registry.</summary>
	private sealed record RegistryMessages(
		string AddRequires, string NameAndSecondEmpty, string AlreadyExists, string Created, string FailedToCreate,
		string DeleteRequires, string NameEmpty, string NotFound, string CannotDeleteSystem, string Deleted, string FailedToDelete,
		string LetterRequires, string CannotModifySystem, string SingleCharacters, string LetterConflict, string FailedToUpdate,
		string LetterSet, string LetterCleared, string TypeRequires, string NameAndTypesEmpty, string TypeUpdated,
		string AliasRequires, string AliasSet, string RestrictRequires, string NameAndPermissionsEmpty, string PermissionsUpdated,
		string DecompileRequires, string DisableEnableRequires, string CannotDisableSystem,
		string DisabledFormat, string EnabledFormat, string FailedToDisableFormat, string FailedToEnableFormat);

	private static readonly DefinitionRegistry FlagRegistry = new(
		Find: async (mediator, name) => await mediator.Send(new GetObjectFlagQuery(name)) is { } flag ? FromFlag(flag) : null,
		All: mediator => mediator.CreateStream(new GetAllObjectFlagsQuery()).Select(FromFlag),
		Create: async (mediator, name, symbol) => await mediator.Send(new CreateObjectFlagCommand(
			name, null, symbol,
			false, // user-created flags are never system flags
			["FLAG^WIZARD"], ["FLAG^WIZARD"], ["PLAYER", "THING", "ROOM", "EXIT"])) is not null,
		Update: async (mediator, flag) => await mediator.Send(new UpdateObjectFlagCommand(
			flag.Name, flag.Aliases, flag.Symbol, flag.SetPermissions, flag.UnsetPermissions, flag.TypeRestrictions)),
		Delete: async (mediator, name) => await mediator.Send(new DeleteObjectFlagCommand(name)),
		SetDisabled: async (mediator, name, disabled) => await mediator.Send(new SetObjectFlagDisabledCommand(name, disabled)),
		SingleAlias: false,
		ListHeader:
		[
			"Object Flags:",
			"Name                 Symbol Type Restrictions",
			"-------------------- ------ -------------------"
		],
		ListRow: flag => $"{flag.Name,-20} {flag.Symbol,-6} {string.Join(",", flag.TypeRestrictions)}",
		Describe: flag =>
		[
			$"Flag: {flag.Name}",
			$"Symbol: {flag.Symbol}",
			$"System: {(flag.System ? "Yes" : "No")}",
			$"Disabled: {(flag.Disabled ? "Yes" : "No")}",
			$"Aliases: {(flag.Aliases is { Length: > 0 } aliases ? string.Join(", ", aliases) : "none")}",
			$"Type Restrictions: {string.Join(", ", flag.TypeRestrictions)}",
			$"Set Permissions: {string.Join(", ", flag.SetPermissions)}",
			$"Unset Permissions: {string.Join(", ", flag.UnsetPermissions)}"
		],
		Messages: new(
			AddRequires: nameof(ErrorMessages.Notifications.FlagAddRequiresNameAndSymbol),
			NameAndSecondEmpty: nameof(ErrorMessages.Notifications.FlagNameAndSymbolCannotBeEmpty),
			AlreadyExists: nameof(ErrorMessages.Notifications.FlagAlreadyExistsFormat),
			Created: nameof(ErrorMessages.Notifications.FlagCreatedWithSymbolFormat),
			FailedToCreate: nameof(ErrorMessages.Notifications.FailedToCreateFlagFormat),
			DeleteRequires: nameof(ErrorMessages.Notifications.FlagDeleteRequiresName),
			NameEmpty: nameof(ErrorMessages.Notifications.FlagNameCannotBeEmpty),
			NotFound: nameof(ErrorMessages.Notifications.FlagNotFoundFormat),
			CannotDeleteSystem: nameof(ErrorMessages.Notifications.CannotDeleteSystemFlagFormat),
			Deleted: nameof(ErrorMessages.Notifications.FlagDeletedFormat),
			FailedToDelete: nameof(ErrorMessages.Notifications.FailedToDeleteFlagFormat),
			LetterRequires: nameof(ErrorMessages.Notifications.FlagLetterRequiresName),
			CannotModifySystem: nameof(ErrorMessages.Notifications.CannotModifySystemFlagFormat),
			SingleCharacters: nameof(ErrorMessages.Notifications.FlagCharactersMustBeSingleCharacters),
			LetterConflict: nameof(ErrorMessages.Notifications.FlagLetterConflictFormat),
			FailedToUpdate: nameof(ErrorMessages.Notifications.FailedToUpdateFlagFormat),
			LetterSet: nameof(ErrorMessages.Notifications.FlagLetterSetFormat),
			LetterCleared: nameof(ErrorMessages.Notifications.FlagLetterClearedFormat),
			TypeRequires: nameof(ErrorMessages.Notifications.FlagTypeRequiresNameAndTypes),
			NameAndTypesEmpty: nameof(ErrorMessages.Notifications.FlagNameAndTypesCannotBeEmpty),
			TypeUpdated: nameof(ErrorMessages.Notifications.FlagTypeUpdatedFormat),
			AliasRequires: nameof(ErrorMessages.Notifications.FlagAliasRequiresNameAndAliases),
			AliasSet: nameof(ErrorMessages.Notifications.FlagAliasesSetFormat),
			RestrictRequires: nameof(ErrorMessages.Notifications.FlagRestrictRequiresNameAndPermissions),
			NameAndPermissionsEmpty: nameof(ErrorMessages.Notifications.FlagNameAndPermissionsCannotBeEmpty),
			PermissionsUpdated: nameof(ErrorMessages.Notifications.FlagPermissionsUpdatedFormat),
			DecompileRequires: nameof(ErrorMessages.Notifications.FlagDecompileRequiresName),
			DisableEnableRequires: nameof(ErrorMessages.Notifications.FlagDisableEnableRequiresNameFormat),
			CannotDisableSystem: nameof(ErrorMessages.Notifications.CannotDeleteSystemFlagFormat),
			DisabledFormat: ErrorMessages.Notifications.FlagDisabledFormat,
			EnabledFormat: ErrorMessages.Notifications.FlagEnabledFormat,
			FailedToDisableFormat: ErrorMessages.Notifications.FailedToDisableFlagFormat,
			FailedToEnableFormat: ErrorMessages.Notifications.FailedToEnableFlagFormat));

	private static readonly DefinitionRegistry PowerRegistry = new(
		Find: async (mediator, name) => await mediator.Send(new GetPowerQuery(name)) is { } power ? FromPower(power) : null,
		All: mediator => mediator.CreateStream(new GetPowersQuery()).Select(FromPower),
		Create: async (mediator, name, alias) => await mediator.Send(new CreatePowerCommand(
			name, alias,
			string.Empty, // PennMUSH @power/add defaults <letter> to none
			false, // user-created powers are never system powers
			["FLAG^WIZARD"], ["FLAG^WIZARD"], ["PLAYER"])) is not null,
		Update: async (mediator, power) => await mediator.Send(new UpdatePowerCommand(
			power.Name, power.Aliases is [var alias, ..] ? alias : string.Empty, power.Symbol,
			power.SetPermissions, power.UnsetPermissions, power.TypeRestrictions)),
		Delete: async (mediator, name) => await mediator.Send(new DeletePowerCommand(name)),
		SetDisabled: async (mediator, name, disabled) => await mediator.Send(new SetPowerDisabledCommand(name, disabled)),
		SingleAlias: true,
		ListHeader:
		[
			"Object Powers:",
			"Name                 Symbol Alias              Type Restrictions",
			"-------------------- ------ ------------------ -------------------"
		],
		ListRow: power => $"{power.Name,-20} {power.Symbol,-6} {power.Aliases?.FirstOrDefault(),-18} {string.Join(",", power.TypeRestrictions)}",
		Describe: power =>
		[
			$"Power: {power.Name}",
			$"Symbol: {power.Symbol}",
			$"Alias: {power.Aliases?.FirstOrDefault()}",
			$"System: {(power.System ? "Yes" : "No")}",
			$"Disabled: {(power.Disabled ? "Yes" : "No")}",
			$"Type Restrictions: {string.Join(", ", power.TypeRestrictions)}",
			$"Set Permissions: {string.Join(", ", power.SetPermissions)}",
			$"Unset Permissions: {string.Join(", ", power.UnsetPermissions)}"
		],
		Messages: new(
			AddRequires: nameof(ErrorMessages.Notifications.PowerAddRequiresNameAndAlias),
			NameAndSecondEmpty: nameof(ErrorMessages.Notifications.PowerNameAndAliasCannotBeEmpty),
			AlreadyExists: nameof(ErrorMessages.Notifications.PowerAlreadyExistsFormat),
			Created: nameof(ErrorMessages.Notifications.PowerCreatedWithAliasFormat),
			FailedToCreate: nameof(ErrorMessages.Notifications.FailedToCreatePowerFormat),
			DeleteRequires: nameof(ErrorMessages.Notifications.PowerDeleteRequiresName),
			NameEmpty: nameof(ErrorMessages.Notifications.PowerNameCannotBeEmpty),
			NotFound: nameof(ErrorMessages.Notifications.PowerNotFoundFormat),
			CannotDeleteSystem: nameof(ErrorMessages.Notifications.CannotDeleteSystemPowerFormat),
			Deleted: nameof(ErrorMessages.Notifications.PowerDeletedFormat),
			FailedToDelete: nameof(ErrorMessages.Notifications.FailedToDeletePowerFormat),
			LetterRequires: nameof(ErrorMessages.Notifications.PowerLetterRequiresName),
			CannotModifySystem: nameof(ErrorMessages.Notifications.CannotModifySystemPowerFormat),
			SingleCharacters: nameof(ErrorMessages.Notifications.PowerCharactersMustBeSingleCharacters),
			LetterConflict: nameof(ErrorMessages.Notifications.PowerLetterConflictFormat),
			FailedToUpdate: nameof(ErrorMessages.Notifications.FailedToUpdatePowerFormat),
			LetterSet: nameof(ErrorMessages.Notifications.PowerLetterSetFormat),
			LetterCleared: nameof(ErrorMessages.Notifications.PowerLetterClearedFormat),
			TypeRequires: nameof(ErrorMessages.Notifications.PowerTypeRequiresNameAndTypes),
			NameAndTypesEmpty: nameof(ErrorMessages.Notifications.PowerNameAndTypesCannotBeEmpty),
			TypeUpdated: nameof(ErrorMessages.Notifications.PowerTypeUpdatedFormat),
			AliasRequires: nameof(ErrorMessages.Notifications.PowerAliasRequiresNameAndAlias),
			AliasSet: nameof(ErrorMessages.Notifications.PowerAliasChangedFormat),
			RestrictRequires: nameof(ErrorMessages.Notifications.PowerRestrictRequiresNameAndPermissions),
			NameAndPermissionsEmpty: nameof(ErrorMessages.Notifications.PowerNameAndPermissionsCannotBeEmpty),
			PermissionsUpdated: nameof(ErrorMessages.Notifications.PowerPermissionsUpdatedFormat),
			DecompileRequires: nameof(ErrorMessages.Notifications.PowerDecompileRequiresName),
			DisableEnableRequires: nameof(ErrorMessages.Notifications.PowerDisableEnableRequiresNameFormat),
			CannotDisableSystem: nameof(ErrorMessages.Notifications.CannotDisableSystemPowerFormat),
			DisabledFormat: ErrorMessages.Notifications.PowerDisabledFormat,
			EnabledFormat: ErrorMessages.Notifications.PowerEnabledFormat,
			FailedToDisableFormat: ErrorMessages.Notifications.FailedToDisablePowerFormat,
			FailedToEnableFormat: ErrorMessages.Notifications.FailedToEnablePowerFormat));

	private static RegistryEntry FromFlag(SharpObjectFlag flag)
		=> new(flag.Name, flag.Aliases, flag.Symbol, flag.System, flag.Disabled,
			flag.TypeRestrictions, flag.SetPermissions, flag.UnsetPermissions, flag.Id);

	private static RegistryEntry FromPower(SharpPower power)
		=> new(power.Name, string.IsNullOrEmpty(power.Alias) ? [] : [power.Alias], power.Symbol, power.System, power.Disabled,
			power.TypeRestrictions, power.SetPermissions, power.UnsetPermissions, power.Id);

	/// <summary>
	/// The switches <c>@flag</c> and <c>@power</c> share — PennMUSH's <c>do_list_flags</c>,
	/// <c>do_flag_info</c>, <c>do_flag_add</c>, <c>do_flag_delete</c>, <c>do_flag_letter</c>,
	/// <c>do_flag_type</c>, <c>do_flag_alias</c>, <c>do_flag_restrict</c> and <c>do_flag_disable</c>
	/// (<c>src/flags.c</c>) — over whichever <paramref name="registry"/> the command edits.
	/// </summary>
	private async ValueTask<Option<CallState>> EditDefinitionAsync(DefinitionRegistry registry, IMUSHCodeParser parser,
		AnySharpObject executor, DefinitionOperation operation)
	{
		var arguments = parser.CurrentState.Arguments;
		var keys = registry.Messages;
		string Argument(int index) => arguments.Count > index ? arguments[index.ToString()].Message!.ToPlainText() : string.Empty;

		async ValueTask<CallState> Say(string key, params object[] values)
		{
			await NotifyService.NotifyLocalized(executor, key, executor, values);
			return CallState.Empty;
		}

		if (operation == DefinitionOperation.List)
		{
			// list_all_flags: a wildcard over the names, and disabled definitions only for God.
			var pattern = Argument(0).Trim();
			var matcher = pattern.Length == 0 ? null : SoftcodeRegex.Wildcard(pattern);
			var rows = await registry.All(Mediator)
				.Where(entry => (!entry.Disabled || executor.IsGod()) && (matcher is null || SoftcodeRegex.IsMatch(matcher, entry.Name)))
				.Select(registry.ListRow)
				.ToArrayAsync();
			await NotifyService.Notify(executor, string.Join(Environment.NewLine, [.. registry.ListHeader, .. rows]), executor);
			return CallState.Empty;
		}

		// Authorize the selected operation, not an unrelated switch in the same request.
		if (operation is not (DefinitionOperation.Decompile or DefinitionOperation.Debug) && !executor.IsGod())
		{
			return await Say(nameof(ErrorMessages.Notifications.NotEnoughMagic));
		}

		if (operation == DefinitionOperation.Debug && !await executor.IsWizard())
		{
			return await Say(nameof(ErrorMessages.Notifications.PermissionDenied));
		}

		if (operation == DefinitionOperation.Add)
		{
			if (arguments.Count < 2) return await Say(keys.AddRequires);
			var (name, second) = (Argument(0), Argument(1));
			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(second)) return await Say(keys.NameAndSecondEmpty);
			if (await registry.Find(Mediator, name.ToUpperInvariant()) is not null) return await Say(keys.AlreadyExists, name);

			// A flag's second argument is its letter, kept as typed; a power's is its alias.
			if (!await registry.Create(Mediator, name.ToUpperInvariant(), registry.SingleAlias ? second.ToUpperInvariant() : second))
			{
				return await Say(keys.FailedToCreate, name);
			}

			await Say(keys.Created, name, second);
			return new CallState(MarkupText.Plain(name));
		}

		var requires = operation switch
		{
			DefinitionOperation.Delete => keys.DeleteRequires,
			DefinitionOperation.Letter => keys.LetterRequires,
			DefinitionOperation.Type => keys.TypeRequires,
			DefinitionOperation.Alias => keys.AliasRequires,
			DefinitionOperation.Restrict => keys.RestrictRequires,
			DefinitionOperation.Decompile => keys.DecompileRequires,
			DefinitionOperation.Debug => nameof(ErrorMessages.Notifications.FlagDebugRequiresName),
			_ => keys.DisableEnableRequires
		};
		var needed = operation is DefinitionOperation.Type or DefinitionOperation.Alias or DefinitionOperation.Restrict ? 2 : 1;
		if (arguments.Count < needed)
		{
			return operation is DefinitionOperation.Disable or DefinitionOperation.Enable
				? await Say(requires, operation == DefinitionOperation.Disable ? "DISABLE" : "ENABLE")
				: await Say(requires);
		}

		var typed = operation == DefinitionOperation.Letter ? Argument(0).Trim() : Argument(0);
		var value = Argument(1);
		var blank = operation switch
		{
			DefinitionOperation.Type when string.IsNullOrWhiteSpace(typed) || string.IsNullOrWhiteSpace(value) => keys.NameAndTypesEmpty,
			DefinitionOperation.Restrict when string.IsNullOrWhiteSpace(typed) || string.IsNullOrWhiteSpace(value) => keys.NameAndPermissionsEmpty,
			DefinitionOperation.Alias when registry.SingleAlias && (string.IsNullOrWhiteSpace(typed) || string.IsNullOrWhiteSpace(value)) => keys.NameAndSecondEmpty,
			DefinitionOperation.Decompile or DefinitionOperation.Debug => null,
			_ when string.IsNullOrWhiteSpace(typed) => keys.NameEmpty,
			_ => null
		};
		if (blank is not null) return await Say(blank);

		if (await registry.Find(Mediator, typed.ToUpperInvariant()) is not { } entry)
		{
			return await Say(keys.NotFound, typed);
		}

		if (operation is DefinitionOperation.Decompile or DefinitionOperation.Debug)
		{
			var lines = registry.Describe(entry);
			string[] output = operation == DefinitionOperation.Debug
				? [$"DEBUG - {lines[0]}", $"ID: {entry.Id ?? "N/A"}", .. lines[1..]]
				: lines;
			await NotifyService.Notify(executor, string.Join(Environment.NewLine, output), executor);
			return CallState.Empty;
		}

		if (entry.System)
		{
			return await Say(operation switch
			{
				DefinitionOperation.Delete => keys.CannotDeleteSystem,
				DefinitionOperation.Disable or DefinitionOperation.Enable => keys.CannotDisableSystem,
				_ => keys.CannotModifySystem
			}, typed);
		}

		return operation switch
		{
			DefinitionOperation.Delete => await DeleteDefinitionAsync(registry, entry, typed, Say),
			DefinitionOperation.Letter => await LetterDefinitionAsync(registry, entry, value.Trim(), typed, Say),
			DefinitionOperation.Disable or DefinitionOperation.Enable
				=> await DisableDefinitionAsync(registry, executor, entry, typed, operation == DefinitionOperation.Disable),
			_ => await UpdateDefinitionAsync(registry, operation, entry, typed, value, Say)
		};
	}

	private async ValueTask<CallState> DeleteDefinitionAsync(DefinitionRegistry registry, RegistryEntry entry, string typed,
		Func<string, object[], ValueTask<CallState>> say)
	{
		if (!await registry.Delete(Mediator, entry.Name)) return await say(registry.Messages.FailedToDelete, [typed]);
		await say(registry.Messages.Deleted, [typed]);
		return new CallState(MarkupText.Plain(typed));
	}

	/// <summary><c>do_flag_letter</c>: an absent and an empty letter alike clear it; a letter is one character.</summary>
	private async ValueTask<CallState> LetterDefinitionAsync(DefinitionRegistry registry, RegistryEntry entry, string letter, string typed,
		Func<string, object[], ValueTask<CallState>> say)
	{
		var keys = registry.Messages;
		if (letter.Length > 1) return await say(keys.SingleCharacters, []);

		// letter_to_flagptr's `n->tab == &ptab_flag` guard makes this unreachable for the POWER
		// flagspace; it is implemented as written, not as reached.
		if (letter.Length == 1
				&& await FindLetterConflict(registry.All(Mediator).Select(x => (x.Name, x.Symbol, x.TypeRestrictions)),
					entry.Name, letter, entry.TypeRestrictions) is { } conflict)
		{
			return await say(keys.LetterConflict, [conflict]);
		}

		if (!await registry.Update(Mediator, entry with { Symbol = letter })) return await say(keys.FailedToUpdate, [typed]);

		await (letter.Length == 1 ? say(keys.LetterSet, [entry.Name, letter]) : say(keys.LetterCleared, [entry.Name]));
		return new CallState(MarkupText.Plain(entry.Name));
	}

	/// <summary><c>do_flag_type</c>, <c>do_flag_alias</c> and <c>do_flag_restrict</c>: one field replaced.</summary>
	private async ValueTask<CallState> UpdateDefinitionAsync(DefinitionRegistry registry, DefinitionOperation operation,
		RegistryEntry entry, string typed, string value, Func<string, object[], ValueTask<CallState>> say)
	{
		var keys = registry.Messages;
		var words = value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
		var (updated, key, shown) = operation switch
		{
			DefinitionOperation.Type => (entry with { TypeRestrictions = [.. words.Select(t => t.ToUpper())] }, keys.TypeUpdated,
				string.Join(", ", words.Select(t => t.ToUpper()))),
			DefinitionOperation.Restrict => (entry with { SetPermissions = words, UnsetPermissions = words }, keys.PermissionsUpdated,
				string.Join(", ", words)),
			_ when registry.SingleAlias => (entry with { Aliases = [value.ToUpper()] }, keys.AliasSet, value),
			_ => (entry with { Aliases = words.Length > 0 ? [.. words.Select(a => a.ToUpper())] : null }, keys.AliasSet,
				words.Length > 0 ? string.Join(", ", words.Select(a => a.ToUpper())) : "none")
		};

		if (!await registry.Update(Mediator, updated)) return await say(keys.FailedToUpdate, [typed]);
		await say(key, [typed, shown]);
		return new CallState(MarkupText.Plain(typed));
	}

	private async ValueTask<CallState> DisableDefinitionAsync(DefinitionRegistry registry, AnySharpObject executor,
		RegistryEntry entry, string typed, bool disable)
	{
		var keys = registry.Messages;
		var done = await registry.SetDisabled(Mediator, entry.Name, disable);
		var format = (done, disable) switch
		{
			(true, true) => keys.DisabledFormat,
			(true, false) => keys.EnabledFormat,
			(false, true) => keys.FailedToDisableFormat,
			(false, false) => keys.FailedToEnableFormat
		};

		await NotifyService.Notify(executor, string.Format(format, typed), executor);
		return done ? new CallState(MarkupText.Plain(typed)) : CallState.Empty;
	}

	[SharpCommand(Name = "@HOOK",
		Switches =
		[
			"LIST", "AFTER", "BEFORE", "EXTEND", "IGSWITCH", "IGNORE", "OVERRIDE", "INPLACE", "INLINE", "LOCALIZE",
			"CLEARREGS", "NOBREAK"
		], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, CommandLock = "FLAG^WIZARD|POWER^HOOK", MinArgs = 0, ParameterNames = ["type", "object/attribute"])]
	public async ValueTask<Option<CallState>> Hook(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (!await executor.IsWizard())
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
		}

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookMustSpecifyCommandName), executor);
			return new CallState(ErrorMessages.Returns.NoCommandSpecified);
		}

		var commandName = args["0"].Message?.ToPlainText()?.ToUpper();
		if (string.IsNullOrEmpty(commandName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookMustSpecifyCommandName), executor);
			return new CallState(ErrorMessages.Returns.NoCommandSpecified);
		}

		// do_hook resolves the name with command_find and then works on cmd->hooks, so naming an alias
		// hooks the command it aliases (command.c:2589) — and reports cmd->name back. A name no command
		// answers to is left as typed, so nothing that hooks an unknown name changes meaning here.
		commandName = FindCommand(commandName)?.LibraryInformation.Attribute.Name ?? commandName;

		if (switches.Contains("LIST"))
		{
			var hooks = await HookService.GetAllHooksAsync(commandName);
			if (hooks.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookNoHooksForCommandFormat), executor, commandName);
				return CallState.Empty;
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookListHeaderFormat), executor, commandName);
			foreach (var (hookType, hook) in hooks)
			{
				var flags = new List<string>();
				if (hook.Inline) flags.Add("inline");
				if (hook.NoBreak) flags.Add("nobreak");
				if (hook.Localize) flags.Add("localize");
				if (hook.ClearRegs) flags.Add("clearregs");

				var flagStr = flags.Count > 0 ? $" ({string.Join(", ", flags)})" : "";
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookEntryFormat), executor, hookType, hook.TargetObject, hook.AttributeName, flagStr);
			}
			return CallState.Empty;
		}

		var hookTypes = new[] { "IGNORE", "OVERRIDE", "BEFORE", "AFTER", "EXTEND", "IGSWITCH" };
		var selectedHookType = hookTypes.FirstOrDefault(switches.Contains);

		if (selectedHookType == "IGSWITCH")
		{
			selectedHookType = "EXTEND";
		}

		if (selectedHookType == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookMustSpecifyType), executor);
			return new CallState(ErrorMessages.Returns.NoHookType);
		}

		if (args.Count < 2 || string.IsNullOrWhiteSpace(args["1"].Message?.ToPlainText()))
		{
			var cleared = await HookService.ClearHookAsync(commandName, selectedHookType);
			if (cleared)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookClearedFormat), executor, selectedHookType, commandName);
				return CallState.Empty;
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookNotSetFormat), executor, selectedHookType, commandName);
			return new CallState(ErrorMessages.Returns.NoHook);
		}

		// PennMUSH form (CMD_T_EQSPLIT | CMD_T_RS_ARGS): @hook/<type> <command> = <object>, <attribute>.
		// CB.RSArgs has already split the RHS on commas, so args["1"] = object and args["2"] = attribute.
		// Read them directly — re-splitting args["1"] dropped the attribute and silently defaulted the
		// hook to cmd.<type>.
		var objectRef = args["1"].Message!.ToPlainText().Trim();

		if (string.IsNullOrWhiteSpace(objectRef))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookMustSpecifyObject), executor);
			return new CallState(ErrorMessages.Returns.NoObject);
		}

		var maybeObject = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor,
			objectRef, LocateFlags.All);

		if (maybeObject is not AnySharpObject targetObject)
		{
			return CallState.Empty;
		}
		var dbref = targetObject.Object().DBRef;

		var attributeArg = args.Count > 2 ? args["2"].Message?.ToPlainText() : null;
		var attributeName = !string.IsNullOrWhiteSpace(attributeArg)
			? attributeArg.Trim()
			: $"cmd.{selectedHookType.ToLower()}";

		var attrResult = await AttributeService.GetAttributeAsync(executor, targetObject,
			attributeName, IAttributeService.AttributeMode.Read);

		if (attrResult.IsError)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookAttributeNotFoundFormat), executor, attributeName, dbref);
			return new CallState(ErrorMessages.Returns.NoAttribute);
		}

		var inline = switches.Contains("INLINE");
		var inplace = switches.Contains("INPLACE");
		var nobreak = switches.Contains("NOBREAK") || inplace;
		var localize = switches.Contains("LOCALIZE") || inplace;
		var clearregs = switches.Contains("CLEARREGS") || inplace;

		await HookService.SetHookAsync(commandName, selectedHookType, dbref, attributeName,
			inline || inplace, nobreak, localize, clearregs);

		var flagDesc = inline || inplace ? " (inline)" : "";
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookSetFormat), executor, selectedHookType, commandName, flagDesc);

		return CallState.Empty;
	}

	/// <summary>
	/// The eight things <c>@list</c> can list. PennMUSH spells each of them both ways — as a switch
	/// (<c>cmd_list</c>) and as an argument (<c>do_list</c>), both in src/cmds.c — so SharpMUSH does too.
	/// </summary>
	private enum ListKind
	{
		Motd,
		Functions,
		Commands,
		Attribs,
		Locks,
		Flags,
		Powers,
		Allocations
	}

	/// <summary>
	/// Resolves <c>@list &lt;type&gt;</c>'s argument the way PennMUSH's <c>do_list</c> (src/cmds.c) does:
	/// in that order, and with that mix of prefix and exact matching. "commands", "functions", "powers",
	/// "locks" and "allocations" accept any non-empty prefix (<c>string_prefixe</c>); "motd", "attribs"
	/// and "flags" must be spelled in full (<c>strcasecmp</c>).
	/// </summary>
	/// <remarks>
	/// The order is load-bearing, not incidental: "f" reaches <c>functions</c> by prefix before it can
	/// reach the exact-match-only <c>flags</c>, exactly as it does in PennMUSH.
	/// </remarks>
	private ListKind? ResolveListKind(string argument)
	{
		var arg = argument.Trim();
		if (arg.Length == 0) return null;

		bool Prefix(string full) => full.StartsWith(arg, StringComparison.OrdinalIgnoreCase);
		bool Exact(string full) => full.Equals(arg, StringComparison.OrdinalIgnoreCase);

		if (Prefix("commands")) return ListKind.Commands;
		if (Prefix("functions")) return ListKind.Functions;
		if (Exact("motd")) return ListKind.Motd;
		if (Exact("attribs")) return ListKind.Attribs;
		if (Exact("flags")) return ListKind.Flags;
		if (Prefix("powers")) return ListKind.Powers;
		if (Prefix("locks")) return ListKind.Locks;
		if (Prefix("allocations")) return ListKind.Allocations;

		return null;
	}

	[SharpCommand(Name = "@LIST",
		Switches =
		[
			"LOWERCASE", "MOTD", "LOCKS", "FLAGS", "FUNCTIONS", "POWERS", "COMMANDS", "ATTRIBS", "ALLOCATIONS", "ALL",
			"BUILTIN", "LOCAL"
		], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["type"])]
	public async ValueTask<Option<CallState>> List(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var useLowercase = switches.Contains("LOWERCASE");

		// PennMUSH's cmd_list consults the switches first and only falls through to do_list — which reads
		// the same eight names off the argument — when none of them is set. A switch therefore still wins
		// over a contradicting argument, and `@list/lowercase commands` keeps working.
		var kind =
			switches.Contains("MOTD") ? ListKind.Motd
			: switches.Contains("FUNCTIONS") ? ListKind.Functions
			: switches.Contains("COMMANDS") ? ListKind.Commands
			: switches.Contains("ATTRIBS") ? ListKind.Attribs
			: switches.Contains("LOCKS") ? ListKind.Locks
			: switches.Contains("FLAGS") ? ListKind.Flags
			: switches.Contains("POWERS") ? ListKind.Powers
			: switches.Contains("ALLOCATIONS") ? ListKind.Allocations
			: ResolveListKind(parser.CurrentState.Arguments.TryGetValue("0", out var typeArg)
				? typeArg.Message?.ToPlainText() ?? string.Empty
				: string.Empty);

		if (kind is null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListNotUnderstood), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Motd)
		{
			var isWizard = await executor.IsWizard();

			var motdFile = Configuration.CurrentValue.Message.MessageOfTheDayFile;
			var motdHtmlFile = Configuration.CurrentValue.Message.MessageOfTheDayHtmlFile;

			await NotifyService.Notify(executor, "Current Message of the Day settings:", executor);
			await NotifyService.Notify(executor, $"  Connect MOTD File: {motdFile ?? "(not set)"}", executor);
			await NotifyService.Notify(executor, $"  Connect MOTD HTML: {motdHtmlFile ?? "(not set)"}", executor);

			if (isWizard)
			{
				var wizmotdFile = Configuration.CurrentValue.Message.WizMessageOfTheDayFile;
				var wizmotdHtmlFile = Configuration.CurrentValue.Message.WizMessageOfTheDayHtmlFile;

				await NotifyService.Notify(executor, $"  Wizard MOTD File: {wizmotdFile ?? "(not set)"}", executor);
				await NotifyService.Notify(executor, $"  Wizard MOTD HTML: {wizmotdHtmlFile ?? "(not set)"}", executor);
			}

			return CallState.Empty;
		}

		if (kind == ListKind.Flags)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Object Flags:" : "OBJECT FLAGS:";
			output.AppendLine(header);

			var headerLine = useLowercase
				? "name                 symbol type restrictions"
				: "NAME                 SYMBOL TYPE RESTRICTIONS";
			output.AppendLine(headerLine);
			output.AppendLine("-------------------- ------ -------------------");

			var flags = Mediator.CreateStream(new GetAllObjectFlagsQuery());
			await foreach (var flag in flags)
			{
				var flagName = useLowercase ? flag.Name?.ToLower() ?? "" : flag.Name ?? "";
				var symbol = useLowercase ? flag.Symbol?.ToLower() ?? "" : flag.Symbol ?? "";
				var types = string.Join(",", (flag.TypeRestrictions ?? []).Select(t => useLowercase ? t?.ToLower() ?? "" : t ?? ""));
				output.AppendLine($"{flagName,-20} {symbol,-6} {types}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Powers)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Object Powers:" : "OBJECT POWERS:";
			output.AppendLine(header);

			var headerLine = useLowercase
				? "name                 symbol alias              type restrictions"
				: "NAME                 SYMBOL ALIAS              TYPE RESTRICTIONS";
			output.AppendLine(headerLine);
			output.AppendLine("-------------------- ------ ------------------ -------------------");

			var powers = Mediator.CreateStream(new GetPowersQuery());
			await foreach (var power in powers)
			{
				var powerName = useLowercase ? power.Name.ToLower() : power.Name;
				var alias = useLowercase ? power.Alias.ToLower() : power.Alias;
				// A power's letter is case-sensitive, so /lowercase never folds it.
				var types = string.Join(",", power.TypeRestrictions.Select(t => useLowercase ? t.ToLower() : t));
				output.AppendLine($"{powerName,-20} {power.Symbol,-6} {alias,-18} {types}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Locks)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Lock Types:" : "LOCK TYPES:";
			output.AppendLine(header);

			var lockTypes = Enum.GetNames(typeof(LockType));
			foreach (var lockType in lockTypes.OrderBy(x => x))
			{
				var displayName = useLowercase ? lockType.ToLower() : lockType.ToUpper();
				output.AppendLine($"  {displayName}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Attribs)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Standard Attributes:" : "STANDARD ATTRIBUTES:";
			output.AppendLine(header);

			var attributes = Mediator.CreateStream(new GetAllAttributeEntriesQuery());
			await foreach (var attr in attributes.OrderBy(x => x.Name))
			{
				var attrName = useLowercase ? attr.Name.ToLower() : attr.Name;
				output.AppendLine($"  {attrName}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Commands)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Commands:" : "COMMANDS:";
			output.AppendLine(header);

			var filterBuiltin = switches.Contains("BUILTIN");
			var filterLocal = switches.Contains("LOCAL");

			var commandPairs = CommandLibrary.AsEnumerable();

			if (filterBuiltin && !filterLocal)
			{
				commandPairs = commandPairs.Where(kvp => kvp.Value.IsSystem);
			}
			else if (filterLocal && !filterBuiltin)
			{
				commandPairs = commandPairs.Where(kvp => !kvp.Value.IsSystem);
			}

			var commands = commandPairs
				.Select(kvp => kvp.Value.LibraryInformation.Attribute.Name)
				.Distinct()
				.OrderBy(x => x);

			foreach (var displayName in commands.Select(cmdName => useLowercase ? cmdName.ToLower() : cmdName))
			{
				output.AppendLine($"  {displayName}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Functions)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Functions:" : "FUNCTIONS:";
			output.AppendLine(header);

			var filterBuiltin = switches.Contains("BUILTIN");
			var filterLocal = switches.Contains("LOCAL");

			var functionPairs = FunctionLibrary.AsEnumerable();

			if (filterBuiltin && !filterLocal)
			{
				functionPairs = functionPairs.Where(kvp => kvp.Value.IsSystem);
			}
			else if (filterLocal && !filterBuiltin)
			{
				functionPairs = functionPairs.Where(kvp => !kvp.Value.IsSystem);
			}

			var functions = functionPairs
				.Select(kvp => kvp.Value.LibraryInformation.Attribute.Name)
				.Distinct()
				.OrderBy(x => x);

			foreach (var displayName in functions.Select(funcName => useLowercase ? funcName.ToLower() : funcName))
			{
				output.AppendLine($"  {displayName}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Allocations)
		{
			var isWizard = await executor.IsWizard();
			if (!isWizard)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var output = new System.Text.StringBuilder();
			output.AppendLine("Memory Allocations:");
			output.AppendLine($"  Total Memory: {GC.GetTotalMemory(false):N0} bytes");
			output.AppendLine($"  GC Gen 0 Collections: {GC.CollectionCount(0)}");
			output.AppendLine($"  GC Gen 1 Collections: {GC.CollectionCount(1)}");
			output.AppendLine($"  GC Gen 2 Collections: {GC.CollectionCount(2)}");

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		// Unreachable: every ListKind has a branch above, and a null kind returned early.
		throw new UnreachableException($"@list has no branch for {kind}.");
	}
}
