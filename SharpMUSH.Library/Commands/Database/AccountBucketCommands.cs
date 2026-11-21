using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Keeps <paramref name="Json"/> in the account's bucket <paramref name="Name"/>, both already checked by
/// <see cref="AccountBuckets"/>. See <see cref="IAccountBucketStore.SetAccountBucketAsync"/>. The bucket reads
/// are not cached, so nothing is invalidated.
/// </summary>
public record SetAccountBucketCommand(string AccountId, string Name, string Json) : ICommand<AccountBucketWrite>;

/// <summary>Removes the account's bucket <paramref name="Name"/>; false when there was none.</summary>
public record RemoveAccountBucketCommand(string AccountId, string Name) : ICommand<bool>;
