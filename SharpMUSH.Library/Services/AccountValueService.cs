using Mediator;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>The account a reader may read: the account, <see cref="NotFound"/> for a player on none, or the refusal.</summary>
internal union ReadableAccount(SharpAccount, NotFound, Error<string>);

/// <summary>An account a writer may change, with the objid changes are made for and whether they override a lock.</summary>
internal sealed record WritableAccount(string Id, string Writer, bool OverrideLock);

public class AccountValueService(IMediator mediator, IAccountService accounts, IOptionsMonitor<SharpMUSHOptions> options)
	: IAccountValueService
{
	public async ValueTask<AccountValueRead> GetAsync(AnySharpObject executor, SharpPlayer player, string name,
		CancellationToken cancellationToken = default)
		=> AccountValues.Name(name) switch
		{
			string key => await ReadableAccountAsync(executor, player, cancellationToken) switch
			{
				SharpAccount { Id: { } id } => await mediator.Send(new GetAccountValueQuery(id, key), cancellationToken) switch
				{
					AccountValue value => value,
					NotFound none => none
				},
				SharpAccount or NotFound => new NotFound(),
				Error<string> error => error
			},
			Error<string> error => error
		};

	public async ValueTask<Result<IReadOnlyList<AccountValue>>> ListAsync(AnySharpObject executor, SharpPlayer player,
		CancellationToken cancellationToken = default)
		=> await ReadableAccountAsync(executor, player, cancellationToken) switch
		{
			SharpAccount { Id: { } id } => new Result<IReadOnlyList<AccountValue>>(
				await mediator.Send(new GetAccountValuesQuery(id), cancellationToken)),
			SharpAccount or NotFound => new Result<IReadOnlyList<AccountValue>>(Array.Empty<AccountValue>()),
			Error<string> error => error
		};

	public async ValueTask<Result<Success>> SetAsync(AnySharpObject executor, SharpPlayer player, string name, string json,
		CancellationToken cancellationToken = default)
		=> AccountValues.Name(name) switch
		{
			string key => await WritableAccountAsync(executor, player, cancellationToken) switch
			{
				WritableAccount account when string.IsNullOrWhiteSpace(json) => await RemoveAsync(account, key, cancellationToken),
				WritableAccount account => await WriteAsync(account, key, json, cancellationToken),
				Error<string> error => error
			},
			Error<string> error => error
		};

	public async ValueTask<Result<Success>> LockAsync(AnySharpObject executor, SharpPlayer player, string name, bool locked,
		CancellationToken cancellationToken = default)
		=> AccountValues.Name(name) switch
		{
			string key => await WritableAccountAsync(executor, player, cancellationToken) switch
			{
				WritableAccount account => await mediator.Send(
					new SetAccountValueLockCommand(account.Id, key, account.Writer, locked, account.OverrideLock), cancellationToken) switch
				{
					Success success => success,
					NotFound => new Error<string>(ErrorMessages.Returns.NoSuchAccountValue),
					AccountValueLocked => new Error<string>(ErrorMessages.Returns.AccountValueLocked)
				},
				Error<string> error => error
			},
			Error<string> error => error
		};

	/// <summary>Removing a value that is not there does nothing, as clearing an unset attribute does.</summary>
	private async ValueTask<Result<Success>> RemoveAsync(WritableAccount account, string key, CancellationToken cancellationToken)
		=> await mediator.Send(new RemoveAccountValueCommand(account.Id, key, account.Writer, account.OverrideLock),
			cancellationToken) switch
		{
			Success or NotFound => new Success(),
			AccountValueLocked => new Error<string>(ErrorMessages.Returns.AccountValueLocked)
		};

	private async ValueTask<Result<Success>> WriteAsync(WritableAccount account, string key, string json,
		CancellationToken cancellationToken)
		=> AccountValues.Json(json, (int)Math.Min(options.CurrentValue.Limit.MaxAttributeValueLength, int.MaxValue)) switch
		{
			string valid => await mediator.Send(
				new SetAccountValueCommand(account.Id, key, valid, account.Writer, account.OverrideLock), cancellationToken) switch
			{
				Success success => success,
				NotFound => new Error<string>(ErrorMessages.Returns.PlayerHasNoAccount),
				AccountValuesFull => new Error<string>(string.Format(null, ErrorMessages.Returns.AccountValuesFullFormat,
					AccountValues.MaxPerAccount)),
				AccountValueLocked => new Error<string>(ErrorMessages.Returns.AccountValueLocked)
			},
			Error<string> error => error
		};

	/// <summary><c>players.view</c> reads any account's values; a character reads those of its own account.</summary>
	private async ValueTask<ReadableAccount> ReadableAccountAsync(AnySharpObject executor, SharpPlayer player,
		CancellationToken cancellationToken)
	{
		var holder = await accounts.GetAccountForCharacterAsync(player.Object.DBRef, cancellationToken);
		if (!await executor.Can(PortalPermission.PlayersView)
			&& !await accounts.SharesAccountAsync(executor, player, holder, cancellationToken))
			return new Error<string>(PermissionRefusal.Needs(PortalPermission.PlayersView).ReturnText);
		return holder is null ? new NotFound() : holder;
	}

	/// <summary>
	/// <c>players.moderate</c> writes any account's values: they are account data staff and packages rely on, so a player
	/// does not write their own. Changes are made for the executor's owner, and override a lock with <c>game.override</c> over
	/// every character on the account.
	/// </summary>
	private async ValueTask<Result<WritableAccount>> WritableAccountAsync(AnySharpObject executor, SharpPlayer player,
		CancellationToken cancellationToken)
	{
		if (!await executor.Can(PortalPermission.PlayersModerate))
			return new Error<string>(PermissionRefusal.Needs(PortalPermission.PlayersModerate).ReturnText);
		if (await accounts.GetAccountForCharacterAsync(player.Object.DBRef, cancellationToken) is not { Id: { } id })
			return new Error<string>(ErrorMessages.Returns.PlayerHasNoAccount);

		var owner = await executor.Object().Owner.WithCancellation(cancellationToken);
		return new WritableAccount(id, owner.Object.DBRef.ToString(), await OverridesEveryCharacterAsync(executor, id, cancellationToken));
	}

	/// <summary>
	/// Whether <c>game.override</c> reaches every character on the account. A value belongs to the account, not to the
	/// character it was named through, so an override that reaches only a low-ranked alt does not open it.
	/// </summary>
	private async ValueTask<bool> OverridesEveryCharacterAsync(AnySharpObject executor, string accountId,
		CancellationToken cancellationToken)
	{
		foreach (var character in await accounts.GetCharactersAsync(accountId, cancellationToken))
			if (!await executor.CanOver(PortalPermission.Override, character))
				return false;
		return true;
	}
}
