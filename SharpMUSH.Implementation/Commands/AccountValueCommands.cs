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

	private static readonly string[] AccountValueLockKinds = ["VIEW", "WRITE", "DELETE"];

	/// <summary>
	/// <c>@accval</c>: the values (JSON) kept on the account holding a player, and the locks on them.
	/// <c>@accval &lt;player&gt;</c> lists them, <c>&lt;player&gt;/&lt;name&gt;</c> shows one, <c>=&lt;json&gt;</c> sets it and
	/// <c>=</c> alone clears it. <c>/lock/view</c>, <c>/lock/write</c> and <c>/lock/delete</c> set a value's own locks, and
	/// <c>/rule/&lt;kind&gt; &lt;pattern&gt;=&lt;lock&gt;</c> the game-wide rules (<c>/rules</c> lists them). Every check is
	/// <see cref="IAccountValueService"/>'s, the same as the functions'.
	/// </summary>
	[SharpCommand(Name = "@ACCVAL", Switches = ["LOCK", "RULE", "RULES", "VIEW", "WRITE", "DELETE"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 2, ParameterNames = ["player/name", "json"])]
	public async ValueTask<Option<CallState>> AccVal(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.ToArray();
		var args = parser.CurrentState.ArgumentList;
		var left = (args.GetValueOrDefault("0")?.Message.ToPlainText() ?? string.Empty).Trim();
		var hasRight = args.ContainsKey("1");
		var right = args.GetValueOrDefault("1")?.Message.ToPlainText() ?? string.Empty;
		var kinds = switches.Where(AccountValueLockKinds.Contains).ToArray();
		var operation = switches.Except(AccountValueLockKinds).ToArray() switch
		{
			[] => kinds.Length == 0 ? "VALUE" : "",
			["LOCK"] when kinds.Length <= 1 => "LOCK",
			["RULE"] when kinds.Length <= 1 => "RULE",
			["RULES"] when kinds.Length == 0 => "RULES",
			_ => ""
		};
		var kind = kinds.FirstOrDefault() ?? string.Empty;

		var output = operation switch
		{
			"RULES" => AccValRules(parser, await AccountValueService.ListRulesAsync(ExecutionBudget.CurrentToken)),
			"RULE" when kind.Length > 0 && hasRight => Said(await AccountValueService.SetRuleAsync(executor, left, kind, right,
				ExecutionBudget.CurrentToken), right.Trim().Length == 0 ? "Rule cleared." : "Rule set."),
			"RULE" => MarkupText.Plain("Usage: @accval/rule/<view|write|delete> <pattern>=<lock>. See help @accval."),
			"LOCK" or "VALUE" => await AccValForPlayerAsync(parser, executor, operation, kind, left, hasRight, right),
			_ => MarkupText.Plain("Choose @accval, /lock with one of /view, /write and /delete, /rule with one of them, or /rules.")
		};
		await NotifyService.Notify(executor, output);
		return new CallState(output);
	}

	private async ValueTask<MString> AccValForPlayerAsync(IMUSHCodeParser parser, AnySharpObject executor, string operation,
		string kind, string left, bool hasRight, string right)
	{
		var slash = left.IndexOf('/');
		var (playerName, name) = slash >= 0 ? (left[..slash].Trim(), left[(slash + 1)..].Trim()) : (left, string.Empty);
		if (left.Length == 0 || (slash < 0 && (hasRight || operation == "LOCK")) || (operation == "LOCK" && kind.Length > 0 != hasRight))
			return MarkupText.Plain(operation == "LOCK"
				? "Usage: @accval/lock[/<view|write|delete>] <player>/<name>[=<lock>]. See help @accval."
				: "Usage: @accval <player>[/<name>[=<json>]]. See help @accval.");

		if (await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, playerName, LocateFlags.PlayersPreference)
				is not AnySharpObject target)
			return MarkupText.Empty;
		if (target is not SharpPlayer player)
			return MarkupText.Plain(ErrorMessages.Returns.NotAPlayer);

		var token = ExecutionBudget.CurrentToken;
		return operation switch
		{
			"LOCK" when hasRight => Said(await AccountValueService.SetLockAsync(executor, player, name, kind, right, token),
				right.Trim().Length == 0 ? "Unlocked." : "Locked."),
			"LOCK" => await AccountValueService.GetAsync(executor, player, name, token) switch
			{
				AccountValue value => AccValLocks(parser, value, await AccountValueService.ListRulesAsync(token)),
				NotFound => MarkupText.Plain(ErrorMessages.Returns.NoSuchAccountValue),
				Error<string> error => MarkupText.Plain(error.Value)
			},
			_ when slash < 0 => AccValTable(parser, player, await AccountValueService.ListAsync(executor, player, token),
				await AccountValueService.ListRulesAsync(token)),
			_ when hasRight => Said(await AccountValueService.SetAsync(executor, player, name, right, token),
				string.IsNullOrWhiteSpace(right) ? "Cleared." : "Set."),
			_ => await AccountValueService.GetAsync(executor, player, name, token) switch
			{
				AccountValue value => MarkupText.Plain(value.Json),
				NotFound => MarkupText.Plain($"{player.Object.Name}'s account has no value named '{name}'."),
				Error<string> error => MarkupText.Plain(error.Value)
			}
		};
	}

	private static MString Said(Result<Success> result, string said) => result switch
	{
		Success => MarkupText.Plain(said),
		Error<string> error => MarkupText.Plain(error.Value)
	};

	/// <summary>
	/// A lock as a listing shows it: the value's own, <c>(rule)</c> when a rule decides, or nothing when the default
	/// permission does.
	/// </summary>
	private static string LockShown(IReadOnlyList<AccountValueRule> rules, AccountValue value, AccountValueLockKind kind)
		=> value.Locks[kind] is { Length: > 0 } own ? own
			: AccountValueRules.Lock(rules, value.Name, kind).Length > 0 ? "(rule)"
			: string.Empty;

	/// <summary><c>@accval &lt;player&gt;</c>: a titled table of the values the caller may view, with their locks and the start of their JSON.</summary>
	private static MString AccValTable(IMUSHCodeParser parser, SharpPlayer player, IReadOnlyList<AccountValue> values,
		IReadOnlyList<AccountValueRule> rules)
	{
		var title = MarkupText.Plain($"Account values of {player.Object.Name}");
		if (values.Count == 0)
			return ServerLayout.Build(ServerLayout.Panel(title, new TextBlock(MarkupText.Plain("None."))), 78, parser.HouseLook());

		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Name")) { Wrap = false },
				new TableColumn(MarkupText.Plain("View")) { Priority = 2 },
				new TableColumn(MarkupText.Plain("Write")) { Priority = 2 },
				new TableColumn(MarkupText.Plain("Delete")) { Priority = 3 },
				new TableColumn(MarkupText.Plain("Value")) { Min = 10 },
			],
			values.Select(value => new[]
			{
				value.Name,
				LockShown(rules, value, AccountValueLockKind.View),
				LockShown(rules, value, AccountValueLockKind.Write),
				LockShown(rules, value, AccountValueLockKind.Delete),
				value.Json.Length <= AccountValuePreviewLength ? value.Json : value.Json[..(AccountValuePreviewLength - 3)] + "..."
			}));
		return ServerLayout.Build(ServerLayout.Panel(title, table), 78, parser.HouseLook());
	}

	/// <summary><c>@accval/lock &lt;player&gt;/&lt;name&gt;</c>: the value's own locks and the rules' that decide where it sets none.</summary>
	private static MString AccValLocks(IMUSHCodeParser parser, AccountValue value, IReadOnlyList<AccountValueRule> rules)
	{
		(string, MString)[] fields = Enum.GetValues<AccountValueLockKind>()
			.Select(kind => (kind.ToString(), MarkupText.Plain(value.Locks[kind] is { Length: > 0 } own ? own
				: AccountValueRules.Lock(rules, value.Name, kind) is { Length: > 0 } ruled ? $"{ruled} (rule)"
				: kind == AccountValueLockKind.View ? "default: players.view or the account's characters" : "default: players.moderate")))
			.ToArray();
		return ServerLayout.Build(ServerLayout.Section(MarkupText.Plain($"Locks on {value.Name}"), ServerLayout.KeyValues(fields)), 78,
			parser.HouseLook());
	}

	/// <summary><c>@accval/rules</c>: every game-wide rule, by pattern.</summary>
	private static MString AccValRules(IMUSHCodeParser parser, IReadOnlyList<AccountValueRule> rules)
	{
		var title = MarkupText.Plain("Account value rules");
		if (rules.Count == 0)
			return ServerLayout.Build(ServerLayout.Panel(title,
				new TextBlock(MarkupText.Plain("None. Set one with @accval/rule/<view|write|delete> <pattern>=<lock>."))), 78, parser.HouseLook());

		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Pattern")) { Wrap = false },
				new TableColumn(MarkupText.Plain("View")) { Min = 6 },
				new TableColumn(MarkupText.Plain("Write")) { Min = 6 },
				new TableColumn(MarkupText.Plain("Delete")) { Min = 6 },
			],
			rules.Select(rule => new[] { rule.Pattern, rule.Locks.View, rule.Locks.Write, rule.Locks.Delete }));
		return ServerLayout.Build(ServerLayout.Panel(title, table), 78, parser.HouseLook());
	}
}
