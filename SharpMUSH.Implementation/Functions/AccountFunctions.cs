using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// <c>account(&lt;player&gt;[, id|name])</c>: the account a character is linked to, empty when it is on none.
	/// <c>id</c> (the default) is the account's stable id; <c>name</c> its username, which is half of a login
	/// and so needs <c>players.view</c>. The id is also open to a character on the same account.
	/// </summary>
	[SharpFunction(Name = "account", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player", "field"])]
	public async ValueTask<CallState> Account(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
		var field = args.TryGetValue("1", out var given) ? given.Message.ToPlainText().Trim().ToLowerInvariant() : "id";
		if (field is not ("id" or "name"))
			return new CallState("#-1 FIELD MUST BE ID OR NAME");

		return await WithCharacterAccount(parser, args["0"].Message.ToPlainText(), PortalPermission.PlayersView, field == "id",
			(holder, _) => ValueTask.FromResult(new CallState(holder is null ? string.Empty : field == "id" ? holder.Id ?? string.Empty : holder.Username)));
	}

	/// <summary>
	/// <c>accountchars(&lt;player&gt;)</c>: the objids of every character on the same account as the player, lowest
	/// dbref first; just the player when it is on no account. Open to <c>players.view</c> and to a character on that
	/// account.
	/// </summary>
	[SharpFunction(Name = "accountchars", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player"])]
	public async ValueTask<CallState> AccountChars(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WithCharacterAccount(parser, parser.CurrentState.ArgumentList[0].Message.ToPlainText(), PortalPermission.PlayersView, true,
			async (holder, player) => holder is { Id: { } id }
				? new CallState(string.Join(' ', (await AccountService.GetCharactersAsync(id, ExecutionBudget.CurrentToken))
					.OrderBy(character => character.Object.Key)
					.Select(character => character.Object.DBRef.ToString())))
				: new CallState(player.Object.DBRef.ToString()));

	/// <summary>
	/// <c>getaccval(&lt;player&gt;/&lt;name&gt;)</c>: the JSON kept as the value <c>&lt;name&gt;</c> on the account holding
	/// the player, empty when there is no such value or no account. Open to <c>players.view</c> and to a character on
	/// that account.
	/// </summary>
	[SharpFunction(Name = "getaccval", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player/name"])]
	public async ValueTask<CallState> GetAccVal(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var (player, name) = SplitAccountValue(parser.CurrentState.ArgumentList["0"].Message.ToPlainText());
		return await WithAccountPlayer(parser, player, async (executor, found) =>
			await AccountValueService.GetAsync(executor, found, name, ExecutionBudget.CurrentToken) switch
			{
				AccountValue value => new CallState(value.Json),
				NotFound => CallState.Empty,
				Error<string> error => new CallState(error.Value)
			});
	}

	/// <summary>
	/// <c>laccval(&lt;player&gt;[/&lt;pattern&gt;])</c>: the names of the values on the account holding the player, in
	/// ASCII order, space-separated, those matching the wildcard <c>&lt;pattern&gt;</c> when one is given. Open as
	/// <c>getaccval()</c> is.
	/// </summary>
	[SharpFunction(Name = "laccval", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player/pattern"])]
	public async ValueTask<CallState> LAccVal(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var (player, pattern) = SplitAccountValue(parser.CurrentState.ArgumentList["0"].Message.ToPlainText());
		var matches = SoftcodeRegex.Wildcard(string.IsNullOrEmpty(pattern) ? "*" : pattern);
		return await WithAccountPlayer(parser, player, async (executor, found) =>
			await AccountValueService.ListAsync(executor, found, ExecutionBudget.CurrentToken) switch
			{
				IReadOnlyList<AccountValue> values => new CallState(string.Join(' ',
					values.Select(value => value.Name).Where(name => matches.IsMatch(name)))),
				Error<string> error => new CallState(error.Value)
			});
	}

	/// <summary>
	/// <c>setaccval(&lt;player&gt;/&lt;name&gt;[, &lt;json&gt;])</c>: keeps the JSON as the value on the account holding the
	/// player, or removes the value when the JSON is empty or left out. Needs <c>players.moderate</c>: account values
	/// are data staff and packages rely on, so a player does not write their own. A locked value changes only for its
	/// owner and for <c>game.override</c>.
	/// </summary>
	[SharpFunction(Name = "setaccval", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.HasSideFX, ParameterNames = ["player/name", "json"])]
	public async ValueTask<CallState> SetAccVal(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
		var (player, name) = SplitAccountValue(args["0"].Message.ToPlainText());
		var json = args.TryGetValue("1", out var given) ? given.Message.ToPlainText() : string.Empty;
		return await WithAccountPlayer(parser, player, async (executor, found) =>
			Answered(await AccountValueService.SetAsync(executor, found, name, json, ExecutionBudget.CurrentToken)));
	}

	/// <summary>
	/// <c>accvallock(&lt;player&gt;/&lt;name&gt;[, on|off])</c>: with one argument, 1 when the value is locked and 0 when it
	/// is not, read as <c>getaccval()</c> is. With <c>on</c> or <c>off</c>, locks or unlocks it, as <c>atrlock()</c> does an
	/// attribute: that needs <c>players.moderate</c>, and locking makes the caller's owner the value's owner.
	/// </summary>
	[SharpFunction(Name = "accvallock", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.HasSideFX, SideEffectMinArgs = 2,
		ParameterNames = ["player/name", "on|off"])]
	public async ValueTask<CallState> AccValLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
		var (player, name) = SplitAccountValue(args["0"].Message.ToPlainText());
		var action = args.TryGetValue("1", out var given) ? given.Message.ToPlainText() : string.Empty;
		if (string.IsNullOrWhiteSpace(action))
			return await WithAccountPlayer(parser, player, async (executor, found) =>
				await AccountValueService.GetAsync(executor, found, name, ExecutionBudget.CurrentToken) switch
				{
					AccountValue value => new CallState(value.Locked ? "1" : "0"),
					NotFound => new CallState(ErrorMessages.Returns.NoSuchAccountValue),
					Error<string> error => new CallState(error.Value)
				});

		if (AttributeLockSwitch.Parse(action) is not bool locked)
			return new CallState(ErrorMessages.Returns.InvalidValue);
		return await WithAccountPlayer(parser, player, async (executor, found) =>
			Answered(await AccountValueService.LockAsync(executor, found, name, locked, ExecutionBudget.CurrentToken)));
	}

	/// <summary><c>&lt;player&gt;/&lt;name&gt;</c> split at the first <c>/</c>; the name is empty when there is none.</summary>
	private static (string Player, string Name) SplitAccountValue(string given)
		=> given.IndexOf('/') is var at and >= 0 ? (given[..at].Trim(), given[(at + 1)..].Trim()) : (given.Trim(), string.Empty);

	private static CallState Answered(Result<Success> result) => result switch
	{
		Success => CallState.Empty,
		Error<string> error => new CallState(error.Value)
	};

	/// <summary>Locates the player an account value is asked of; the service decides who may ask.</summary>
	private async ValueTask<CallState> WithAccountPlayer(IMUSHCodeParser parser, string name,
		Func<AnySharpObject, SharpPlayer, ValueTask<CallState>> answer)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, name, LocateFlags.PlayersPreference,
			async found => found is SharpPlayer player
				? await answer(executor, player)
				: new CallState(ErrorMessages.Returns.NotAPlayer));
	}

	/// <summary>
	/// Locates a player and answers from the account holding it. <paramref name="permission"/> may always ask; when
	/// <paramref name="sameAccountMayAsk"/>, so may a character on that account.
	/// </summary>
	private async ValueTask<CallState> WithCharacterAccount(IMUSHCodeParser parser, string name, string permission,
		bool sameAccountMayAsk, Func<SharpAccount?, SharpPlayer, ValueTask<CallState>> answer)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, name, LocateFlags.PlayersPreference,
			async found => found is SharpPlayer player
				? await AnsweredForAsync(executor, player, permission, sameAccountMayAsk, answer)
				: new CallState(ErrorMessages.Returns.NotAPlayer));
	}

	private async ValueTask<CallState> AnsweredForAsync(AnySharpObject executor, SharpPlayer player, string permission,
		bool sameAccountMayAsk, Func<SharpAccount?, SharpPlayer, ValueTask<CallState>> answer)
	{
		var token = ExecutionBudget.CurrentToken;
		var holder = await AccountService.GetAccountForCharacterAsync(player.Object.DBRef, token);
		if (await executor.Can(permission)
			|| (sameAccountMayAsk && await AccountService.SharesAccountAsync(executor, player, holder, token)))
			return await answer(holder, player);
		return PermissionRefusal.Needs(permission).Return;
	}
}
