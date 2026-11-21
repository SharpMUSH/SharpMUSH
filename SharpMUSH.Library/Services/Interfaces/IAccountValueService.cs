using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// One account value as a reader asked for it: the value, <see cref="NotFound"/> when the account holds no value by
/// that name (or the player is on no account), or the refusal as an <see cref="Error{T}"/> of softcode error text.
/// </summary>
public union AccountValueRead(AccountValue, NotFound, Error<string>);

/// <summary>
/// Account values (JSON kept on a web account) as softcode reaches them, through a character on the account. Every
/// check lives here, so the functions and <c>@accval</c> agree.
/// <para>
/// Viewing, writing and deleting a value each pass a lock: the value's own lock of that kind, else that of the most
/// specific game-wide rule matching its name (<see cref="AccountValueRules"/>), else the default: <c>players.view</c>
/// or a character on the account to view, <c>players.moderate</c> to write or delete. A lock is evaluated with the
/// player named as the locked object and the caller as the unlocker. <c>game.override</c> over every character on the
/// account passes every lock. Setting a value's own locks needs <c>players.moderate</c> and its write lock; setting a
/// rule needs <c>config.admin</c>.
/// </para>
/// Every <see cref="Error{T}"/> carries the <c>#-1</c> text a function returns.
/// </summary>
public interface IAccountValueService
{
	/// <summary>The value <paramref name="name"/> on the account holding <paramref name="player"/>.</summary>
	ValueTask<AccountValueRead> GetAsync(AnySharpObject executor, SharpPlayer player, string name,
		CancellationToken cancellationToken = default);

	/// <summary>The values on the account holding <paramref name="player"/> the executor may view, by name.</summary>
	ValueTask<IReadOnlyList<AccountValue>> ListAsync(AnySharpObject executor, SharpPlayer player,
		CancellationToken cancellationToken = default);

	/// <summary>Keeps <paramref name="json"/> under <paramref name="name"/>, or removes the value when it is empty or blank.</summary>
	ValueTask<Result<Success>> SetAsync(AnySharpObject executor, SharpPlayer player, string name, string json,
		CancellationToken cancellationToken = default);

	/// <summary>The value's own lock of <paramref name="kind"/>, empty when it sets none. Needs its view lock.</summary>
	ValueTask<Result<string>> GetLockAsync(AnySharpObject executor, SharpPlayer player, string name, string kind,
		CancellationToken cancellationToken = default);

	/// <summary>Sets the value's own lock of <paramref name="kind"/>, or clears it when <paramref name="lockString"/> is empty.</summary>
	ValueTask<Result<Success>> SetLockAsync(AnySharpObject executor, SharpPlayer player, string name, string kind, string lockString,
		CancellationToken cancellationToken = default);

	/// <summary>Every game-wide rule, by pattern.</summary>
	ValueTask<IReadOnlyList<AccountValueRule>> ListRulesAsync(CancellationToken cancellationToken = default);

	/// <summary>The lock of <paramref name="kind"/> the rule for <paramref name="pattern"/> sets, empty when none.</summary>
	ValueTask<Result<string>> GetRuleAsync(string pattern, string kind, CancellationToken cancellationToken = default);

	/// <summary>Sets the lock of <paramref name="kind"/> on the rule for <paramref name="pattern"/>; empty clears it.</summary>
	ValueTask<Result<Success>> SetRuleAsync(AnySharpObject executor, string pattern, string kind, string lockString,
		CancellationToken cancellationToken = default);
}
