using DotNext.Collections.Generic;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using System.Collections.Immutable;
using System.Buffers;
using Microsoft.Extensions.Logging;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <remarks>
	/// Creating on the DBRef is not implemented.
	/// </remarks>
	[SharpCommand(Name = "@PCREATE", Behavior = CB.Default | CB.EqSplit, CommandLock = "FLAG^WIZARD",
		MinArgs = 2, MaxArgs = 3, ParameterNames = ["name", "password"])]
	public async ValueTask<Option<CallState>> PlayerCreate(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var defaultHome = Configuration.CurrentValue.Database.DefaultHome;
		var defaultHomeDbref = new DBRef((int)defaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;
		var args = parser.CurrentState.Arguments;
		var name = args["0"].Message!.ToPlainText();
		var password = args["1"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// Note: We use ValidationType.Name instead of PlayerName because PlayerName requires
		// an existing AnySharpObject target (for rename operations), which we don't have yet
		if (!await ValidateService.Valid(IValidateService.ValidationType.Name, MarkupText.Plain(name), new None()))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PlayerCreateInvalidName), executor);
			return CallState.Empty;
		}

		// This is necessary because ValidationType.Name only checks format, not uniqueness
		if (await Mediator.CreateStream(new GetPlayerQuery(name)).AnyAsync())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PlayerNameAlreadyExists), executor);
			return CallState.Empty;
		}

		if (!await ValidateService.Valid(IValidateService.ValidationType.Password, MarkupText.Plain(password), new None()))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PlayerCreateInvalidPassword), executor);
			return CallState.Empty;
		}

		var player = await Mediator.Send(new CreatePlayerCommand(name, password, defaultHomeDbref, defaultHomeDbref, startingQuota));

		// PennMUSH src/wiz.c do_pcreate ends with exactly this notify — including the password, which the
		// wizard has to be able to pass on to the new player and just typed anyway.
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PlayerCreatedFormat), executor,
			name, player.Number, password);

		// PennMUSH spec: player`create (objid, name, how, descriptor, email)
		await EventService.TriggerEventAsync(
			parser,
			"PLAYER`CREATE",
			executor.Object().DBRef, // Enactor is the wizard who did @pcreate
			player.ToString(),
			name,
			"pcreate",
			"", // descriptor (not applicable for @pcreate)
			""); // email (not applicable for @pcreate)

		return new CallState(player.ToString());
	}

	[SharpCommand(Name = "@NEWPASSWORD", Switches = ["GENERATE"], Behavior = CB.Default | CB.EqSplit | CB.RSNoParse,
		CommandLock = "FLAG^WIZARD", MinArgs = 1, ParameterNames = ["player", "password"])]
	public async ValueTask<Option<CallState>> NewPassword(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var arg0 = args["0"].Message!.ToPlainText();
		var isGenerate = parser.CurrentState.Switches.Contains("GENERATE");

		if (isGenerate && parser.CurrentState.Arguments.Count > 1)
		{
			await NotifyService.NotifyLocalized(executor.Object().DBRef, nameof(ErrorMessages.Notifications.NewPasswordGenerateSwitchConflict), executor);
		}

		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0) switch
		{
			AnySharpObject and SharpPlayer asPlayer => await NewPasswordAsync(executor, asPlayer, args, isGenerate),
			AnySharpObject => throw new InvalidOperationException("A player lookup found something that is not a player."),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> NewPasswordAsync(AnySharpObject executor, SharpPlayer asPlayer,
		Dictionary<string, CallState> args, bool isGenerate)
	{
		if (isGenerate)
		{
			var generatedPassword = PasswordService.GenerateRandomPassword();

			await Mediator.Send(
				new SetPlayerPasswordCommand(asPlayer,
					PasswordService.HashPassword(asPlayer.Object.DBRef.ToString(), generatedPassword)));

			await NotifyService.NotifyLocalized(executor.Object().DBRef, nameof(ErrorMessages.Notifications.NewPasswordGeneratedFormat), executor, asPlayer.Object.Name, generatedPassword);

			return new CallState(generatedPassword);
		}

		if (!args.TryGetValue("1", out var arg1CallState))
		{
			await NotifyService.Notify(executor, "Usage: @newpassword <player>=<password>", executor);
			return new CallState(string.Format(ErrorMessages.Returns.TooFewCommandArguments, "@NEWPASSWORD", 2, 1));
		}

		var arg1 = arg1CallState.Message!.ToPlainText();
		var newHashedPassword = PasswordService.HashPassword(asPlayer.Object.DBRef.ToString(), arg1);

		await Mediator.Send(new SetPlayerPasswordCommand(asPlayer, newHashedPassword));

		await NotifyService.NotifyLocalized(executor.Object().DBRef, nameof(ErrorMessages.Notifications.NewPasswordSetFormat), executor, asPlayer.Object.Name, arg1);

		return new CallState(arg1);
	}

	/// <summary>
	/// Manages sitelock rules that control which hosts can connect, create players, or use guests.
	/// @sitelock - Lists all rules and banned names
	/// @sitelock/check &lt;host&gt; - Checks which rule matches a host
	/// @sitelock/name &lt;name&gt; - Manages banned player names (not yet implemented)
	/// @sitelock/ban &lt;pattern&gt; - Bans a host pattern (not yet implemented)
	/// @sitelock/register &lt;pattern&gt; - Sets registration requirement (not yet implemented)
	/// @sitelock/remove &lt;pattern&gt; - Removes a rule (not yet implemented)
	/// </summary>
	[SharpCommand(Name = "@SITELOCK", Switches = ["BAN", "CHECK", "REGISTER", "REMOVE", "NAME", "PLAYER", "LIST"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs, CommandLock = "FLAG^WIZARD", MinArgs = 0, ParameterNames = ["site", "rule"])]
	public async ValueTask<Option<CallState>> SiteLock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		var sitelockRules = Configuration.CurrentValue.SitelockRules;
		var bannedNames = Configuration.CurrentValue.BannedNames;

		if (args.Count == 0 || switches.Contains("LIST"))
		{
			var output = new System.Text.StringBuilder();
			output.AppendLine($"Sitelock Rules ({sitelockRules.Rules.Count} total):");
			output.AppendLine("Pattern                      Options");
			output.AppendLine("---------------------------- ------------------------------");

			if (sitelockRules.Rules.Count == 0)
			{
				output.AppendLine("  (No rules defined - all connections allowed by default)");
			}
			else
			{
				foreach (var rule in sitelockRules.Rules)
				{
					var pattern = rule.Key;
					var options = string.Join(", ", rule.Value);
					output.AppendLine($"{pattern,-28} {options}");
				}
			}

			output.AppendLine();
			output.AppendLine($"Banned Player Names ({bannedNames.BannedNames.Length} total):");
			if (bannedNames.BannedNames.Length == 0)
			{
				output.AppendLine("  (No banned names defined)");
			}
			else
			{
				output.AppendLine("  " + string.Join(", ", bannedNames.BannedNames));
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (switches.Contains("CHECK"))
		{
			if (args.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SitelockCheckRequiresHost), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}

			var hostToCheck = args["0"].Message!.ToPlainText();

			KeyValuePair<string, string[]>? matchingRule = sitelockRules.Rules
				.FirstOrDefault(rule => SitelockMatcher.Matches(rule.Key, hostToCheck, hostToCheck));

			if (matchingRule.HasValue)
			{
				var options = string.Join(", ", matchingRule.Value.Value);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SitelockHostMatchesFormat), executor, hostToCheck, matchingRule.Value.Key, options);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SitelockHostNoMatchFormat), executor, hostToCheck);
			}

			return CallState.Empty;
		}

		if (switches.Contains("NAME"))
		{
			if (args.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SitelockNameRequiresName), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}

			// Note: Actual modification of configuration is not yet implemented
			// This would require saving to the database
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SitelockNameNotImplemented), executor);
			return new CallState(ErrorMessages.Returns.NotImplemented);
		}

		// @sitelock/ban <pattern> - shorthand for !connect !create !guest
		if (switches.Contains("BAN"))
		{
			if (args.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SitelockBanRequiresPattern), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}

			var banPattern = args["0"].Message!.ToPlainText();
			string[] banFlags = ["!connect", "!create", "!guest"];
			await AddSitelockRuleAsync(banPattern, banFlags);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SitelockRuleAddedFormat), executor, banPattern, string.Join(" ", banFlags));
			return CallState.Empty;
		}

		// @sitelock/register <pattern> - shorthand for !create register
		if (switches.Contains("REGISTER"))
		{
			if (args.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SitelockRegisterRequiresPattern), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}

			var registerPattern = args["0"].Message!.ToPlainText();
			string[] registerFlags = ["!create", "register"];
			await AddSitelockRuleAsync(registerPattern, registerFlags);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SitelockRuleAddedFormat), executor, registerPattern, string.Join(" ", registerFlags));
			return CallState.Empty;
		}

		if (switches.Contains("REMOVE"))
		{
			if (args.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SitelockRemoveRequiresPattern), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}

			var removePattern = args["0"].Message!.ToPlainText();
			var removed = await RemoveSitelockRuleAsync(removePattern);
			if (!removed)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SitelockRuleNotFound), executor);
				return new CallState(ErrorMessages.Returns.NoMatch);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SitelockRuleRemovedFormat), executor, removePattern);
			return CallState.Empty;
		}

		if (args.Count == 2)
		{
			var rulePattern = args["0"].Message!.ToPlainText();
			var ruleFlags = args["1"].Message!.ToPlainText()
				.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			await AddSitelockRuleAsync(rulePattern, ruleFlags);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SitelockRuleAddedFormat), executor, rulePattern, string.Join(" ", ruleFlags));
			return CallState.Empty;
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SitelockInvalidSyntax), executor);
		return new CallState(ErrorMessages.Returns.InvalidArguments);
	}

	/// <summary>
	/// The freshest known <see cref="SharpMUSHOptions"/>: whatever is actually persisted in the
	/// database, falling back to <see cref="Configuration"/>'s in-memory snapshot only if nothing has
	/// been persisted yet. Read-modify-write mutations (<see cref="AddSitelockRuleAsync"/>,
	/// <see cref="RemoveSitelockRuleAsync"/>) must base their merge on this rather than on
	/// <c>Configuration.CurrentValue</c> alone: <c>IOptionsWrapper&lt;SharpMUSHOptions&gt;</c>
	/// re-reads lazily off the reload change-token, and a rule persisted moments earlier by a
	/// *different* mutation (e.g. via the web admin UI, or a prior command in the same turn) could
	/// otherwise be silently dropped by an overwrite based on a stale in-memory copy.
	/// </summary>
	private async ValueTask<SharpMUSHOptions> CurrentPersistedOptionsAsync()
		=> await ObjectDataService.GetExpandedServerDataAsync<SharpMUSHOptions>()
			?? Configuration.CurrentValue;

	/// <summary>
	/// Adds or replaces the sitelock rule for <paramref name="pattern"/> with <paramref name="flags"/>,
	/// persists it via <see cref="IExpandedObjectDataService.SetExpandedServerDataAsync{T}"/>, signals a reload via
	/// <see cref="ConfigurationReloadService.SignalChange"/>, and immediately enforces it via
	/// <see cref="IBanEnforcer.EnforceHostRuleAsync"/> so live connections matching the new rule are
	/// dropped right away. Mirrors <c>SitelockController.AddSitelockRule</c> (SharpMUSH.Server).
	/// </summary>
	private async ValueTask AddSitelockRuleAsync(string pattern, string[] flags)
	{
		var currentOptions = await CurrentPersistedOptionsAsync();
		var newRules = new Dictionary<string, string[]>(currentOptions.SitelockRules.Rules)
		{
			[pattern] = flags
		};

		var updatedOptions = currentOptions with
		{
			SitelockRules = new SitelockRulesOptions(newRules)
		};

		await ObjectDataService.SetExpandedServerDataAsync(updatedOptions);
		ConfigReloadService.SignalChange();
		await BanEnforcer.EnforceHostRuleAsync(pattern);
	}

	/// <summary>
	/// Removes the sitelock rule for <paramref name="pattern"/>, persists via
	/// <see cref="IExpandedObjectDataService.SetExpandedServerDataAsync{T}"/>, and signals a reload via
	/// <see cref="ConfigurationReloadService.SignalChange"/>. Mirrors
	/// <c>SitelockController.DeleteSitelockRule</c> (SharpMUSH.Server). Returns <see langword="false"/>
	/// without persisting anything when no rule for <paramref name="pattern"/> exists.
	/// </summary>
	private async ValueTask<bool> RemoveSitelockRuleAsync(string pattern)
	{
		var currentOptions = await CurrentPersistedOptionsAsync();
		var newRules = new Dictionary<string, string[]>(currentOptions.SitelockRules.Rules);

		if (!newRules.Remove(pattern))
		{
			return false;
		}

		var updatedOptions = currentOptions with
		{
			SitelockRules = new SitelockRulesOptions(newRules)
		};

		await ObjectDataService.SetExpandedServerDataAsync(updatedOptions);
		ConfigReloadService.SignalChange();
		return true;
	}

	/// <summary>
	/// PennMUSH <c>@hide</c> (<c>hide_player</c>, <c>bsd.c:7161-7251</c>): a permission-gated,
	/// per-CONNECTION toggle — unrelated to the <c>DARK</c> object flag. With no target (the only
	/// form SharpMUSH implements; Penn's numeric-descriptor and named-player-target forms are out of
	/// scope), it acts on every one of the executor's own currently-open connections. A bare
	/// <c>@hide</c> with no switch reproduces Penn's <c>status == 2</c> aggregate toggle
	/// (<c>bsd.c:7224-7232</c>): hide all connections if any of them is currently visible, otherwise
	/// unhide all of them (i.e. only flip to "all unhidden" once every connection was already
	/// hidden). The notify text mirrors Penn's self-target branch (<c>bsd.c:7239,7246</c>) — not its
	/// numeric-descriptor branch's "Connection hidden."/"Connection unhidden." (<c>bsd.c:7205,7207</c>),
	/// which SharpMUSH doesn't implement here.
	/// </summary>
	[SharpCommand(Name = "@HIDE", Switches = ["NO", "OFF", "YES", "ON"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = ["on-off"])]
	public async ValueTask<Option<CallState>> Hide(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;

		if (!await executor.CanHide())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return CallState.Empty;
		}

		var playerRef = executor.Object().DBRef;
		var connections = await ConnectionService.Get(playerRef).ToListAsync();

		bool shouldBeHidden;
		if (switches.Contains("YES") || switches.Contains("ON"))
		{
			shouldBeHidden = true;
		}
		else if (switches.Contains("NO") || switches.Contains("OFF"))
		{
			shouldBeHidden = false;
		}
		else
		{
			// No switch = aggregate toggle: hide all connections unless every one of them is
			// already hidden, in which case unhide all of them (bsd.c:7224-7232).
			var allHidden = connections.Count != 0 && connections.All(c => c.IsHidden);
			shouldBeHidden = !allHidden;
		}

		foreach (var connection in connections)
		{
			ConnectionService.Update(connection.Handle, "Hidden", shouldBeHidden ? "1" : "0");
		}

		await NotifyService.NotifyLocalized(executor,
			shouldBeHidden
				? nameof(ErrorMessages.Notifications.NoLongerAppearOnWho)
				: nameof(ErrorMessages.Notifications.NowAppearOnWho),
			executor);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@SUGGEST", Switches = ["ADD", "DELETE", "LIST"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["text"])]
	public async ValueTask<Option<CallState>> Suggest(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.Arguments;

		var suggestionData = await ObjectDataService.GetExpandedServerDataAsync<SuggestionData>()
			?? new SuggestionData();

		if (suggestionData.Categories == null)
		{
			suggestionData = suggestionData with { Categories = new Dictionary<string, HashSet<string>>() };
		}

		if (args.Count == 0 || switches.Contains("LIST"))
		{
			if (suggestionData.Categories.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoSuggestionCategoriesDefined), executor);
			}
			else
			{
				var output = new System.Text.StringBuilder();
				output.AppendLine("Suggestion categories:");
				foreach (var (category, words) in suggestionData.Categories.OrderBy(kvp => kvp.Key))
				{
					output.AppendLine($"  {category}: {words.Count} words");
				}
				await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			}
			return CallState.Empty;
		}

		if (switches.Contains("ADD"))
		{
			if (args.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SuggestAddUsage), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}

			var category = args["0"].Message!.ToPlainText().ToLower();
			var word = args["1"].Message!.ToPlainText().ToLower();

			if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(word))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SuggestCategoryAndWordCannotBeEmpty), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}

			if (!suggestionData.Categories.ContainsKey(category))
			{
				suggestionData.Categories[category] = new HashSet<string>();
			}

			if (suggestionData.Categories[category].Add(word))
			{
				await ObjectDataService.SetExpandedServerDataAsync(suggestionData, ignoreNull: true);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SuggestAddedWordToCategoryFormat), executor, word, category);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SuggestWordAlreadyExistsFormat), executor, word, category);
			}

			return CallState.Empty;
		}

		if (switches.Contains("DELETE"))
		{
			if (args.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SuggestDeleteUsage), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}

			var category = args["0"].Message!.ToPlainText().ToLower();
			var word = args["1"].Message!.ToPlainText().ToLower();

			if (!suggestionData.Categories.ContainsKey(category))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SuggestCategoryDoesNotExistFormat), executor, category);
				return CallState.Empty;
			}

			if (suggestionData.Categories[category].Remove(word))
			{
				if (suggestionData.Categories[category].Count == 0)
				{
					suggestionData.Categories.Remove(category);
				}

				await ObjectDataService.SetExpandedServerDataAsync(suggestionData, ignoreNull: true);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SuggestRemovedWordFromCategoryFormat), executor, word, category);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SuggestWordNotFoundInCategoryFormat), executor, word, category);
			}

			return CallState.Empty;
		}

		if (args.Count == 1)
		{
			var category = args["0"].Message!.ToPlainText().ToLower();

			if (!suggestionData.Categories.ContainsKey(category))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SuggestCategoryDoesNotExistFormat), executor, category);
				return CallState.Empty;
			}

			var words = suggestionData.Categories[category].OrderBy(w => w).ToList();
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SuggestCategoryWordCountFormat), executor, category, words.Count);
			await NotifyService.Notify(executor, string.Join(", ", words), executor);

			return CallState.Empty;
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SuggestUsage), executor);
		return CallState.Empty;
	}

	[SharpCommand(Name = "@PASSWORD", Switches = [],
		Behavior = CB.Player | CB.EqSplit | CB.NoParse | CB.RSNoParse | CB.NoGuest, MinArgs = 2, MaxArgs = 0, ParameterNames = ["old", "new"])]
	public async ValueTask<Option<CallState>> Password(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var oldPassword = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var newPassword = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		if (executor is not SharpPlayer player)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PasswordOnlyPlayersHavePasswords), executor);
			return new CallState(ErrorMessages.Returns.InvalidObjectType);
		}

		var isValidPassword = PasswordService.PasswordIsValid(executor.Object().DBRef.ToString(), oldPassword,
			player.PasswordHash);
		if (!isValidPassword)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PasswordInvalid), executor);
			return new CallState(ErrorMessages.Returns.InvalidPassword);
		}

		var hashedPassword = PasswordService.HashPassword(executor.Object().DBRef.ToString(), newPassword);
		await PasswordService.SetPassword(player, hashedPassword);

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@WARNINGS", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Warnings(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (!args.TryGetValue("0", out var objectArg) || string.IsNullOrWhiteSpace(objectArg.Message?.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Usage: @warnings <object>=<warning list>", executor);
			await NotifyService.Notify(executor, "Available warnings: none, serious, normal, extra, all", executor);
			await NotifyService.Notify(executor, "Individual: exit-unlinked, exit-oneway, exit-multiple, exit-msgs, exit-desc,", executor);
			await NotifyService.Notify(executor, "           thing-msgs, thing-desc, room-desc, my-desc, lock-checks", executor);
			await NotifyService.Notify(executor, "Use !warning to negate (e.g., 'all !exit-desc')", executor);
			return CallState.Empty;
		}

		if (!args.TryGetValue("1", out var warningListArg))
		{
			await NotifyService.Notify(executor, "Usage: @warnings <object>=<warning list>", executor);
			return CallState.Empty;
		}

		var objectString = objectArg.Message?.ToString() ?? string.Empty;
		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objectString,
			LocateFlags.All) switch
		{
			AnySharpObject target => await SetWarningsAsync(executor, target, warningListArg),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> SetWarningsAsync(AnySharpObject executor, AnySharpObject target,
		CallState warningListArg)
	{
		var targetObj = target.Object();

		if (!await PermissionService.Controls(executor, target))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var warningListString = warningListArg.Message?.ToPlainText() ?? string.Empty;
		var unknownWarnings = new List<string>();
		var newWarnings = WarningTypeHelper.ParseWarnings(warningListString, unknownWarnings);

		foreach (var unknown in unknownWarnings)
		{
			await NotifyService.Notify(executor, $"Unknown warning: {unknown}", executor);
		}

		var oldWarnings = targetObj.Warnings;

		await Mediator.Send(new SetObjectWarningsCommand(target, newWarnings));

		if (newWarnings != WarningType.None)
		{
			var warningString = WarningTypeHelper.UnparseWarnings(newWarnings);
			await NotifyService.Notify(executor, $"Warnings set to: {warningString}", executor);
		}
		else
		{
			await NotifyService.Notify(executor, "Warnings cleared.", executor);
		}

		Logger?.LogInformation("@WARNINGS: {Executor} set warnings on {Target} from {Old} to {New}",
			executor.Object().Name, targetObj.Name, oldWarnings, newWarnings);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@WCHECK", Switches = ["ALL", "ME"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> WizardCheck(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		var checkAll = switches.Contains("ALL");
		var checkMe = switches.Contains("ME");

		if (checkAll)
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.Notify(executor, "You'd better check your wizbit first.", executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			await NotifyService.Notify(executor, "Running database topology warning checks...", executor);
			var checkedCount = await WarningService.CheckAllObjectsAsync();
			await NotifyService.Notify(executor, $"Warning checks complete. Checked {checkedCount} objects.", executor);

			Logger?.LogInformation("@WCHECK/ALL executed by {Executor}, checked {Count} objects",
				executor.Object().Name, checkedCount);
		}
		else if (checkMe)
		{
			await NotifyService.Notify(executor, "Checking objects you own...", executor);
			var warningCount = await WarningService.CheckOwnedObjectsAsync(executor);

			Logger?.LogInformation("@WCHECK/ME executed by {Executor}, found {Count} warnings",
				executor.Object().Name, warningCount);
		}
		else
		{
			if (!args.TryGetValue("0", out var objectArg) || string.IsNullOrWhiteSpace(objectArg.Message?.ToPlainText()))
			{
				await NotifyService.Notify(executor, "Usage: @wcheck <object> or @wcheck/me or @wcheck/all", executor);
				return CallState.Empty;
			}

			var objectString = objectArg.Message?.ToString() ?? string.Empty;
			return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objectString,
				LocateFlags.All) switch
			{
				AnySharpObject target => await WarningCheckObjectAsync(executor, target),
				Error<CallState> error => error.Value
			};
		}

		return CallState.Empty;
	}

	/// <summary>Runs the warning checks on one object its owner, or a See_All viewer, names.</summary>
	private async ValueTask<Option<CallState>> WarningCheckObjectAsync(AnySharpObject executor, AnySharpObject target)
	{
		var targetObj = target.Object();
		var targetOwner = await targetObj.Owner.WithCancellation(CancellationToken.None);

		if (!(await executor.IsSee_All() || targetOwner.Object.DBRef.Equals(executor.Object().DBRef)))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		await WarningService.CheckObjectAsync(executor, target);
		await NotifyService.Notify(executor, "@wcheck complete.", executor);

		Logger?.LogInformation("@WCHECK executed by {Executor} on {Target}",
			executor.Object().Name, targetObj.Name);

		return CallState.Empty;
	}
}
