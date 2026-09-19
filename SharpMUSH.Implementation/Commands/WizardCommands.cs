using DotNext.Collections.Generic;
using Humanizer;
using SharpMUSH.Implementation.Common;
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
using System.Text.RegularExpressions;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@POOR", Switches = [], Behavior = CB.Default, MinArgs = 1, MaxArgs = 1, ParameterNames = ["player"])]
	public async ValueTask<Option<CallState>> Poor(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!await executor.IsWizard())
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
		}

		if (parser.CurrentState.Arguments.Count < 1)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PoorUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		if (!Configuration.CurrentValue.Limit.UseQuota)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaSystemDisabled), executor);
			return CallState.Empty;
		}

		var playerArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg) switch
		{
			AnySharpObject and SharpPlayer player => await PoorAsync(executor, player),
			AnySharpObject => throw new InvalidOperationException("A player lookup found something that is not a player."),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> PoorAsync(AnySharpObject executor, SharpPlayer player)
	{
		await Mediator.Send(new SetPlayerQuotaCommand(player, 0));

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PlayerSetToPoorFormat), executor, player.Object.Name);
		await NotifyService.NotifyLocalized(player.Object.DBRef, nameof(ErrorMessages.Notifications.YourQuotaSetToZeroByFormat), executor, executor.Object().Name);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@SQUOTA", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 1, ParameterNames = ["type", "value"])]
	public async ValueTask<Option<CallState>> ShortQuota(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (!Configuration.CurrentValue.Limit.UseQuota)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaSystemDisabledMessage), executor);
			return CallState.Empty;
		}

		AnySharpObject targetPlayer;
		if (args.Count > 0)
		{
			var playerArg = args["0"].Message!.ToPlainText();
			switch (await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg))
			{
				case AnySharpObject found:
					targetPlayer = found;
					break;
				case Error<CallState> error:
					return error.Value;
				default:
					throw new InvalidOperationException("A player lookup returned neither a player nor an error.");
			}
		}
		else
		{
			// PennMUSH src/wiz.c:165 resolves the no-argument form as Owner(player), not the executor
			// itself, so a thing or a puppet reports the quota of whoever owns it. Defaulting to the
			// executor instead made every non-player caller - `@force <thing>=@quota`, a $-command on a
			// thing - throw out of the command and take its queue entry with it.
			targetPlayer = new AnySharpObject(
				await executor.Object().Owner.WithCancellation(CancellationToken.None));
		}

		if (targetPlayer is not SharpPlayer targetPlayerObj)
		{
			throw new InvalidOperationException("A quota belongs to a player.");
		}

		if (!await PermissionService.CanSeeQuota(executor, targetPlayer))
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.CantLookAtOthersQuota,
				shouldNotify: true);
		}

		var quota = targetPlayerObj.Quota;

		var objectsOwned = await Mediator.Send(new GetOwnedObjectCountQuery(targetPlayerObj));

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaStatusFormat), executor, objectsOwned, quota);

		return CallState.Empty;
	}


	/// <summary>
	/// Who a wall broadcast reaches. PennMUSH's do_wall() (src/speech.c) picks a flag mask per
	/// command and hands it to flag_broadcast() (src/notify.c), which delivers only to connected
	/// players whose flags satisfy the mask.
	/// </summary>
	private enum WallAudience
	{
		/// <summary>No mask: every connected player. @wall.</summary>
		Everyone,

		/// <summary>Mask "WIZARD ROYALTY", which flaglist_check_long() reads as any-of. @rwall.</summary>
		RoyaltyAndWizards,

		/// <summary>Mask "WIZARD". @wizwall.</summary>
		Wizards
	}

	/// <summary>
	/// The one body behind @wall, @rwall and @wizwall — PennMUSH's do_wall(), which the three
	/// commands differ from each other only in the prefix and the flag mask they pass it.
	/// </summary>
	private async ValueTask<Option<CallState>> WallCore(IMUSHCodeParser parser, SharpCommandAttribute attribute,
		WallAudience audience, string prefix)
	{
		if (await RejectIfTooFewArguments(parser, attribute) is { } tooFewArguments) return tooFewArguments;

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var shout = switches.Contains("NOEVAL")
			? ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);

		if (!switches.Contains("EMIT"))
		{
			shout = MarkupText.Concat(MarkupText.Plain(prefix + " "), shout);
		}

		await foreach (var connection in ConnectionService.GetAll())
		{
			// PennMUSH iterates DESC_ITER_CONN, so a socket sitting at the connect screen hears nothing.
			if (connection.State is not IConnectionService.ConnectionState.LoggedIn || connection.Ref is null)
			{
				continue;
			}

			if (!await WallAudienceIncludes(audience, connection.Ref.Value))
			{
				continue;
			}

			await NotifyService.Notify(connection.Handle, shout, executor);
		}

		return new CallState(shout);
	}

	/// <summary>PennMUSH flag_broadcast()'s per-listener mask test.</summary>
	private async ValueTask<bool> WallAudienceIncludes(WallAudience audience, DBRef listener)
	{
		if (audience is WallAudience.Everyone)
		{
			return true;
		}

		if (await Mediator.Send(new GetObjectNodeQuery(listener)) is not AnySharpObject known)
		{
			return false;
		}

		return audience switch
		{
			WallAudience.Wizards => await known.IsWizard(),
			WallAudience.RoyaltyAndWizards => await known.IsWizard() || await known.IsRoyalty(),
			_ => true
		};
	}

	[SharpCommand(Name = "@RWALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default | CB.NoParse,
		CommandLock = "FLAG^WIZARD|FLAG^ROYALTY", MinArgs = 1, ParameterNames = ["message"])]
	public ValueTask<Option<CallState>> RoyaltyWall(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> WallCore(parser, _2, WallAudience.RoyaltyAndWizards, Configuration.CurrentValue.Cosmetic.RoyaltyWallPrefix);

	[SharpCommand(Name = "@WIZWALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default | CB.NoParse,
		CommandLock = "FLAG^WIZARD", MinArgs = 1, MaxArgs = 1, ParameterNames = ["message"])]
	public ValueTask<Option<CallState>> WizardWall(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> WallCore(parser, _2, WallAudience.Wizards, Configuration.CurrentValue.Cosmetic.WizardWallPrefix);

	[SharpCommand(Name = "@ALLQUOTA", Switches = ["QUIET"], Behavior = CB.Default,
		CommandLock = "FLAG^WIZARD|POWER^QUOTA", MinArgs = 1, MaxArgs = 1, ParameterNames = ["type"])]
	public async ValueTask<Option<CallState>> AllQuota(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var isQuiet = switches.Contains("QUIET");

		if (parser.CurrentState.Arguments.Count < 1)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AllQuotaUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var amountArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		if (!int.TryParse(amountArg, out var amount))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaAmountMustBeNumber), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		if (!Configuration.CurrentValue.Limit.UseQuota)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaSystemDisabled), executor);
			return CallState.Empty;
		}

		var players = Mediator.CreateStream(new GetAllPlayersQuery());
		var count = 0;

		await foreach (var player in players)
		{
			await Mediator.Send(new SetPlayerQuotaCommand(player, amount));
			count++;

			if (!isQuiet)
			{
				await NotifyService.NotifyLocalized(player.Object.DBRef, nameof(ErrorMessages.Notifications.AllQuotaSetForPlayerFormat), executor, amount, executor.Object().Name);
			}
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SetQuotaForPlayersFormat), executor, amount, count);

		return CallState.Empty;
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

	[SharpCommand(Name = "@MOTD", Switches = ["CONNECT", "LIST", "WIZARD", "DOWN", "FULL", "CLEAR"],
		Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["type", "message"])]
	public async ValueTask<Option<CallState>> MessageOfTheDay(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.ArgumentsOrdered;
		var argText = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty).ToPlainText();

		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>() ?? new MotdData();

		if (switches.Contains("LIST"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return CallState.Empty;
			}

			var output = new System.Text.StringBuilder();
			output.AppendLine("Message of the Day settings:");
			output.AppendLine($"Connect MOTD: {(string.IsNullOrEmpty(motdData.ConnectMotd) ? "(not set)" : motdData.ConnectMotd)}");
			output.AppendLine($"Wizard MOTD:  {(string.IsNullOrEmpty(motdData.WizardMotd) ? "(not set)" : motdData.WizardMotd)}");
			output.AppendLine($"Down MOTD:    {(string.IsNullOrEmpty(motdData.DownMotd) ? "(not set)" : motdData.DownMotd)}");
			output.AppendLine($"Full MOTD:    {(string.IsNullOrEmpty(motdData.FullMotd) ? "(not set)" : motdData.FullMotd)}");

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		string motdType;
		bool isConnect = !switches.Any() || switches.Contains("CONNECT");
		bool isWizard = switches.Contains("WIZARD");
		bool isDown = switches.Contains("DOWN");
		bool isFull = switches.Contains("FULL");

		if (isWizard)
			motdType = "wizard";
		else if (isDown)
			motdType = "down";
		else if (isFull)
			motdType = "full";
		else
			motdType = "connect";

		if (motdType == "connect")
		{
			if (!await executor.IsWizard() && !await executor.HasPower("ANNOUNCE"))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NeedAnnouncePower), executor);
				return CallState.Empty;
			}
		}
		else
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return CallState.Empty;
			}
		}

		if (switches.Contains("CLEAR"))
		{
			var newMotdData = motdType switch
			{
				"wizard" => motdData with { WizardMotd = null },
				"down" => motdData with { DownMotd = null },
				"full" => motdData with { FullMotd = null },
				_ => motdData with { ConnectMotd = null }
			};

			await ObjectDataService.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MotdClearedFormat), executor, motdType.Humanize(LetterCasing.Title));
			return CallState.Empty;
		}

		if (string.IsNullOrEmpty(argText))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MotdUsage), executor);
			return CallState.Empty;
		}

		var newMotdDataSet = motdType switch
		{
			"wizard" => motdData with { WizardMotd = argText },
			"down" => motdData with { DownMotd = argText },
			"full" => motdData with { FullMotd = argText },
			_ => motdData with { ConnectMotd = argText }
		};

		await ObjectDataService.SetExpandedServerDataAsync(newMotdDataSet, ignoreNull: true);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MotdSetFormat), executor, motdType.Humanize(LetterCasing.Title));
		return CallState.Empty;
	}

	[SharpCommand(Name = "@REJECTMOTD", Switches = ["CLEAR"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
		MinArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> RejectMessageOfTheDay(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		// Alias for @motd/full
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.ArgumentsOrdered;
		var argText = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty).ToPlainText();

		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>() ?? new MotdData();

		if (switches.Contains("CLEAR"))
		{
			var newMotdData = motdData with { FullMotd = null };
			await ObjectDataService.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FullMotdCleared), executor);
		}
		else if (string.IsNullOrEmpty(argText))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RejectMotdUsage), executor);
		}
		else
		{
			var newMotdData = motdData with { FullMotd = argText };
			await ObjectDataService.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FullMotdSet), executor);
		}

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

	[SharpCommand(Name = "@CHOWNALL", Switches = ["PRESERVE", "THINGS", "ROOMS", "EXITS"],
		Behavior = CB.Default | CB.EqSplit, CommandLock = "FLAG^WIZARD", MinArgs = 1, MaxArgs = 2, ParameterNames = ["old-owner", "new-owner"])]
	public async ValueTask<Option<CallState>> Chown(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.Arguments;
		var preserve = switches.Contains("PRESERVE");

		if (args.Count < 1)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ChownAllUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var playerArg = args["0"].Message!.ToPlainText();
		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg) switch
		{
			AnySharpObject and SharpPlayer oldOwner => await ChownAllFromAsync(parser, executor, oldOwner, switches, args,
				preserve),
			AnySharpObject => throw new InvalidOperationException("A player lookup found something that is not a player."),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> ChownAllFromAsync(IMUSHCodeParser parser, AnySharpObject executor,
		SharpPlayer oldOwner, IEnumerable<string> switches, Dictionary<string, CallState> args, bool preserve)
	{
		var newOwner = executor;
		if (args.Count > 1)
		{
			var newOwnerArg = args["1"].Message!.ToPlainText();
			switch (await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, newOwnerArg))
			{
				case AnySharpObject found:
					newOwner = found;
					break;
				case Error<CallState> error:
					return error.Value;
			}
		}

		var chownThings = switches.Contains("THINGS") || (!switches.Contains("ROOMS") && !switches.Contains("EXITS"));
		var chownRooms = switches.Contains("ROOMS") || (!switches.Contains("THINGS") && !switches.Contains("EXITS"));
		var chownExits = switches.Contains("EXITS") || (!switches.Contains("THINGS") && !switches.Contains("ROOMS"));

		var objects = Mediator.CreateStream(new GetAllTypedObjectsQuery());
		var count = 0;

		await foreach (var obj in objects)
		{
			var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);

			if (objOwner.Object.DBRef.Number != oldOwner.Object.DBRef.Number)
			{
				continue;
			}

			// obj is already AnySharpObject — no secondary GetObjectNodeQuery needed

			var shouldChown = (chownThings && obj.IsThing) ||
												(chownRooms && obj.IsRoom) ||
												(chownExits && obj.IsExit);

			if (!shouldChown)
			{
				continue;
			}

			if (newOwner is not SharpPlayer newOwnerPlayer)
			{
				throw new InvalidOperationException("Objects are owned by players.");
			}

			await Mediator.Send(new SetObjectOwnerCommand(obj, newOwnerPlayer));
			count++;

			if (!preserve && !obj.IsPlayer)
			{
				if (await obj.HasFlag("WIZARD"))
				{
					await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!WIZARD", false);
				}
				if (await obj.HasFlag("ROYALTY"))
				{
					await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!ROYALTY", false);
				}
				if (await obj.HasFlag("TRUST"))
				{
					await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!TRUST", false);
				}
				await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "HALT", false);

				await ManipulateSharpObjectService.ClearAllPowers(executor, obj, false);
			}
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ChownAllCompleteFormat), executor, count, oldOwner.Object.Name, newOwner.Object().Name);

		return CallState.Empty;
	}

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

	[SharpCommand(Name = "@QUOTA", Switches = ["ALL", "SET"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0,
		MaxArgs = 2, ParameterNames = ["player", "quota"])]
	public async ValueTask<Option<CallState>> Quota(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.Arguments;

		if (!Configuration.CurrentValue.Limit.UseQuota)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaSystemDisabled), executor);
			return CallState.Empty;
		}

		if (switches.Contains("SET"))
		{
			if (!await executor.IsWizard())
			{
				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.PermissionDenied,
					shouldNotify: true);
			}

			if (args.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaSetUsage), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}

			var playerArg = args["0"].Message!.ToPlainText();
			var amountArg = args["1"].Message!.ToPlainText();

			if (!int.TryParse(amountArg, out var amount))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaAmountMustBeNumber), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}

			return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor,
				playerArg, async player =>
				{
					await Mediator.Send(new SetPlayerQuotaCommand(player, amount));

					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaForPlayerSetFormat), executor, player.Object.Name, amount);
					await NotifyService.NotifyLocalized(player.Object.DBRef, nameof(ErrorMessages.Notifications.YourQuotaSetToByFormat), executor, amount, executor.Object().Name);

					return CallState.Empty;
				});
		}

		if (switches.Contains("ALL"))
		{
			if (!await executor.IsWizard())
			{
				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.PermissionDenied,
					shouldNotify: true);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaListingHeader), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaListingColumnHeader), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaListingSeparator), executor);

			var players = Mediator.CreateStream(new GetAllPlayersQuery());
			await foreach (var player in players)
			{
				var objectCount = await Mediator.Send(new GetOwnedObjectCountQuery(player));
				var playerName = player.Object.Name.PadToColumns(27);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaPlayerRowFormat), executor, playerName, objectCount, player.Quota);
			}

			return CallState.Empty;
		}

		AnySharpObject targetPlayer;
		if (args.Count > 0)
		{
			var playerArg = args["0"].Message!.ToPlainText();
			switch (await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg))
			{
				case AnySharpObject found:
					targetPlayer = found;
					break;
				case Error<CallState> error:
					return error.Value;
				default:
					throw new InvalidOperationException("A player lookup returned neither a player nor an error.");
			}
		}
		else
		{
			// PennMUSH src/wiz.c:165 resolves the no-argument form as Owner(player), not the executor
			// itself, so a thing or a puppet reports the quota of whoever owns it. Defaulting to the
			// executor instead made every non-player caller - `@force <thing>=@quota`, a $-command on a
			// thing - throw out of the command and take its queue entry with it.
			targetPlayer = new AnySharpObject(
				await executor.Object().Owner.WithCancellation(CancellationToken.None));
		}

		if (targetPlayer is not SharpPlayer targetPlayerObj)
		{
			throw new InvalidOperationException("A quota belongs to a player.");
		}

		if (!await PermissionService.CanSeeQuota(executor, targetPlayer))
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.CantLookAtOthersQuota,
				shouldNotify: true);
		}

		var quota = targetPlayerObj.Quota;

		var objectsOwned = await Mediator.Send(new GetOwnedObjectCountQuery(targetPlayerObj));

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaPlayerObjectsFormat), executor, targetPlayerObj.Object.Name, objectsOwned, quota);

		return CallState.Empty;
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

	[SharpCommand(Name = "@WALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default | CB.NoParse,
		CommandLock = "FLAG^WIZARD|FLAG^ROYALTY|POWER^ANNOUNCE", MinArgs = 1, ParameterNames = ["message"])]
	public ValueTask<Option<CallState>> Wall(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> WallCore(parser, _2, WallAudience.Everyone, Configuration.CurrentValue.Cosmetic.WallPrefix);

	[SharpCommand(Name = "@CHZONEALL", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit, CommandLock = "FLAG^WIZARD",
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["old-zone", "new-zone"])]
	public async ValueTask<Option<CallState>> ChangeZoneAll(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var playerName = args["0"].Message!.ToPlainText();
		var zoneName = args["1"].Message!.ToPlainText();
		var preserve = parser.CurrentState.Switches.Contains("PRESERVE");

		// A player by name, so MAT_PMATCH: MAT_EVERYTHING carries MAT_PLAYER, which only answers to a
		// leading '*'.
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, playerName,
			LocateFlags.All | LocateFlags.MatchOptionalWildCardForPlayerName,
			async player =>
			{
				if (!player.IsPlayer)
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.InvalidPlayer,
						notifyMessage: ErrorMessages.Notifications.MustBePlayer,
						shouldNotify: true);
				}

				if (zoneName.Equals("none", StringComparison.InvariantCultureIgnoreCase))
				{
					var allObjects = Mediator.CreateStream(new GetAllTypedObjectsQuery())!;
					var count = 0;

					await foreach (var obj in allObjects)
					{
						var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
						if (objOwner.Object.DBRef.Number == player.Object().DBRef.Number)
						{
							// obj is already AnySharpObject — no secondary GetObjectNodeQuery needed
							await Mediator.Send(new UnsetObjectZoneCommand(obj));
							count++;
						}
					}

					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ZonesClearedForOwnerFormat), executor, count, player.Object().Name);
					return CallState.Empty;
				}

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, zoneName, LocateFlags.All,
					async zoneObj =>
					{
						var allObjects = Mediator.CreateStream(new GetAllTypedObjectsQuery())!;
						var count = 0;

						await foreach (var obj in allObjects)
						{
							var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
							if (objOwner.Object.DBRef.Number == player.Object().DBRef.Number)
							{
								// obj is already AnySharpObject — no secondary GetObjectNodeQuery needed

								if (!await HelperFunctions.SafeToAddZone(Mediator, Database, obj, zoneObj))
								{
									continue;
								}

								await Mediator.Send(new SetObjectZoneCommand(obj, zoneObj));

								if (!preserve && !obj.IsPlayer)
								{
									if (await obj.HasFlag("WIZARD"))
									{
										await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!WIZARD", false);
									}
									if (await obj.HasFlag("ROYALTY"))
									{
										await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!ROYALTY", false);
									}
									if (await obj.HasFlag("TRUST"))
									{
										await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!TRUST", false);
									}

									await ManipulateSharpObjectService.ClearAllPowers(executor, obj, false);
								}

								count++;
							}
						}

						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ZoneSetForOwnerFormat), executor, zoneObj.Object().Name, count, player.Object().Name);
						return CallState.Empty;
					}
				);
			}
		);
	}

	[SharpCommand(Name = "@POLL", Switches = ["CLEAR"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Poll(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.ArgumentsOrdered;

		var pollData = await ObjectDataService.GetExpandedServerDataAsync<PollData>() ?? new PollData();

		if (switches.Contains("CLEAR"))
		{
			if (!await executor.IsWizard() && !await executor.HasPower("POLL"))
			{
				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.PermissionDenied,
					shouldNotify: true);
			}

			var newPollData = pollData with { Message = null };
			await ObjectDataService.SetExpandedServerDataAsync(newPollData, ignoreNull: true);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PollMessageCleared), executor);
			return CallState.Empty;
		}

		if (args.Count == 0)
		{
			if (string.IsNullOrEmpty(pollData.Message))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PollNoPollMessage), executor);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PollCurrentMessageFormat), executor, pollData.Message);
			}
			return CallState.Empty;
		}

		if (!await executor.IsWizard() && !await executor.HasPower("POLL"))
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
		}

		var argText = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty).ToPlainText();
		var newData = pollData with { Message = argText };
		await ObjectDataService.SetExpandedServerDataAsync(newData, ignoreNull: true);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PollMessageSet), executor);
		return CallState.Empty;
	}

	[SharpCommand(Name = "@WIZMOTD", Switches = ["CLEAR"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
		MinArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> WizardMessageOfTheDay(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		// Alias for @motd/wizard
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.ArgumentsOrdered;
		var argText = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty).ToPlainText();

		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>() ?? new MotdData();

		if (switches.Contains("CLEAR"))
		{
			var newMotdData = motdData with { WizardMotd = null };
			await ObjectDataService.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WizMotdCleared), executor);
		}
		else if (string.IsNullOrEmpty(argText))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WizMotdUsage), executor);
		}
		else
		{
			var newMotdData = motdData with { WizardMotd = argText };
			await ObjectDataService.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WizMotdSet), executor);
		}

		return CallState.Empty;
	}

}
