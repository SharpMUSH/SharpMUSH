using Humanizer;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using System.Collections.Immutable;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
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

	[SharpCommand(Name = "@LISTMOTD", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> ListMessageOfTheDay(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var isWizard = await executor.IsWizard();

		var motdFile = Configuration.CurrentValue.Message.MessageOfTheDayFile;
		var motdHtmlFile = Configuration.CurrentValue.Message.MessageOfTheDayHtmlFile;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdCurrentSettingsHeader), executor);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdConnectFileFormat), executor, motdFile ?? "(not set)");
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdConnectHtmlFormat), executor, motdHtmlFile ?? "(not set)");

		if (isWizard)
		{
			var wizmotdFile = Configuration.CurrentValue.Message.WizMessageOfTheDayFile;
			var wizmotdHtmlFile = Configuration.CurrentValue.Message.WizMessageOfTheDayHtmlFile;

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdWizardFileFormat), executor, wizmotdFile ?? "(not set)");
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdWizardHtmlFormat), executor, wizmotdHtmlFile ?? "(not set)");
		}

		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>();
		if (motdData != null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EmptyLine), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdTemporaryHeader), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdConnectMotdFormat), executor, string.IsNullOrEmpty(motdData.ConnectMotd) ? "(not set)" : motdData.ConnectMotd);

			if (isWizard)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdWizardMotdFormat), executor, string.IsNullOrEmpty(motdData.WizardMotd) ? "(not set)" : motdData.WizardMotd);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdDownMotdFormat), executor, string.IsNullOrEmpty(motdData.DownMotd) ? "(not set)" : motdData.DownMotd);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdFullMotdFormat), executor, string.IsNullOrEmpty(motdData.FullMotd) ? "(not set)" : motdData.FullMotd);
			}
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "DOING", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> Doing(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var isAdmin = await executor.IsWizard() ||
									await executor.IsRoyalty() ||
									await executor.IsSee_All();

		var pattern = args.ContainsKey("0") ? args["0"].Message?.ToPlainText() : null;

		var everyone = ConnectionService.GetAll();
		const string fmt = "{0,-18} {1,10} {2,6}  {3,-32}";
		var header = string.Format(fmt, "Player Name", "On For", "Idle", "Doing");

		var playerList = new List<string>();
		await foreach (var connection in everyone.Where(player => player.Ref.HasValue))
		{
			if (!isAdmin && connection.PresenceClass == PresenceClasses.Portal)
			{
				continue;
			}

			// Like WHO, a descriptor whose player is gone is left out rather than failing the listing.
			if (await Mediator.Send(new GetObjectNodeQuery(connection.Ref!.Value)) is not AnySharpObject obj)
			{
				continue;
			}

			var playerName = obj.Object().Name;

			if (!isAdmin && await obj.HasFlag("DARK"))
			{
				continue;
			}

			if (!string.IsNullOrWhiteSpace(pattern) && !MatchesPattern(playerName, pattern))
			{
				continue;
			}

			var doingText = await GetDoingText(executor, obj);

			playerList.Add(string.Format(
				fmt,
				playerName,
				TimeHelpers.TimeString(connection.Connected!.Value, accuracy: 3),
				TimeHelpers.TimeString(connection.Idle!.Value),
				doingText));
		}

		var footer = $"{playerList.Count} players logged in.";
		var message = $"{header}\n{string.Join('\n', playerList)}\n{footer}";

		await NotifyService.Notify(executor, message, executor);

		return new None();
	}

	private bool MatchesPattern(string playerName, string pattern)
	{
		if (pattern.Contains('*') || pattern.Contains('?'))
		{
			return MushText.IsWildcardMatch(MarkupText.Plain(playerName), pattern);
		}

		return playerName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
	}

	private async ValueTask<string> GetDoingText(AnySharpObject executor, AnySharpObject player)
	{
		var doingAttr = await AttributeService.GetAttributeAsync(
			executor,
			player,
			"DOING",
			mode: IAttributeService.AttributeMode.Read,
			parent: false);

		return doingAttr switch
		{
			SharpAttribute[] chain => chain.Last().Value.ToPlainText(),
			None or Error<string> => string.Empty
		};
	}
}
