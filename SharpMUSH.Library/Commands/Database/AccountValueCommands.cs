using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Keeps <paramref name="Json"/> in the account's value <paramref name="Name"/> for <paramref name="Writer"/> (an objid).
/// See <see cref="IAccountValueStore.SetAccountValueAsync"/>. The reads are not cached, so nothing is invalidated.
/// </summary>
public record SetAccountValueCommand(string AccountId, string Name, string Json, string Writer, bool OverrideLock)
	: ICommand<AccountValueWrite>;

/// <summary>Removes the account's value <paramref name="Name"/>. See <see cref="IAccountValueStore.RemoveAccountValueAsync"/>.</summary>
public record RemoveAccountValueCommand(string AccountId, string Name, string Writer, bool OverrideLock) : ICommand<AccountValueChange>;

/// <summary>Locks or unlocks the account's value <paramref name="Name"/>. See <see cref="IAccountValueStore.SetAccountValueLockAsync"/>.</summary>
public record SetAccountValueLockCommand(string AccountId, string Name, string Writer, bool Locked, bool OverrideLock)
	: ICommand<AccountValueChange>;
