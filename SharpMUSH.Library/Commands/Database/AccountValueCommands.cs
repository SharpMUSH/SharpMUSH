using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Keeps <paramref name="Json"/> in the account's value <paramref name="Name"/>, if its locks are still
/// <paramref name="Expected"/>. See <see cref="IAccountValueStore.SetAccountValueAsync"/>. The reads are not cached, so
/// nothing is invalidated.
/// </summary>
public record SetAccountValueCommand(string AccountId, string Name, string Json, Found<AccountValueLocks> Expected)
	: ICommand<AccountValueWrite>;

/// <summary>Removes the account's value <paramref name="Name"/>. See <see cref="IAccountValueStore.RemoveAccountValueAsync"/>.</summary>
public record RemoveAccountValueCommand(string AccountId, string Name, AccountValueLocks Expected) : ICommand<AccountValueChange>;

/// <summary>Sets the value's own locks. See <see cref="IAccountValueStore.SetAccountValueLocksAsync"/>.</summary>
public record SetAccountValueLocksCommand(string AccountId, string Name, AccountValueLocks Expected, AccountValueLocks Locks)
	: ICommand<AccountValueChange>;

/// <summary>Keeps or removes a rule. See <see cref="IAccountValueStore.SetAccountValueRuleAsync"/>.</summary>
public record SetAccountValueRuleCommand(string Pattern, AccountValueLocks Locks) : ICommand;
