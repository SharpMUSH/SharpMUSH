using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

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
		var args = parser.CurrentState.Arguments;
		var field = args.TryGetValue("1", out var given) ? given.Message.ToPlainText().Trim().ToLowerInvariant() : "id";
		if (field is not ("id" or "name"))
			return new CallState("#-1 FIELD MUST BE ID OR NAME");

		return await WithCharacterAccount(parser, args["0"].Message.ToPlainText(), field == "id",
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
		=> await WithCharacterAccount(parser, parser.CurrentState.Arguments["0"].Message.ToPlainText(), true,
			async (holder, player) => holder is { Id: { } id }
				? new CallState(string.Join(' ', (await AccountService.GetCharactersAsync(id, ExecutionBudget.CurrentToken))
					.OrderBy(character => character.Object.Key)
					.Select(character => character.Object.DBRef.ToString())))
				: new CallState(player.Object.DBRef.ToString()));

	/// <summary>
	/// Locates a player and answers from the account holding it. <c>players.view</c> may always ask; when
	/// <paramref name="sameAccountMayAsk"/>, so may a character on that account.
	/// </summary>
	private async ValueTask<CallState> WithCharacterAccount(IMUSHCodeParser parser, string name, bool sameAccountMayAsk,
		Func<SharpAccount?, SharpPlayer, ValueTask<CallState>> answer)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, name, LocateFlags.PlayersPreference,
			async found => found is SharpPlayer player
				? await AnsweredForAsync(executor, player, sameAccountMayAsk, answer)
				: new CallState(ErrorMessages.Returns.NotAPlayer));
	}

	private async ValueTask<CallState> AnsweredForAsync(AnySharpObject executor, SharpPlayer player, bool sameAccountMayAsk,
		Func<SharpAccount?, SharpPlayer, ValueTask<CallState>> answer)
	{
		var token = ExecutionBudget.CurrentToken;
		var holder = await AccountService.GetAccountForCharacterAsync(player.Object.DBRef, token);
		if (await executor.Can(PortalPermission.PlayersView)
			|| (sameAccountMayAsk && await OnAccountAsync(executor, holder, player, token)))
			return await answer(holder, player);
		return new CallState(ErrorMessages.Returns.PermissionDenied);
	}

	/// <summary>True when the executor is the character itself, or a character on the account holding it.</summary>
	private async ValueTask<bool> OnAccountAsync(AnySharpObject executor, SharpAccount? holder, SharpPlayer player, CancellationToken token)
		=> executor.Object().DBRef.Number == player.Object.DBRef.Number
			|| (holder is { Id: { } id } && executor.IsPlayer
				&& (await AccountService.GetAccountForCharacterAsync(executor.Object().DBRef, token))?.Id == id);
}
