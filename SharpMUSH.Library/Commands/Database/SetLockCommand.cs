using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetLockCommand(SharpObject Target, string LockName, string LockString, AnySharpObject Executor) : ICommand<Result<Success>>, ICacheInvalidating
{
	/// <summary>Explicit flags for validated restoration or cloning; otherwise preserve existing flags.</summary>
	public Services.LockService.LockFlags? Flags { get; init; }

	public DBRef? Creator { get; init; }
	public bool PreserveCreator { get; init; }

	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.DBRef)];
	public string[] CacheTags => [];
}

public record UnsetLockCommand(SharpObject Target, string LockName, AnySharpObject Executor) : ICommand<Result<Success>>, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.DBRef)];
	public string[] CacheTags => [];
}

public record SetLockFlagsCommand(SharpObject Target, string LockName, Services.LockService.LockFlags Flags,
	bool Clear, AnySharpObject Executor) : ICommand<Result<Success>>, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.DBRef)];
	public string[] CacheTags => [];
}

/// <summary>Copies a persisted expression without resolving names or dangling references anew.</summary>
public record CopyLockCommand(SharpObject Source, SharpObject Target, string LockName, AnySharpObject Executor)
	: ICommand<Result<Success>>, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.DBRef)];
	public string[] CacheTags => [];
}
