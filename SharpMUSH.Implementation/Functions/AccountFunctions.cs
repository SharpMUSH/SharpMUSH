using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
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
	/// <c>bucket(&lt;player&gt;, &lt;name&gt;)</c>: the JSON in the bucket <c>&lt;name&gt;</c> on the account holding the
	/// player, empty when there is no such bucket or no account. Open to <c>players.view</c> and to a character on
	/// that account.
	/// </summary>
	[SharpFunction(Name = "bucket", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player", "name"])]
	public async ValueTask<CallState> Bucket(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
		return AccountBuckets.Name(args["1"].Message.ToPlainText()) switch
		{
			string name => await WithCharacterAccount(parser, args["0"].Message.ToPlainText(), PortalPermission.PlayersView, true,
				async (holder, _) => holder is { Id: { } id } && await Mediator.Send(new GetAccountBucketQuery(id, name)) is string json
					? new CallState(json)
					: new CallState(string.Empty)),
			Error<string> error => BucketRefused(error)
		};
	}

	/// <summary>
	/// <c>buckets(&lt;player&gt;)</c>: the names of the buckets on the account holding the player, in ASCII order,
	/// space-separated; empty when it has none or the player is on no account. Open as <c>bucket()</c> is.
	/// </summary>
	[SharpFunction(Name = "buckets", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player"])]
	public async ValueTask<CallState> Buckets(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WithCharacterAccount(parser, parser.CurrentState.ArgumentList[0].Message.ToPlainText(), PortalPermission.PlayersView, true,
			async (holder, _) => new CallState(holder is { Id: { } id }
				? string.Join(' ', await Mediator.Send(new GetAccountBucketNamesQuery(id)))
				: string.Empty));

	/// <summary>
	/// <c>setbucket(&lt;player&gt;, &lt;name&gt;[, &lt;json&gt;])</c>: keeps the JSON in the bucket on the account holding
	/// the player, or removes the bucket when the JSON is empty or left out. Needs <c>players.moderate</c>: a bucket
	/// is account data staff and packages rely on, so a player cannot write their own.
	/// </summary>
	[SharpFunction(Name = "setbucket", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.HasSideFX,
		ParameterNames = ["player", "name", "json"])]
	public async ValueTask<CallState> SetBucket(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
		var given = args.TryGetValue("2", out var value) ? value.Message.ToPlainText() : string.Empty;
		return AccountBuckets.Name(args["1"].Message.ToPlainText()) switch
		{
			string name => await WithCharacterAccount(parser, args["0"].Message.ToPlainText(), PortalPermission.PlayersModerate, false,
				(holder, _) => holder is { Id: { } id }
					? WriteBucketAsync(id, name, given)
					: ValueTask.FromResult(new CallState(ErrorMessages.Returns.BucketPlayerHasNoAccount))),
			Error<string> error => BucketRefused(error)
		};
	}

	private async ValueTask<CallState> WriteBucketAsync(string accountId, string name, string given)
	{
		if (string.IsNullOrWhiteSpace(given))
		{
			await Mediator.Send(new RemoveAccountBucketCommand(accountId, name));
			return CallState.Empty;
		}

		return AccountBuckets.Json(given, (int)Configuration.CurrentValue.Limit.MaxAttributeValueLength) switch
		{
			string json => await Mediator.Send(new SetAccountBucketCommand(accountId, name, json)) switch
			{
				Success => CallState.Empty,
				NotFound => new CallState(ErrorMessages.Returns.BucketPlayerHasNoAccount),
				AccountBucketsFull => new CallState(string.Format(ErrorMessages.Returns.BucketsFullFormat, AccountBuckets.MaxPerAccount))
			},
			Error<string> error => BucketRefused(error)
		};
	}

	private static CallState BucketRefused(Error<string> error) => new(string.Format(ErrorMessages.Returns.ReasonFormat, error.Value));

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
			|| (sameAccountMayAsk && await OnAccountAsync(executor, holder, player, token)))
			return await answer(holder, player);
		return PermissionRefusal.Needs(permission).Return;
	}

	/// <summary>True when the executor is the character itself, or a character on the account holding it.</summary>
	private async ValueTask<bool> OnAccountAsync(AnySharpObject executor, SharpAccount? holder, SharpPlayer player, CancellationToken token)
		=> executor.Object().DBRef.Number == player.Object.DBRef.Number
			|| (holder is { Id: { } id } && executor.IsPlayer
				&& (await AccountService.GetAccountForCharacterAsync(executor.Object().DBRef, token))?.Id == id);
}
