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
/// check lives here, so the functions and <c>@accval</c> agree: reading needs <c>players.view</c> or a character on the
/// account; writing and locking need <c>players.moderate</c>. A locked value then changes only for its owner (the
/// owner of whoever locked it) and for <c>game.override</c>, as <c>@atrlock</c> works on attributes.
/// Every <see cref="Error{T}"/> carries the <c>#-1</c> text a function returns.
/// </summary>
public interface IAccountValueService
{
	/// <summary>The value <paramref name="name"/> on the account holding <paramref name="player"/>.</summary>
	ValueTask<AccountValueRead> GetAsync(AnySharpObject executor, SharpPlayer player, string name,
		CancellationToken cancellationToken = default);

	/// <summary>Every value on the account holding <paramref name="player"/>, by name; empty when it is on no account.</summary>
	ValueTask<Result<IReadOnlyList<AccountValue>>> ListAsync(AnySharpObject executor, SharpPlayer player,
		CancellationToken cancellationToken = default);

	/// <summary>Keeps <paramref name="json"/> under <paramref name="name"/>, or removes the value when it is empty or blank.</summary>
	ValueTask<Result<Success>> SetAsync(AnySharpObject executor, SharpPlayer player, string name, string json,
		CancellationToken cancellationToken = default);

	/// <summary>Locks or unlocks the value <paramref name="name"/>. Locking makes the executor's owner the value's owner.</summary>
	ValueTask<Result<Success>> LockAsync(AnySharpObject executor, SharpPlayer player, string name, bool locked,
		CancellationToken cancellationToken = default);
}
