using MarkupString.Layout;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>The longest stretch of a value's JSON <c>@accval &lt;player&gt;</c> shows in its listing.</summary>
	private const int AccountValuePreviewLength = 40;

	/// <summary>
	/// <c>@accval</c>: the values (JSON) kept on the account holding a player. <c>@accval &lt;player&gt;</c> lists them,
	/// <c>&lt;player&gt;/&lt;name&gt;</c> shows one, <c>=&lt;json&gt;</c> sets it, <c>=</c> alone clears it, and
	/// <c>/lock</c> and <c>/unlock</c> lock it the way <c>@atrlock</c> locks an attribute. Every check is
	/// <see cref="IAccountValueService"/>'s, the same as the functions'.
	/// </summary>
	[SharpCommand(Name = "@ACCVAL", Switches = ["LOCK", "UNLOCK"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["player/name", "json"])]
	public async ValueTask<Option<CallState>> AccVal(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.ToArray();
		var args = parser.CurrentState.ArgumentList;
		var left = (args.GetValueOrDefault("0")?.Message.ToPlainText() ?? string.Empty).Trim();
		var json = args.GetValueOrDefault("1")?.Message.ToPlainText() ?? string.Empty;
		var slash = left.IndexOf('/');
		var (playerName, name) = slash >= 0 ? (left[..slash].Trim(), left[(slash + 1)..].Trim()) : (left, string.Empty);

		if (switches.Length > 1)
			return await AccValSaidAsync(executor, MarkupText.Plain("Choose one of /lock and /unlock."));
		if (switches.Length == 0 && slash < 0 && args.ContainsKey("1"))
			return await AccValSaidAsync(executor, MarkupText.Plain("Usage: @accval <player>/<name>=<json>. See help @accval."));
		if (switches.Length == 1 && (slash < 0 || args.ContainsKey("1")))
			return await AccValSaidAsync(executor, MarkupText.Plain($"Usage: @accval/{switches[0].ToLowerInvariant()} <player>/<name>. See help @accval."));

		if (await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, playerName, LocateFlags.PlayersPreference)
				is not AnySharpObject target)
			return CallState.Empty;
		if (target is not SharpPlayer player)
			return await AccValSaidAsync(executor, MarkupText.Plain(ErrorMessages.Returns.NotAPlayer));

		var token = ExecutionBudget.CurrentToken;
		var output = switches.FirstOrDefault() switch
		{
			"LOCK" or "UNLOCK" => Done(await AccountValueService.LockAsync(executor, player, name, switches[0] == "LOCK", token),
				switches[0] == "LOCK" ? "Locked." : "Unlocked."),
			_ when slash < 0 => await AccValListAsync(parser, executor, player),
			_ when args.ContainsKey("1") => Done(await AccountValueService.SetAsync(executor, player, name, json, token),
				string.IsNullOrWhiteSpace(json) ? "Cleared." : "Set."),
			_ => await AccountValueService.GetAsync(executor, player, name, token) switch
			{
				AccountValue value => MarkupText.Plain(value.Json),
				NotFound => MarkupText.Plain($"{player.Object.Name}'s account has no value named '{name}'."),
				Error<string> error => MarkupText.Plain(error.Value)
			}
		};
		return await AccValSaidAsync(executor, output);

		static MString Done(Result<Success> result, string said) => result switch
		{
			Success => MarkupText.Plain(said),
			Error<string> error => MarkupText.Plain(error.Value)
		};
	}

	private async ValueTask<Option<CallState>> AccValSaidAsync(AnySharpObject executor, MString output)
	{
		await NotifyService.Notify(executor, output);
		return new CallState(output);
	}

	/// <summary><c>@accval &lt;player&gt;</c>: a titled table of the account's values, each with its lock, owner and the start of its JSON.</summary>
	private async ValueTask<MString> AccValListAsync(IMUSHCodeParser parser, AnySharpObject executor, SharpPlayer player)
		=> await AccountValueService.ListAsync(executor, player, ExecutionBudget.CurrentToken) switch
		{
			IReadOnlyList<AccountValue> values => AccValTable(parser, player, values),
			Error<string> error => MarkupText.Plain(error.Value)
		};

	private static MString AccValTable(IMUSHCodeParser parser, SharpPlayer player, IReadOnlyList<AccountValue> values)
	{
		var title = MarkupText.Plain($"Account values of {player.Object.Name}");
		if (values.Count == 0)
			return ServerLayout.Build(ServerLayout.Panel(title, new TextBlock(MarkupText.Plain("None."))), 78, parser.HouseLook());

		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Name")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Locked")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Owner")) { Wrap = false, Priority = 3 },
				new TableColumn(MarkupText.Plain("Value")) { Min = 10 },
			],
			values.Select(value => new[]
			{
				value.Name,
				value.Locked ? "yes" : "no",
				value.Owner,
				value.Json.Length <= AccountValuePreviewLength ? value.Json : value.Json[..(AccountValuePreviewLength - 3)] + "..."
			}));
		return ServerLayout.Build(ServerLayout.Panel(title, table), 78, parser.HouseLook());
	}
}
