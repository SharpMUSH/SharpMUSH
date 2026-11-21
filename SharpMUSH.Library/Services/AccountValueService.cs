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

public class AccountValueService(
	IMediator mediator,
	IAccountService accounts,
	ILockService locks,
	IOptionsMonitor<SharpMUSHOptions> options) : IAccountValueService
{
	/// <summary>How many times a write checks again after the value's locks changed under it.</summary>
	private const int Attempts = 3;

	/// <summary>What a check of one player's account values needs: the account, the rules, and whether the caller overrides.</summary>
	private sealed record Scope(string? AccountId, SharpAccount? Holder, IReadOnlyList<AccountValueRule> Rules, bool Overrides);

	public async ValueTask<AccountValueRead> GetAsync(AnySharpObject executor, SharpPlayer player, string name,
		CancellationToken cancellationToken = default)
		=> AccountValues.Name(name) switch
		{
			string key => await ReadAsync(executor, player, key, cancellationToken),
			Error<string> error => error
		};

	public async ValueTask<IReadOnlyList<AccountValue>> ListAsync(AnySharpObject executor, SharpPlayer player,
		CancellationToken cancellationToken = default)
	{
		var scope = await ScopeAsync(executor, player, cancellationToken);
		if (scope.AccountId is not { } id) return [];

		var visible = new List<AccountValue>();
		foreach (var value in await mediator.Send(new GetAccountValuesQuery(id), cancellationToken))
			if (await MayAsync(executor, player, scope, value.Name, value.Locks, AccountValueLockKind.View, cancellationToken) is Success)
				visible.Add(value);
		return visible;
	}

	public async ValueTask<Result<Success>> SetAsync(AnySharpObject executor, SharpPlayer player, string name, string json,
		CancellationToken cancellationToken = default)
		=> AccountValues.Name(name) switch
		{
			string key when string.IsNullOrWhiteSpace(json) => await RemoveAsync(executor, player, key, cancellationToken),
			string key => AccountValues.Json(json, (int)Math.Min(options.CurrentValue.Limit.MaxAttributeValueLength, int.MaxValue)) switch
			{
				string valid => await WriteAsync(executor, player, key, valid, cancellationToken),
				Error<string> error => error
			},
			Error<string> error => error
		};

	public async ValueTask<Result<string>> GetLockAsync(AnySharpObject executor, SharpPlayer player, string name, string kind,
		CancellationToken cancellationToken = default)
		=> Parsed(name, kind) switch
		{
			LockTarget target => await ReadAsync(executor, player, target.Key, cancellationToken) switch
			{
				AccountValue value => value.Locks[target.Kind],
				NotFound => new Error<string>(ErrorMessages.Returns.NoSuchAccountValue),
				Error<string> error => error
			},
			Error<string> error => error
		};

	public async ValueTask<Result<Success>> SetLockAsync(AnySharpObject executor, SharpPlayer player, string name, string kind,
		string lockString, CancellationToken cancellationToken = default)
		=> Parsed(name, kind) switch
		{
			LockTarget target => await SetOwnLockAsync(executor, player, target.Key, target.Kind, lockString, cancellationToken),
			Error<string> error => error
		};

	private async ValueTask<Result<Success>> SetOwnLockAsync(AnySharpObject executor, SharpPlayer player, string key,
		AccountValueLockKind lockKind, string lockString, CancellationToken cancellationToken)
	{
		if (!await executor.Can(PortalPermission.PlayersModerate))
			return new Error<string>(PermissionRefusal.Needs(PortalPermission.PlayersModerate).ReturnText);
		if (await BoundLockAsync(executor, player, lockString, cancellationToken) is not string bound)
			return new Error<string>(ErrorMessages.Returns.InvalidLock);

		for (var attempt = 0; attempt < Attempts; attempt++)
		{
			var scope = await ScopeAsync(executor, player, cancellationToken);
			if (scope.AccountId is not { } id) return new Error<string>(ErrorMessages.Returns.PlayerHasNoAccount);
			if (await mediator.Send(new GetAccountValueQuery(id, key), cancellationToken) is not AccountValue value)
				return new Error<string>(ErrorMessages.Returns.NoSuchAccountValue);
			if (await MayAsync(executor, player, scope, key, value.Locks, AccountValueLockKind.Write, cancellationToken) is Error<string> refused)
				return refused;

			switch (await mediator.Send(new SetAccountValueLocksCommand(id, key, value.Locks, value.Locks.With(lockKind, bound)),
				cancellationToken))
			{
				case Success success: return success;
				case NotFound: return new Error<string>(ErrorMessages.Returns.NoSuchAccountValue);
			}
		}

		return new Error<string>(ErrorMessages.Returns.AccountValueChanged);
	}

	public ValueTask<IReadOnlyList<AccountValueRule>> ListRulesAsync(CancellationToken cancellationToken = default)
		=> mediator.Send(new GetAccountValueRulesQuery(), cancellationToken);

	public async ValueTask<Result<string>> GetRuleAsync(string pattern, string kind, CancellationToken cancellationToken = default)
		=> Parsed(pattern, kind) switch
		{
			LockTarget target => (await ListRulesAsync(cancellationToken)).FirstOrDefault(rule => rule.Pattern == target.Key)?.Locks[target.Kind]
				?? string.Empty,
			Error<string> error => error
		};

	public async ValueTask<Result<Success>> SetRuleAsync(AnySharpObject executor, string pattern, string kind, string lockString,
		CancellationToken cancellationToken = default)
		=> Parsed(pattern, kind) switch
		{
			LockTarget target => await SetRuleLockAsync(executor, target.Key, target.Kind, lockString, cancellationToken),
			Error<string> error => error
		};

	private async ValueTask<Result<Success>> SetRuleLockAsync(AnySharpObject executor, string key, AccountValueLockKind lockKind,
		string lockString, CancellationToken cancellationToken)
	{
		if (!await executor.Can(PortalPermission.ConfigAdmin))
			return new Error<string>(PermissionRefusal.Needs(PortalPermission.ConfigAdmin).ReturnText);
		if (await BoundLockAsync(executor, executor, lockString, cancellationToken) is not string bound)
			return new Error<string>(ErrorMessages.Returns.InvalidLock);

		var current = (await ListRulesAsync(cancellationToken)).FirstOrDefault(rule => rule.Pattern == key)?.Locks
			?? AccountValueLocks.None;
		await mediator.Send(new SetAccountValueRuleCommand(key, current.With(lockKind, bound)), cancellationToken);
		return new Success();
	}

	/// <summary>A value name (or rule pattern) and a lock kind, both checked.</summary>
	private sealed record LockTarget(string Key, AccountValueLockKind Kind);

	private static Result<LockTarget> Parsed(string name, string kind) => AccountValues.Name(name) switch
	{
		string key => AccountValues.LockKind(kind) switch
		{
			AccountValueLockKind lockKind => new LockTarget(key, lockKind),
			Error<string> error => error
		},
		Error<string> error => error
	};

	private async ValueTask<AccountValueRead> ReadAsync(AnySharpObject executor, SharpPlayer player, string key,
		CancellationToken cancellationToken)
	{
		var scope = await ScopeAsync(executor, player, cancellationToken);
		Found<AccountValue> found = scope.AccountId is { } id
			? await mediator.Send(new GetAccountValueQuery(id, key), cancellationToken)
			: new NotFound();
		var own = found is AccountValue value ? value.Locks : AccountValueLocks.None;
		return await MayAsync(executor, player, scope, key, own, AccountValueLockKind.View, cancellationToken) switch
		{
			Error<string> refused => refused,
			Success => found switch
			{
				AccountValue readable => readable,
				NotFound none => none
			}
		};
	}

	private async ValueTask<Result<Success>> WriteAsync(AnySharpObject executor, SharpPlayer player, string key, string json,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; attempt < Attempts; attempt++)
		{
			var scope = await ScopeAsync(executor, player, cancellationToken);
			if (scope.AccountId is not { } id) return new Error<string>(ErrorMessages.Returns.PlayerHasNoAccount);
			var found = await mediator.Send(new GetAccountValueQuery(id, key), cancellationToken);
			var own = found is AccountValue value ? value.Locks : AccountValueLocks.None;
			if (await MayAsync(executor, player, scope, key, own, AccountValueLockKind.Write, cancellationToken) is Error<string> refused)
				return refused;

			Found<AccountValueLocks> expected = found is AccountValue seen ? seen.Locks : new NotFound();
			switch (await mediator.Send(new SetAccountValueCommand(id, key, json, expected), cancellationToken))
			{
				case Success success: return success;
				case NotFound: return new Error<string>(ErrorMessages.Returns.PlayerHasNoAccount);
				case AccountValuesFull:
					return new Error<string>(string.Format(null, ErrorMessages.Returns.AccountValuesFullFormat, AccountValues.MaxPerAccount));
			}
		}

		return new Error<string>(ErrorMessages.Returns.AccountValueChanged);
	}

	/// <summary>Removing a value that is not there does nothing, as clearing an unset attribute does.</summary>
	private async ValueTask<Result<Success>> RemoveAsync(AnySharpObject executor, SharpPlayer player, string key,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; attempt < Attempts; attempt++)
		{
			var scope = await ScopeAsync(executor, player, cancellationToken);
			if (scope.AccountId is not { } id
				|| await mediator.Send(new GetAccountValueQuery(id, key), cancellationToken) is not AccountValue value)
				return new Success();
			if (await MayAsync(executor, player, scope, key, value.Locks, AccountValueLockKind.Delete, cancellationToken) is Error<string> refused)
				return refused;

			if (await mediator.Send(new RemoveAccountValueCommand(id, key, value.Locks), cancellationToken) is not AccountValueChanged)
				return new Success();
		}

		return new Error<string>(ErrorMessages.Returns.AccountValueChanged);
	}

	/// <summary>
	/// Whether the executor passes the lock of <paramref name="kind"/> that decides for the value: an override over every
	/// character on the account passes; else the value's own lock, else the rules', else the default permission.
	/// </summary>
	private async ValueTask<Result<Success>> MayAsync(AnySharpObject executor, SharpPlayer player, Scope scope, string key,
		AccountValueLocks own, AccountValueLockKind kind, CancellationToken cancellationToken)
	{
		if (scope.Overrides) return new Success();

		var lockString = AccountValueRules.Effective(scope.Rules, key, own, kind);
		if (lockString.Length > 0)
			return await locks.Evaluate(lockString, player, executor)
				? new Success()
				: new Error<string>(string.Format(null, ErrorMessages.Returns.AccountValueLockedFormat, kind.ToString().ToUpperInvariant()));

		var scopeNeeded = kind == AccountValueLockKind.View ? PortalPermission.PlayersView : PortalPermission.PlayersModerate;
		if (await executor.Can(scopeNeeded)
			|| (kind == AccountValueLockKind.View && await accounts.SharesAccountAsync(executor, player, scope.Holder, cancellationToken)))
			return new Success();
		return new Error<string>(PermissionRefusal.Needs(scopeNeeded).ReturnText);
	}

	private async ValueTask<Scope> ScopeAsync(AnySharpObject executor, SharpPlayer player, CancellationToken cancellationToken)
	{
		var holder = await accounts.GetAccountForCharacterAsync(player.Object.DBRef, cancellationToken);
		var rules = await ListRulesAsync(cancellationToken);
		var characters = holder is { Id: { } id } ? await accounts.GetCharactersAsync(id, cancellationToken) : [player];
		return new Scope(holder?.Id, holder, rules, await OverridesEveryAsync(executor, characters));
	}

	/// <summary>
	/// Whether <c>game.override</c> reaches every one of <paramref name="characters"/>. A value belongs to the account, not
	/// to the character it was named through, so an override that reaches only a low-ranked alt does not open it.
	/// </summary>
	private static async ValueTask<bool> OverridesEveryAsync(AnySharpObject executor, IEnumerable<SharpPlayer> characters)
	{
		foreach (var character in characters)
			if (!await executor.CanOver(PortalPermission.Override, character))
				return false;
		return true;
	}

	/// <summary>A lock as given, bound to dbrefs as <c>@lock</c> binds one; empty stays empty, and an invalid one is refused.</summary>
	private async ValueTask<Found<string>> BoundLockAsync(AnySharpObject executor, AnySharpObject lockee, string lockString,
		CancellationToken cancellationToken)
	{
		var given = lockString.Trim();
		if (given.Length == 0) return string.Empty;
		return await locks.BindAsync(given, executor, cancellationToken) is string bound && locks.Validate(bound, lockee)
			? bound
			: new NotFound();
	}
}
