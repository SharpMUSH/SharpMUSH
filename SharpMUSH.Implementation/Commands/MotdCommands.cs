using SharpMUSH.Library.Authorization;
using Humanizer;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Attributes;
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
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Common;

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
			if (!await executor.Can(PortalPermission.ChatAdmin))
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

		var motdType = switches.Contains("WIZARD") ? "wizard"
			: switches.Contains("DOWN") ? "down"
			: switches.Contains("FULL") ? "full"
			: "connect";

		return await SetMotdAsync(executor, motdType, switches.Contains("CLEAR"), argText,
			nameof(ErrorMessages.Notifications.MotdUsage));
	}

	/// <summary>PennMUSH's <c>@wizmotd</c>: <c>cmd_motd</c> with the type fixed to the wizard MOTD.</summary>
	[SharpCommand(Name = "@WIZMOTD", Switches = ["CLEAR"], Behavior = CB.Default, CommandLock = "PERM^chat.admin",
		MinArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> WizardMessageOfTheDay(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
		=> await SetMotdAsync(await parser.CurrentState.KnownExecutorObject(Mediator), "wizard",
			parser.CurrentState.Switches.Contains("CLEAR"), MotdArgument(parser),
			nameof(ErrorMessages.Notifications.WizMotdUsage));

	/// <summary>PennMUSH's <c>@rejectmotd</c>: <c>cmd_motd</c> with the type fixed to the full MOTD.</summary>
	[SharpCommand(Name = "@REJECTMOTD", Switches = ["CLEAR"], Behavior = CB.Default, CommandLock = "PERM^chat.admin",
		MinArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> RejectMessageOfTheDay(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
		=> await SetMotdAsync(await parser.CurrentState.KnownExecutorObject(Mediator), "full",
			parser.CurrentState.Switches.Contains("CLEAR"), MotdArgument(parser),
			nameof(ErrorMessages.Notifications.RejectMotdUsage));

	private static string MotdArgument(IMUSHCodeParser parser)
		=> ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 0, MarkupText.Empty).ToPlainText();

	/// <summary>
	/// PennMUSH's <c>do_motd</c> (src/bsd.c) once the type is known: the permission check for that type,
	/// then set or clear it. <c>@motd</c>, <c>@wizmotd</c> and <c>@rejectmotd</c> all land here; each
	/// keeps its own usage line.
	/// </summary>
	private async ValueTask<Option<CallState>> SetMotdAsync(AnySharpObject executor, string motdType, bool clear,
		string argText, string usageKey)
	{
		if (motdType == "connect")
		{
			if (!await executor.Can(PortalPermission.ChatAdmin) && !await executor.HasPower("ANNOUNCE"))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NeedAnnouncePower), executor);
				return CallState.Empty;
			}
		}
		else if (!await executor.Can(PortalPermission.ChatAdmin))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return CallState.Empty;
		}

		if (!clear && string.IsNullOrEmpty(argText))
		{
			await NotifyService.NotifyLocalized(executor, usageKey, executor);
			return CallState.Empty;
		}

		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>() ?? new MotdData();
		var message = clear ? null : argText;
		var newMotdData = motdType switch
		{
			"wizard" => motdData with { WizardMotd = message },
			"down" => motdData with { DownMotd = message },
			"full" => motdData with { FullMotd = message },
			_ => motdData with { ConnectMotd = message }
		};

		await ObjectDataService.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
		await NotifyService.NotifyLocalized(executor,
			clear ? nameof(ErrorMessages.Notifications.MotdClearedFormat) : nameof(ErrorMessages.Notifications.MotdSetFormat),
			executor, motdType.Humanize(LetterCasing.Title));
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

		var isWizard = await executor.Can(PortalPermission.ChatAdmin);

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdCurrentSettingsHeader), executor);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdSourceFormat), executor,
			await MessageSourceDescriptionAsync());

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

	/// <summary>Where the connect screen and the MOTDs come from, for <c>@listmotd</c> and <c>@list motd</c>.</summary>
	private async ValueTask<string> MessageSourceDescriptionAsync()
		=> await MessageService.GetSourceAsync() is (GameMessageSource.Object, _)
			&& await MessageService.MessagesObjectAsync() is AnySharpObject holder
				? $"the Messages object ({holder.Object().Name}(#{holder.Object().DBRef.Number}))"
				: "the stored messages (the Messages page)";

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

			if (!string.IsNullOrWhiteSpace(pattern) && !await MatchesPattern(obj, pattern))
			{
				continue;
			}

			var doingText = await GetDoingText(parser, executor, obj);

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

	/// <summary>
	/// PennMUSH's <c>who_check_name</c> (<c>src/bsd.c:5602-5633</c>): a pattern without wildcards is a
	/// prefix of the name; one with wildcards matches the name or any of the player's aliases, read with
	/// <c>atr_get</c> - inherited, and with no permission check.
	/// </summary>
	private async ValueTask<bool> MatchesPattern(AnySharpObject player, string pattern)
	{
		var playerName = player.Object().Name;
		if (!(pattern.Contains('*') || pattern.Contains('?')))
		{
			return playerName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
		}

		if (MushText.IsWildcardMatch(MarkupText.Plain(playerName), pattern))
		{
			return true;
		}

		return await AttributeService.GetAttributeAsync(await HelperFunctions.GetGod(Mediator), player,
				PlayerAliases.AttributeName, IAttributeService.AttributeMode.Read, parent: true) is SharpAttribute[] chain
			&& PlayerAliases.Split(chain.Last().Value.ToPlainText())
				.Any(alias => MushText.IsWildcardMatch(MarkupText.Plain(alias), pattern));
	}

	/// <summary>
	/// PennMUSH's <c>get_doing</c> (<c>src/bsd.c:6237-6255</c>): <c>@doing</c> is fetched with
	/// <c>UFUN_IGNORE_PERMS</c> and run, so it is inherited, evaluated, and shown whoever is looking -
	/// the same read <c>doing()</c> makes.
	/// </summary>
	private async ValueTask<string> GetDoingText(IMUSHCodeParser parser, AnySharpObject viewer, AnySharpObject player)
		=> (await AttributeHelpers.EvaluateFormatAttribute(AttributeService, parser, viewer, player, "DOING",
			new Dictionary<string, CallState>(), MarkupText.Empty)).ToPlainText();
}
